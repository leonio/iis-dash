namespace Server.Features.Logs.Shared;

public static class LogFormatDetector
{
    public static LogFormat Detect(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return LogFormat.Unknown;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith('#'))
        {
            return LogFormat.W3C;
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

        return LogFormat.Unknown;
    }
}
