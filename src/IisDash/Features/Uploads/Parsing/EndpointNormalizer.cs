using System.Text.RegularExpressions;

namespace IisDash.Features.Uploads.Parsing;

/// <summary>
/// A URI stem as an endpoint: ids folded into <c>:id</c> so that <c>/orders/42</c> and <c>/orders/43</c> count as one.
/// Numbers and GUIDs are ids; the query string is dropped; and the case, as IIS ignores it, so <c>/Orders/List.aspx</c>
/// and <c>/orders/list.aspx</c> are one endpoint too.
/// </summary>
public static partial class EndpointNormalizer
{
    public static string Normalize(string? uriStem) => Normalize(uriStem.AsSpan());

    public static string Normalize(ReadOnlySpan<char> uriStem)
    {
        uriStem = uriStem.Trim();
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

            segments.Add(Guid.TryParse(segment, out _) || NumericSegment().IsMatch(segment) ? ":id" : segment.ToString().ToLowerInvariant());
        }

        return segments.Count == 0 ? "/" : "/" + string.Join('/', segments);
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumericSegment();
}
