using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Data.Entities;

namespace Server.Features.Logs.Shared;

public sealed class LogIngestionService
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "MM/dd/yyyy HH:mm:ss",
        "M/d/yyyy H:mm:ss",
        "MM-dd-yy HH:mm:ss",
    ];

    private readonly IisLogDbContext _dbContext;
    private readonly ILogger<LogIngestionService> _logger;

    public LogIngestionService(IisLogDbContext dbContext, ILogger<LogIngestionService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IngestionSummary> IngestAsync(IEnumerable<IngestFile> files, CancellationToken cancellationToken)
    {
        var summary = new IngestionSummary();

        foreach (var file in files)
        {
            var result = await IngestFileAsync(file, cancellationToken);
            summary.FileResults.Add(result);
            summary.TotalFiles++;
            summary.TotalLines += result.TotalLines;
            summary.ParsedLines += result.ParsedLines;
            summary.FailedLines += result.FailedLines;
            summary.SkippedLines += result.SkippedLines;
            summary.UnsupportedFiles += result.Status == IngestionStatus.Unsupported ? 1 : 0;
            summary.RejectedFiles += result.Status == IngestionStatus.Rejected ? 1 : 0;
            summary.EmptyFiles += result.Status == IngestionStatus.Empty ? 1 : 0;
            summary.ProcessedFiles += result.Status == IngestionStatus.Processed ? 1 : 0;
        }

        return summary;
    }

    public async Task<PrecheckResult> PrecheckAsync(IngestFile file, CancellationToken cancellationToken)
    {
        if (file.Length > MaxFileSizeBytes)
        {
            return new PrecheckResult(false, LogFormat.Unknown, "File exceeds 10MB limit.");
        }

        using var reader = new StreamReader(file.Stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var firstLine = await reader.ReadLineAsync(cancellationToken);
        var format = LogFormatDetector.Detect(firstLine);

        if (format == LogFormat.Unknown)
        {
            return new PrecheckResult(false, format, "Unsupported or unknown format.");
        }

        return new PrecheckResult(true, format, "Format looks supported.");
    }

    private async Task<IngestionFileResult> IngestFileAsync(IngestFile file, CancellationToken cancellationToken)
    {
        if (file.Length > MaxFileSizeBytes)
        {
            return new IngestionFileResult(file.FileName, null, LogFormat.Unknown, 0, 0, 0, 0, IngestionStatus.Rejected,
                "File exceeds 10MB limit.");
        }

        using var reader = new StreamReader(file.Stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var firstLine = await reader.ReadLineAsync(cancellationToken);
        if (firstLine is null)
        {
            return new IngestionFileResult(file.FileName, null, LogFormat.Unknown, 0, 0, 0, 0, IngestionStatus.Empty,
                "File contains no lines.");
        }

        var format = LogFormatDetector.Detect(firstLine);
        if (format == LogFormat.Unknown)
        {
            _logger.LogWarning("Unsupported log format for file {FileName}.", file.FileName);
            return new IngestionFileResult(file.FileName, null, format, 0, 0, 0, 0, IngestionStatus.Unsupported,
                "Unsupported or unknown format.");
        }

        var fileHash = HashFileName(file.FileName);
        var existing = await _dbContext.LogFiles
            .SingleOrDefaultAsync(f => f.FileHash == fileHash, cancellationToken);

        if (existing is not null)
        {
            _dbContext.LogFiles.Remove(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var logFile = new LogFile
        {
            FileHash = fileHash,
            OriginalFileName = file.FileName,
            FileSizeBytes = file.Length,
            Format = format.ToString(),
            UploadedAt = DateTimeOffset.UtcNow,
        };

        List<LogRawLine> rawLines = [];
        List<LogEntry> entries = [];
        var failedLines = 0;
        var skippedLines = 0;

        var lineNumber = 1;
        var state = new ParserState(format, firstLine);
        ProcessLine(firstLine, logFile, lineNumber, state, rawLines, entries, ref failedLines, ref skippedLines);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            ProcessLine(line, logFile, lineNumber, state, rawLines, entries, ref failedLines, ref skippedLines);
        }

        logFile.TotalLines = lineNumber;
        logFile.ParsedLines = entries.Count;
        logFile.FailedLines = failedLines;
        logFile.SkippedLines = skippedLines;

        _dbContext.LogFiles.Add(logFile);
        _dbContext.LogRawLines.AddRange(rawLines);
        _dbContext.LogEntries.AddRange(entries);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new IngestionFileResult(file.FileName, fileHash, format, logFile.TotalLines, logFile.ParsedLines,
            logFile.FailedLines, logFile.SkippedLines, IngestionStatus.Processed, "Ingested.");
    }

    private static void ProcessLine(string line, LogFile logFile, int lineNumber, ParserState state,
        List<LogRawLine> rawLines, List<LogEntry> entries, ref int failedLines, ref int skippedLines)
    {
        var result = state.Format switch
        {
            LogFormat.W3C => ParseW3cLine(line, state),
            LogFormat.Iis => ParseIisLine(line, state),
            _ => ParseResult.Unsupported(),
        };

        var rawLine = new LogRawLine
        {
            LogFile = logFile,
            LineNumber = lineNumber,
            LineText = line,
            ParseStatus = result.Status,
            ParseError = result.Error,
            IsDataLine = result.IsDataLine,
        };

        rawLines.Add(rawLine);

        if (result.Entry is not null)
        {
            result.Entry.LogFile = logFile;
            result.Entry.LineNumber = lineNumber;
            entries.Add(result.Entry);
        }
        else if (result.Status == ParseStatus.Failed)
        {
            failedLines++;
        }
        else if (result.Status == ParseStatus.Skipped)
        {
            skippedLines++;
        }
    }

    private static ParseResult ParseW3cLine(string line, ParserState state)
    {
        if (line.StartsWith('#'))
        {
            if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
            {
                var fields = line.AsSpan(8).Trim();
                List<string> fieldList = [];
                foreach (var range in fields.Split(' '))
                {
                    var fieldSlice = fields[range].Trim();
                    if (!fieldSlice.IsEmpty)
                    {
                        fieldList.Add(fieldSlice.ToString());
                    }
                }
                state.W3cFields = fieldList;
            }

            return ParseResult.Skipped();
        }

        if (state.W3cFields.Count == 0)
        {
            return ParseResult.Failed("Missing #Fields header.");
        }

        var span = line.AsSpan();
        var partCount = 0;
        foreach (var _ in span.Split(' '))
        {
            partCount++;
        }

        if (partCount != state.W3cFields.Count)
        {
            return ParseResult.Failed("Field count does not match #Fields header.");
        }

        var entry = new LogEntry();
        string? dateText = null;
        string? timeText = null;

        var i = 0;
        foreach (var range in span.Split(' '))
        {
            var valueSpan = span[range];
            var fieldName = state.W3cFields[i];
            var value = NormalizeValue(valueSpan);
            ApplyW3cField(entry, fieldName, value, ref dateText, ref timeText);
            i++;
        }

        ApplyTimestamp(entry, dateText, timeText);
        entry.DateText = dateText;
        entry.TimeText = timeText;

        return ParseResult.Parsed(entry);
    }

    private static ParseResult ParseIisLine(string line, ParserState state)
    {
        var lineSpan = line.AsSpan();
        if (!state.IisHeaderInitialized)
        {
            var headerValues = CsvParser.ParseLine(lineSpan);
            if (IsIisHeader(headerValues))
            {
                state.IisHeaderInitialized = true;
                state.IisFields = headerValues.Select(value => value.Trim()).ToList();
                return ParseResult.Skipped();
            }

            state.IisHeaderInitialized = true;
            state.IisFields = [.. DefaultIisFields];
        }

        var values = CsvParser.ParseLine(lineSpan);
        if (values.Count != state.IisFields.Count)
        {
            return ParseResult.Failed("Field count does not match IIS header.");
        }

        var entry = new LogEntry();
        string? dateText = null;
        string? timeText = null;

        for (var i = 0; i < values.Count; i++)
        {
            var fieldName = state.IisFields[i];
            var value = NormalizeValue(values[i]);
            ApplyIisField(entry, fieldName, value, ref dateText, ref timeText);
        }

        ApplyTimestamp(entry, dateText, timeText);
        entry.DateText = dateText;
        entry.TimeText = timeText;

        return ParseResult.Parsed(entry);
    }

    private static bool IsIisHeader(IReadOnlyList<string> fields)
    {
        return fields.Count >= 2
            && fields[0].Equals("Date", StringComparison.OrdinalIgnoreCase)
            && fields[1].Equals("Time", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyW3cField(LogEntry entry, string fieldName, string? value,
        ref string? dateText, ref string? timeText)
    {
        switch (fieldName)
        {
            case "date":
                dateText = value;
                break;
            case "time":
                timeText = value;
                break;
            case "c-ip":
                entry.ClientIp = value;
                break;
            case "cs-username":
                entry.Username = value;
                break;
            case "cs-method":
                entry.Method = value;
                break;
            case "cs-uri-stem":
                entry.UriStem = value;
                break;
            case "cs-uri-query":
                entry.UriQuery = value;
                break;
            case "sc-status":
                entry.ProtocolStatus = TryParseInt(value);
                break;
            case "sc-substatus":
                entry.SubStatus = TryParseInt(value);
                break;
            case "sc-win32-status":
                entry.Win32Status = TryParseInt(value);
                break;
            case "sc-bytes":
                entry.BytesSent = TryParseLong(value);
                break;
            case "cs-bytes":
                entry.BytesReceived = TryParseLong(value);
                break;
            case "time-taken":
                entry.TimeTakenMs = TryParseLong(value);
                break;
            case "cs(User-Agent)":
            case "cs(user-agent)":
                entry.UserAgent = value;
                break;
            case "cs(Referer)":
            case "cs(referer)":
                entry.Referer = value;
                break;
            case "cs-host":
                entry.Host = value;
                break;
            case "s-ip":
                entry.ServerIp = value;
                break;
            case "s-port":
                entry.ServerPort = TryParseInt(value);
                break;
            case "cs-version":
                entry.ProtocolVersion = value;
                break;
            case "s-sitename":
                entry.SiteName = value;
                break;
            case "s-computername":
                entry.ServerName = value;
                break;
        }
    }

    private static void ApplyIisField(LogEntry entry, string fieldName, string? value,
        ref string? dateText, ref string? timeText)
    {
        var normalized = NormalizeHeader(fieldName);
        switch (normalized)
        {
            case "date":
                dateText = value;
                break;
            case "time":
                timeText = value;
                break;
            case "clientipaddress":
            case "clientip":
                entry.ClientIp = value;
                break;
            case "username":
                entry.Username = value;
                break;
            case "servicename":
                entry.ServiceName = value;
                break;
            case "servername":
                entry.ServerName = value;
                break;
            case "serveripaddress":
                entry.ServerIp = value;
                break;
            case "serverport":
                entry.ServerPort = TryParseInt(value);
                break;
            case "method":
                entry.Method = value;
                break;
            case "uristem":
                entry.UriStem = value;
                break;
            case "uriquery":
                entry.UriQuery = value;
                break;
            case "protocolstatus":
                entry.ProtocolStatus = TryParseInt(value);
                break;
            case "win32status":
                entry.Win32Status = TryParseInt(value);
                break;
            case "bytessent":
                entry.BytesSent = TryParseLong(value);
                break;
            case "bytesreceived":
                entry.BytesReceived = TryParseLong(value);
                break;
            case "timetaken":
                entry.TimeTakenMs = TryParseLong(value);
                break;
            case "protocolversion":
                entry.ProtocolVersion = value;
                break;
            case "host":
                entry.Host = value;
                break;
            case "useragent":
                entry.UserAgent = value;
                break;
            case "cookie":
                entry.Cookie = value;
                break;
            case "referer":
                entry.Referer = value;
                break;
        }
    }

    private static void ApplyTimestamp(LogEntry entry, string? dateText, string? timeText)
    {
        if (string.IsNullOrWhiteSpace(dateText) || string.IsNullOrWhiteSpace(timeText))
        {
            return;
        }

        var combined = $"{dateText} {timeText}";
        if (DateTimeOffset.TryParseExact(combined, TimestampFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var timestamp))
        {
            entry.Timestamp = timestamp;
            return;
        }

        if (DateTimeOffset.TryParse(combined, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
                out timestamp))
        {
            entry.Timestamp = timestamp;
        }
    }

    private static string? NormalizeValue(ReadOnlySpan<char> value)
    {
        if (value.IsWhiteSpace() || (value.Length == 1 && value[0] == '-'))
        {
            return null;
        }

        return value.ToString();
    }

    private static string? NormalizeValue(string? value) => NormalizeValue(value.AsSpan());

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static long? TryParseLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string NormalizeHeader(string field)
    {
        var builder = new StringBuilder();
        foreach (var character in field)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string HashFileName(string fileName)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(fileName);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class ParserState(LogFormat format, string firstLine)
    {
        public LogFormat Format { get; } = format;
        public List<string> W3cFields { get; set; } = [];
        public bool IisHeaderInitialized { get; set; }
        public List<string> IisFields { get; set; } = [];
        public string FirstLine { get; } = firstLine;
    }

    private sealed record ParseResult(bool IsDataLine, string Status, string? Error, LogEntry? Entry)
    {
        public static ParseResult Parsed(LogEntry entry) => new(true, ParseStatus.Parsed, null, entry);
        public static ParseResult Failed(string error) => new(true, ParseStatus.Failed, error, null);
        public static ParseResult Skipped() => new(false, ParseStatus.Skipped, null, null);
        public static ParseResult Unsupported() => new(false, ParseStatus.Failed, "Unsupported format.", null);
    }

    private static readonly string[] DefaultIisFields =
    [
        "Date",
        "Time",
        "Client IP Address",
        "User Name",
        "Service Name",
        "Server Name",
        "Server IP Address",
        "Server Port",
        "Method",
        "URI Stem",
        "URI Query",
        "Protocol Status",
        "Win32 Status",
        "Bytes Sent",
        "Bytes Received",
        "Time Taken",
        "Protocol Version",
        "Host",
        "User Agent",
        "Cookie",
        "Referer",
    ];

    public sealed record IngestFile(string FileName, long Length, Stream Stream);
    public sealed record PrecheckResult(bool Supported, LogFormat Format, string Message);
    public sealed record IngestionSummary
    {
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int UnsupportedFiles { get; set; }
        public int RejectedFiles { get; set; }
        public int EmptyFiles { get; set; }
        public int TotalLines { get; set; }
        public int ParsedLines { get; set; }
        public int FailedLines { get; set; }
        public int SkippedLines { get; set; }
        public List<IngestionFileResult> FileResults { get; set; } = [];
    }

    public sealed record IngestionFileResult(
        string FileName,
        string? FileHash,
        LogFormat Format,
        int TotalLines,
        int ParsedLines,
        int FailedLines,
        int SkippedLines,
        IngestionStatus Status,
        string Message);

    public enum IngestionStatus
    {
        Processed = 0,
        Unsupported = 1,
        Rejected = 2,
        Empty = 3,
    }

    public static class ParseStatus
    {
        public const string Parsed = "Parsed";
        public const string Failed = "Failed";
        public const string Skipped = "Skipped";
    }
}
