using System.Text.RegularExpressions;

namespace Server.Features.Logs.Shared;

public static partial class EndpointNormalizer
{
    public static string Normalize(string? uriStem) => Normalize(uriStem.AsSpan());

    public static string Normalize(ReadOnlySpan<char> uriStem)
    {
        uriStem = uriStem.Trim();
        if (uriStem.IsEmpty)
        {
            return "/";
        }

        var queryIndex = uriStem.IndexOf('?');
        if (queryIndex >= 0)
        {
            uriStem = uriStem[..queryIndex];
        }

        List<string> segments = [];
        foreach (var range in uriStem.Split('/'))
        {
            var segment = uriStem[range];
            if (segment.IsEmpty)
            {
                continue;
            }

            if (Guid.TryParse(segment, out _) || NumericSegmentRegex().IsMatch(segment))
            {
                segments.Add(":id");
            }
            else
            {
                segments.Add(segment.ToString());
            }
        }

        if (segments.Count == 0)
        {
            return "/";
        }

        return "/" + string.Join('/', segments);
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumericSegmentRegex();
}
