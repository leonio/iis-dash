namespace IisDash.Features.Uploads.Parsing;

/// <summary>The two formats IIS writes: W3C extended (space separated, a #Fields header) and its own CSV format.</summary>
public enum LogFormat
{
    Unknown = 0,
    W3C = 1,
    Iis = 2,
}

public static class LogFormatDetector
{
    private static readonly string[] W3cDirectives = ["#Software:", "#Version:", "#Date:", "#Fields:", "#Remark:", "#Start-Date:", "#End-Date:"];

    /// <summary>The format, from the first line of the file.</summary>
    public static LogFormat Detect(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return LogFormat.Unknown;
        }

        var trimmed = line.TrimStart('\uFEFF', ' ', '\t');
        if (trimmed.StartsWith('#'))
        {
            // A W3C directive; not any line that starts with "#" (a Markdown heading, a shell script).
            return W3cDirectives.Any(d => trimmed.StartsWith(d, StringComparison.OrdinalIgnoreCase)) ? LogFormat.W3C : LogFormat.Unknown;
        }

        if (!trimmed.Contains(','))
        {
            return LogFormat.Unknown;
        }

        var fields = CsvParser.ParseLine(trimmed);
        if (fields.Count >= 2
            && fields[0].Equals("Date", StringComparison.OrdinalIgnoreCase)
            && fields[1].Equals("Time", StringComparison.OrdinalIgnoreCase))
        {
            return LogFormat.Iis;
        }

        if (fields.Any(field => field.Contains("URI", StringComparison.OrdinalIgnoreCase))
            && fields.Any(field => field.Contains("Client", StringComparison.OrdinalIgnoreCase)))
        {
            return LogFormat.Iis;
        }

        // No header: IIS's own layout (client IP, user, date, time, ...), or the same fields starting with the date.
        return LogLineParser.HeaderlessIisFields(fields) is not null ? LogFormat.Iis : LogFormat.Unknown;
    }
}
