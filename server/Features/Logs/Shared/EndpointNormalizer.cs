using System.Text.RegularExpressions;

namespace Server.Features.Logs.Shared;

public static partial class EndpointNormalizer
{
    private static readonly char[] SegmentSeparator = ['/'];

    public static string Normalize(string? uriStem, string? uriQuery = null)
    {
        if (string.IsNullOrWhiteSpace(uriStem))
        {
            return "/";
        }

        var path = uriStem.Trim();
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var segments = path.Split(SegmentSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return "/";
        }

        for (var i = 0; i < segments.Length; i += 1)
        {
            var segment = segments[i];
            if (Guid.TryParse(segment, out _))
            {
                segments[i] = ":id";
                continue;
            }

            if (NumericSegmentRegex().IsMatch(segment))
            {
                segments[i] = ":id";
            }
        }

        return "/" + string.Join('/', segments);
    }

    [GeneratedRegex("^\\d+$")]
    private static partial Regex NumericSegmentRegex();
}
