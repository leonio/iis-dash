using System.Globalization;
using System.IO.Compression;
using System.Text;
using IisDash.Features.Uploads.Parsing;
using IisDash.Shared.Data;
using Microsoft.Data.Sqlite;

namespace IisDash.Features.Uploads;

/// <summary>
/// Reads uploaded log files into the database: one transaction per file, the lines streamed through the parser and
/// inserted with one prepared statement, so a file of any size never sits in memory. A file with the same name as one
/// already there replaces it.
/// </summary>
public sealed class LogIngestion(Db db, AppOptions options, ILogger<LogIngestion> logger)
{
    /// <summary>Lines that could not be read are counted; this many per file are also kept, to show what was wrong.</summary>
    private const int FailedLinesKept = 500;

    public enum Outcome { Ingested, Unsupported, Empty, TooLarge }

    public sealed record FileOutcome(
        string Name,
        Outcome Outcome,
        LogFormat Format,
        int Parsed = 0,
        int Failed = 0,
        int Skipped = 0,
        long? FirstTs = null,
        long? LastTs = null,
        bool Replaced = false);

    /// <summary>One log file from an upload: a plain file, or an entry in a .zip, or the content of a .gz.</summary>
    public sealed record Incoming(string Name, long Size, Func<Stream> Open);

