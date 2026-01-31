using System.Text;

namespace Server.Features.Logs.Shared;

public static class CsvParser
{
    public static IReadOnlyList<string> ParseLine(string line)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(line))
        {
            return results;
        }

        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];

            if (current == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                results.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        results.Add(builder.ToString());
        return results;
    }
}
