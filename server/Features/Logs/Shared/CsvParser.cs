using System.Text;

namespace Server.Features.Logs.Shared;

public static class CsvParser
{
    public static IReadOnlyList<string> ParseLine(string line) => ParseLine(line.AsSpan());

    public static IReadOnlyList<string> ParseLine(ReadOnlySpan<char> line)
    {
        if (line.IsWhiteSpace())
        {
            return [];
        }

        List<string> results = [];
        var inQuotes = false;
        var start = 0;
        var hasQuotesInField = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];

            if (current == '"')
            {
                hasQuotesInField = true;
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                results.Add(ExtractField(line[start..i], hasQuotesInField));
                start = i + 1;
                hasQuotesInField = false;
                continue;
            }
        }

        results.Add(ExtractField(line[start..], hasQuotesInField));
        return results;
    }

    private static string ExtractField(ReadOnlySpan<char> field, bool hasQuotes)
    {
        if (field.IsEmpty)
        {
            return string.Empty;
        }

        if (!hasQuotes)
        {
            return field.ToString();
        }

        // StringBuilder is OK, but only needed it we have to do deal with stripping quotes and all that...
        var builder = new StringBuilder(field.Length);
        var inQuotes = false;

        for (var i = 0; i < field.Length; i++)
        {
            var current = field[i];

            if (current == '"')
            {
                if (inQuotes && i + 1 < field.Length && field[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
