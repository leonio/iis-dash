using System.Globalization;
using System.Text;
using IisDash.Shared.Data;

namespace IisDash.Shared.Filters;

/// <summary>
/// Which requests a page is about. It lives in the URL (<c>?from=…&amp;status=5xx&amp;endpoint=/api/orders</c>), so a view
/// can be bookmarked, shared and gone back to; every chart's drill-down is a link to the same page with one more
/// value. The dashboard, the requests list and the export read the same filter, and turn it into the same SQL.
/// </summary>
public sealed class LogFilter
{
    /// <summary>The query keys, in the order they are written to a URL.</summary>
    public static readonly string[] Keys = ["from", "to", "file", "static", "method", "status", "path", "endpoint", "client", "user", "hour", "min_ms", "max_ms"];

    /// <summary>The keys the filter bar has a field for; the others are set by clicking into a chart, and show as chips.</summary>
    public static readonly string[] FieldKeys = ["from", "to", "file", "static", "method", "status", "path"];

    private readonly SortedDictionary<int, string> _values = [];

    public static LogFilter From(IQueryCollection query)
    {
        var filter = new LogFilter();
        foreach (string key in Keys)
        {
            string value = query[key].ToString().Trim();
            if (value.Length > 0 && Valid(key, value))
            {
                filter._values[Array.IndexOf(Keys, key)] = value;
            }
        }

        return filter;
    }

    public string? this[string key] => _values.GetValueOrDefault(Array.IndexOf(Keys, key));

    public bool IsEmpty => _values.Count == 0;

    public IEnumerable<(string Key, string Value)> Values => _values.Select(kv => (Keys[kv.Key], kv.Value));

    /// <summary>A copy with <paramref name="key"/> set (or removed, for null or empty).</summary>
    public LogFilter With(string key, string? value)
    {
        var copy = new LogFilter();
        foreach (var (k, v) in _values) copy._values[k] = v;
        int index = Array.IndexOf(Keys, key);
        if (string.IsNullOrEmpty(value)) copy._values.Remove(index);
        else copy._values[index] = value;
        return copy;
    }

    public LogFilter With(params (string Key, string? Value)[] values) =>
        values.Aggregate(this, (filter, kv) => filter.With(kv.Key, kv.Value));

    public LogFilter Without(string key) => With(key, null);

    /// <summary>The query string, without the "?": <c>from=2026-10-30T14%3A00&amp;status=5xx</c>.</summary>
    public string Query() => string.Join('&', Values.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));

    /// <summary>A link to <paramref name="path"/> with this filter.</summary>
    public string Url(string path) => IsEmpty ? path : path + "?" + Query();

    public long? FromTs => Time(this["from"]);
    public long? ToTs => Time(this["to"]);

    /// <summary>
    /// The WHERE clause (with "WHERE", or empty) and its parameters, over the requests table aliased as <c>r</c>
    /// when <paramref name="alias"/> is set.
    /// </summary>
    public (string Where, Args Args) Sql(string alias = "")
    {
        string c = alias.Length > 0 ? alias + "." : "";
        var conditions = new List<string>();
        var args = new Args();

        if (FromTs is { } from) { conditions.Add($"{c}ts >= $from"); args["$from"] = from; }
        if (ToTs is { } to) { conditions.Add($"{c}ts < $to"); args["$to"] = to; }
        if (this["file"] is { } file) { conditions.Add($"{c}file_id = $file"); args["$file"] = long.Parse(file, CultureInfo.InvariantCulture); }
        // Static files (scripts, styles, images, fonts, ScriptResource.axd: see StaticFiles.cs) left out, or alone.
        if (this["static"] is { } staticFiles) conditions.Add($"{c}is_static = {(staticFiles == "only" ? 1 : 0)}");
        if (this["method"] is { } method) { conditions.Add($"{c}method = $method"); args["$method"] = method; }
        if (this["status"] is { } status)
        {
            if (status == "other")
            {
                conditions.Add($"({c}status IS NULL OR {c}status < 200 OR {c}status >= 600)");
            }
            else if (status.EndsWith("xx", StringComparison.Ordinal))
            {
                int hundreds = (status[0] - '0') * 100;
                conditions.Add($"{c}status >= {hundreds} AND {c}status < {hundreds + 100}");
            }
            else
            {
                conditions.Add($"{c}status = $status");
                args["$status"] = int.Parse(status, CultureInfo.InvariantCulture);
            }
        }

        if (this["path"] is { } path)
        {
            conditions.Add($"{c}uri_stem LIKE $path ESCAPE '\\'");
            args["$path"] = "%" + path.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        if (this["endpoint"] is { } endpoint) { conditions.Add($"{c}endpoint = $endpoint"); args["$endpoint"] = endpoint; }
        if (this["client"] is { } client) { conditions.Add($"{c}client_ip = $client"); args["$client"] = client; }
        if (this["user"] is { } user) { conditions.Add($"{c}username = $user"); args["$user"] = user; }
        if (this["hour"] is { } hour)
        {
            conditions.Add($"{c}ts IS NOT NULL AND ({c}ts % 86400) / 3600 = $hour");
            args["$hour"] = int.Parse(hour, CultureInfo.InvariantCulture);
        }

        if (this["min_ms"] is { } min) { conditions.Add($"{c}time_taken_ms >= $min_ms"); args["$min_ms"] = long.Parse(min, CultureInfo.InvariantCulture); }
        if (this["max_ms"] is { } max) { conditions.Add($"{c}time_taken_ms < $max_ms"); args["$max_ms"] = long.Parse(max, CultureInfo.InvariantCulture); }

        return (conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions), args);
    }

    /// <summary>A datetime-local value (2026-10-30T14:05, seconds optional) as seconds on the log's clock.</summary>
    public static long? Time(string? value) =>
        value is not null && DateTime.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? (long)(time - DateTime.UnixEpoch).TotalSeconds
            : null;

    /// <summary>Seconds on the log's clock as the value the URL carries: 2026-10-30T14:05 (with seconds only when not zero).</summary>
    public static string TimeValue(long ts)
    {
        var time = DateTime.UnixEpoch.AddSeconds(ts);
        return time.ToString(time.Second == 0 ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Values that would not parse are dropped rather than failing the page: a hand-edited URL still shows something.</summary>
    private static bool Valid(string key, string value) => key switch
    {
        "from" or "to" => Time(value) is not null,
        "static" => value is "hide" or "only",
        "file" or "min_ms" or "max_ms" => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _),
        "hour" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int h) && h is >= 0 and < 24,
        "status" => value is "2xx" or "3xx" or "4xx" or "5xx" or "other"
                    || int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s is >= 100 and < 1000,
        "method" => value.Length <= 32 && value.All(char.IsAsciiLetterUpper),
        _ => value.Length <= 2048,
    };

    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var (key, value) in Values) text.Append(key).Append('=').Append(value).Append(' ');
        return text.ToString().TrimEnd();
    }
}
