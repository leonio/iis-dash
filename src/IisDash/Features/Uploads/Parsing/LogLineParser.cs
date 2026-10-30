using System.Globalization;
using System.Text;

namespace IisDash.Features.Uploads.Parsing;

/// <summary>One request, as read from a log line.</summary>
public sealed class LogRecord
{
    public long? Ts { get; set; }
    public string? ClientIp { get; set; }
    public string? Username { get; set; }
    public string? Method { get; set; }
    public string? UriStem { get; set; }
    public string? UriQuery { get; set; }
    public int? Status { get; set; }
    public int? SubStatus { get; set; }
    public int? Win32Status { get; set; }
    public long? BytesSent { get; set; }
    public long? BytesReceived { get; set; }
    public long? TimeTakenMs { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public string? Host { get; set; }
    public string? ServerIp { get; set; }
    public int? ServerPort { get; set; }
    public string? ProtocolVersion { get; set; }
    public string? SiteName { get; set; }
    public string? ServerName { get; set; }
    public string? ServiceName { get; set; }
    public string? Cookie { get; set; }
}

/// <summary>What a line turned out to be: a request, a header or comment (skipped), or something that could not be read.</summary>
public readonly record struct ParsedLine(LogRecord? Record, bool Skipped, string? Error)
{
    public static ParsedLine Parsed(LogRecord record) => new(record, false, null);
    public static readonly ParsedLine Skip = new(null, true, null);
    public static ParsedLine Failed(string error) => new(null, false, error);
}

/// <summary>
/// Reads one file's lines in order. W3C logs say their fields in a <c>#Fields:</c> line (and may say them again
/// further down, after a restart); IIS's CSV format has a fixed layout, or a <c>Date,Time,...</c> header naming it.
/// </summary>
public sealed class LogLineParser(LogFormat format)
{
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff",
        "MM/dd/yyyy HH:mm:ss",
        "M/d/yyyy H:mm:ss",
        "MM/dd/yy HH:mm:ss",
        "M/d/yy H:mm:ss",
        "MM-dd-yy HH:mm:ss",
    ];

    /// <summary>IIS's own format, as IIS writes it: no header, a trailing comma.</summary>
    private static readonly string[] IisNativeFields =
    [
        "Client IP Address", "User Name", "Date", "Time", "Service Name", "Server Name", "Server IP Address",
        "Time Taken", "Bytes Received", "Bytes Sent", "Protocol Status", "Win32 Status", "Method", "URI Stem", "URI Query",
    ];

    /// <summary>The same kind of fields with the date first, as tools that export IIS logs to CSV often write them.</summary>
    private static readonly string[] DateFirstFields =
    [
        "Date", "Time", "Client IP Address", "User Name", "Service Name", "Server Name", "Server IP Address", "Server Port",
        "Method", "URI Stem", "URI Query", "Protocol Status", "Win32 Status", "Bytes Sent", "Bytes Received", "Time Taken",
        "Protocol Version", "Host", "User Agent", "Cookie", "Referer",
    ];

    private List<string> _w3cFields = [];
    private string[]? _iisKeys; // the layout's field names as keys (see Key)

    public LogFormat Format => format;

    /// <summary>For a headerless IIS-format line, which layout it is in; null if it is in neither.</summary>
    public static string[]? HeaderlessIisFields(IReadOnlyList<string> fields)
    {
        if (fields.Count >= 15 && fields[2].Contains('/') && fields[3].Contains(':'))
        {
            return IisNativeFields;
        }

        return fields.Count >= DateFirstFields.Length && fields[1].Contains(':') && (fields[0].Contains('/') || fields[0].Contains('-'))
            ? DateFirstFields
            : null;
    }

    public ParsedLine Parse(string line) => format switch
    {
        LogFormat.W3C => ParseW3c(line),
        LogFormat.Iis => ParseIis(line),
        _ => ParsedLine.Failed("Unsupported format."),
    };