    /// <summary>
    /// The log files in an upload: zip archives are opened (every file in them, in any folder), .gz files unpacked on
    /// the fly, anything else taken as a log file.
    /// </summary>
    public static IEnumerable<Incoming> Expand(IFormFile file)
    {
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension == ".zip")
        {
            var archive = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0).OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                yield return new Incoming(entry.FullName, entry.Length, entry.Open);
            }
        }
        else if (extension == ".gz")
        {
            yield return new Incoming(Path.GetFileNameWithoutExtension(file.FileName), file.Length,
                () => new GZipStream(file.OpenReadStream(), CompressionMode.Decompress));
        }
        else
        {
            yield return new Incoming(file.FileName, file.Length, file.OpenReadStream);
        }
    }

    /// <summary>The format, from the file's first line, without storing anything.</summary>
    public async Task<(LogFormat Format, bool TooLarge)> PrecheckAsync(Incoming file, CancellationToken ct)
    {
        if (file.Size > options.MaxFileBytes)
        {
            return (LogFormat.Unknown, true);
        }

        await using var stream = file.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (LogFormatDetector.Detect(await reader.ReadLineAsync(ct)), false);
    }

    public async Task<FileOutcome> IngestAsync(Incoming file, CancellationToken ct)
    {
        if (file.Size > options.MaxFileBytes)
        {
            return new FileOutcome(file.Name, Outcome.TooLarge, LogFormat.Unknown);
        }

        await using var stream = file.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        string? first = await reader.ReadLineAsync(ct);
        if (first is null)
        {
            return new FileOutcome(file.Name, Outcome.Empty, LogFormat.Unknown);
        }

        var format = LogFormatDetector.Detect(first);
        if (format == LogFormat.Unknown)
        {
            logger.LogWarning("{File}: not a W3C or IIS log.", file.Name);
            return new FileOutcome(file.Name, Outcome.Unsupported, format);
        }

        using var conn = db.Open();
        using var tx = conn.BeginTransaction();

        bool replaced = Db.Execute(conn, "DELETE FROM log_files WHERE name = $name", new Args { ["$name"] = file.Name }) > 0;
        // No RETURNING: the SQLite in older Windows releases predates it.
        Db.Execute(conn, "INSERT INTO log_files (name, format, size_bytes, uploaded_at) VALUES ($name, $format, $size, $at)",
            new Args
            {
                ["$name"] = file.Name,
                ["$format"] = format == LogFormat.W3C ? "W3C" : "IIS",
                ["$size"] = file.Size,
                ["$at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            });
        long fileId = (long)Db.Command(conn, "SELECT last_insert_rowid()", new Args()).ExecuteScalar()!;

        using var insert = new RequestInsert(conn, fileId);
        using var failed = Db.Command(conn, "INSERT INTO failed_lines (file_id, line_no, text, error) VALUES ($file, $line, $text, $error)",
            new Args { ["$file"] = fileId, ["$line"] = 0, ["$text"] = "", ["$error"] = "" });

        var parser = new LogLineParser(format);
        int lineNo = 0, parsed = 0, failures = 0, skipped = 0;
        long? firstTs = null, lastTs = null;
        for (string? line = first; line is not null; line = await reader.ReadLineAsync(ct))
        {
            lineNo++;
            var result = parser.Parse(line);
            if (result.Record is { } record)
            {
                insert.Run(lineNo, record);
                parsed++;
                if (record.Ts is { } ts)
                {
                    firstTs = firstTs is null || ts < firstTs ? ts : firstTs;
                    lastTs = lastTs is null || ts > lastTs ? ts : lastTs;
                }
            }
            else if (result.Skipped)
            {
                skipped++;
            }
            else
            {
                if (failures++ < FailedLinesKept)
                {
                    failed.Parameters["$line"].Value = lineNo;
                    failed.Parameters["$text"].Value = line.Length > 4000 ? line[..4000] : line;
                    failed.Parameters["$error"].Value = result.Error;
                    failed.ExecuteNonQuery();
                }
            }
        }

        Db.Execute(conn,
            """
            UPDATE log_files SET total_lines = $total, parsed_lines = $parsed, failed_lines = $failed, skipped_lines = $skipped,
                                 first_ts = $first, last_ts = $last
            WHERE id = $id
            """,
            new Args
            {
                ["$id"] = fileId, ["$total"] = lineNo, ["$parsed"] = parsed, ["$failed"] = failures, ["$skipped"] = skipped,
                ["$first"] = firstTs, ["$last"] = lastTs,
            });
        tx.Commit();

        logger.LogInformation("{File}: {Parsed} requests, {Failed} lines unreadable.", file.Name, parsed, failures);
        return new FileOutcome(file.Name, Outcome.Ingested, format, parsed, failures, skipped, firstTs, lastTs, replaced);
    }

    /// <summary>The prepared INSERT for requests, its parameters set per row.</summary>
    private sealed class RequestInsert : IDisposable
    {
        private static readonly string[] Columns =
        [
            "file_id", "line_no", "ts", "method", "uri_stem", "uri_query", "endpoint", "is_static", "status", "sub_status", "win32_status",
            "time_taken_ms", "bytes_sent", "bytes_received", "client_ip", "username", "user_agent", "referer", "host",
            "server_ip", "server_port", "protocol_version", "site_name", "server_name", "service_name", "cookie",
        ];

        private readonly SqliteCommand _command;
        private readonly SqliteParameter[] _p;

        public RequestInsert(SqliteConnection conn, long fileId)
        {
            _command = conn.CreateCommand();
            _command.CommandText =
                $"INSERT INTO requests ({string.Join(", ", Columns)}) VALUES ({string.Join(", ", Columns.Select((_, i) => "$p" + i))})";
            _p = [.. Columns.Select((_, i) => _command.Parameters.Add(new SqliteParameter("$p" + i, DBNull.Value)))];
            _p[0].Value = fileId;
            _command.Prepare();
        }

        public void Run(int lineNo, LogRecord r)
        {
            _p[1].Value = lineNo;
            Set(2, r.Ts);
            Set(3, r.Method);
            Set(4, r.UriStem);
            Set(5, r.UriQuery);
            _p[6].Value = EndpointNormalizer.Normalize(r.UriStem);
            _p[7].Value = StaticFiles.IsStatic(r.UriStem) ? 1 : 0;
            Set(8, r.Status);
            Set(9, r.SubStatus);
            Set(10, r.Win32Status);
            Set(11, r.TimeTakenMs);
            Set(12, r.BytesSent);
            Set(13, r.BytesReceived);
            Set(14, r.ClientIp);
            Set(15, r.Username);
            Set(16, r.UserAgent);
            Set(17, r.Referer);
            Set(18, r.Host);
            Set(19, r.ServerIp);
            Set(20, r.ServerPort);
            Set(21, r.ProtocolVersion);
            Set(22, r.SiteName);
            Set(23, r.ServerName);
            Set(24, r.ServiceName);
            Set(25, r.Cookie);
            _command.ExecuteNonQuery();
        }

        private void Set(int i, string? value) => _p[i].Value = (object?)value ?? DBNull.Value;
        private void Set(int i, long? value) => _p[i].Value = value is { } v ? v : DBNull.Value;
        private void Set(int i, int? value) => _p[i].Value = value is { } v ? v : DBNull.Value;

        public void Dispose() => _command.Dispose();
    }
}