    private ParsedLine ParseW3c(string line)
    {
        if (line.Length == 0)
        {
            return ParsedLine.Skip;
        }

        if (line[0] == '#' || line[0] == '\uFEFF')
        {
            var directive = line.TrimStart('\uFEFF');
            if (directive.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
            {
                _w3cFields = [.. directive[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }

            return ParsedLine.Skip;
        }

        if (_w3cFields.Count == 0)
        {
            return ParsedLine.Failed("Missing #Fields header.");
        }

        var span = line.AsSpan();
        var count = span.Count(' ') + 1;
        if (count != _w3cFields.Count)
        {
            return ParsedLine.Failed($"{count} fields where the #Fields header names {_w3cFields.Count}.");
        }

        var record = new LogRecord();
        string? date = null, time = null;
        var i = 0;
        foreach (var range in span.Split(' '))
        {
            ApplyW3c(record, _w3cFields[i++], Value(span[range]), ref date, ref time);
        }

        record.Ts = Timestamp(date, time);
        return ParsedLine.Parsed(record);
    }

    private ParsedLine ParseIis(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return ParsedLine.Skip;
        }

        var values = CsvParser.ParseLine(line.AsSpan().TrimStart('\uFEFF'));
        if (_iisKeys is null)
        {
            if (values.Count >= 2 && values[0].Trim().Equals("Date", StringComparison.OrdinalIgnoreCase)
                                  && values[1].Trim().Equals("Time", StringComparison.OrdinalIgnoreCase))
            {
                _iisKeys = [.. values.Select(Key)];
                return ParsedLine.Skip;
            }

            _iisKeys = [.. (HeaderlessIisFields(values) ?? DateFirstFields).Select(Key)];
        }

        // IIS ends each line with a comma: one empty field more than it names.
        if (values.Count == _iisKeys.Length + 1 && values[^1].Trim().Length == 0)
        {
            values.RemoveAt(values.Count - 1);
        }

        if (values.Count != _iisKeys.Length)
        {
            return ParsedLine.Failed($"{values.Count} fields where the layout has {_iisKeys.Length}.");
        }

        var record = new LogRecord();
        string? date = null, time = null;
        for (var i = 0; i < values.Count; i++)
        {
            ApplyIis(record, _iisKeys[i], Value(values[i].AsSpan().Trim()), ref date, ref time);
        }

        record.Ts = Timestamp(date, time);
        return ParsedLine.Parsed(record);
    }

    private static void ApplyW3c(LogRecord r, string field, string? value, ref string? date, ref string? time)
    {
        switch (field)
        {
            case "date": date = value; break;
            case "time": time = value; break;
            case "c-ip": r.ClientIp = value; break;
            case "cs-username": r.Username = value; break;
            case "cs-method": r.Method = value; break;
            case "cs-uri-stem": r.UriStem = value; break;
            case "cs-uri-query": r.UriQuery = value; break;
            case "sc-status": r.Status = Int(value); break;
            case "sc-substatus": r.SubStatus = Int(value); break;
            case "sc-win32-status": r.Win32Status = Int(value); break;
            case "sc-bytes": r.BytesSent = Long(value); break;
            case "cs-bytes": r.BytesReceived = Long(value); break;
            case "time-taken": r.TimeTakenMs = Long(value); break;
            case "cs(User-Agent)" or "cs(user-agent)": r.UserAgent = value?.Replace('+', ' '); break;
            case "cs(Referer)" or "cs(referer)": r.Referer = value; break;
            case "cs(Cookie)" or "cs(cookie)": r.Cookie = value; break;
            case "cs-host": r.Host = value; break;
            case "s-ip": r.ServerIp = value; break;
            case "s-port": r.ServerPort = Int(value); break;
            case "cs-version": r.ProtocolVersion = value; break;
            case "s-sitename": r.SiteName = value; break;
            case "s-computername": r.ServerName = value; break;
        }
    }

    private static void ApplyIis(LogRecord r, string key, string? value, ref string? date, ref string? time)
    {
        switch (key)
        {
            case "date": date = value; break;
            case "time": time = value; break;
            case "clientipaddress" or "clientip": r.ClientIp = value; break;
            case "username": r.Username = value; break;
            case "servicename": r.ServiceName = value; break;
            case "servername": r.ServerName = value; break;
            case "serveripaddress": r.ServerIp = value; break;
            case "serverport": r.ServerPort = Int(value); break;
            case "method": r.Method = value; break;
            case "uristem": r.UriStem = value; break;
            case "uriquery": r.UriQuery = value; break;
            case "protocolstatus": r.Status = Int(value); break;
            case "win32status": r.Win32Status = Int(value); break;
            case "bytessent": r.BytesSent = Long(value); break;
            case "bytesreceived": r.BytesReceived = Long(value); break;
            case "timetaken": r.TimeTakenMs = Long(value); break;
            case "protocolversion": r.ProtocolVersion = value; break;
            case "host": r.Host = value; break;
            case "useragent": r.UserAgent = value; break;
            case "cookie": r.Cookie = value; break;
            case "referer": r.Referer = value; break;
        }
    }

    /// <summary>Seconds since 1970 on the log's own clock (see schema.sql), or null if the date or time is missing or unreadable.</summary>
    private static long? Timestamp(string? date, string? time)
    {
        if (date is null || time is null)
        {
            return null;
        }

        return DateTime.TryParseExact($"{date} {time}", TimestampFormats, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? (long)(parsed - DateTime.UnixEpoch).TotalSeconds
            : null;
    }

    private static string? Value(ReadOnlySpan<char> value) =>
        value.IsWhiteSpace() || value is "-" ? null : value.ToString();

    private static int? Int(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static long? Long(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>A header name as a key: letters and digits, lower case ("Client IP Address" → clientipaddress).</summary>
    private static string Key(string field)
    {
        var builder = new StringBuilder(field.Length);
        foreach (var c in field)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
