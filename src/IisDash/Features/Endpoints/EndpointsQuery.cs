using IisDash.Features.Dashboard;
using IisDash.Shared.Data;
using IisDash.Shared.Filters;

namespace IisDash.Features.Endpoints;

/// <summary>One endpoint: its requests and how they went, and when they came (<see cref="Timeline"/>, a count per bucket).</summary>
public sealed record EndpointStats(
    string Endpoint, bool Static, long Count, long ClientErrors, long ServerErrors, double? AvgMs, long? MaxMs, long BytesSent,
    long Clients)
{
    public long[] Timeline { get; set; } = [];
    public double ErrorRate => Count == 0 ? 0 : (double)(ClientErrors + ServerErrors) / Count;
}

public sealed class EndpointsPageData
{
    public List<EndpointStats> Rows { get; init; } = [];
    public long Endpoints { get; init; }
    public long Requests { get; init; }
    public int Page { get; init; }
    public string Sort { get; init; } = "requests";
    public bool Descending { get; init; } = true;
    public long? FirstTs { get; init; }
    public long BucketSeconds { get; init; }
    public int Pages => (int)Math.Max(1, (Endpoints + EndpointsQuery.PageSize - 1) / EndpointsQuery.PageSize);
}

/// <summary>
/// Every endpoint (ids folded, query strings and case ignored), how busy, how often it fails, how fast, and when it is
/// hit: each row carries a timeline over the filter's time range, in up to <see cref="TimelineColumns"/> buckets.
/// </summary>
public static class EndpointsQuery
{
    public const int PageSize = 50;
    public const int TimelineColumns = 60;

    public static readonly Dictionary<string, (string Order, bool Descending)> Sorts = new()
    {
        ["requests"] = ("count(*)", true),
        ["errors"] = ("1.0 * sum(status >= 400 AND status < 600) / count(*)", true),
        ["avg"] = ("avg(time_taken_ms)", true),
        ["max"] = ("max(time_taken_ms)", true),
        ["sent"] = ("sum(bytes_sent)", true),
        ["clients"] = ("count(DISTINCT client_ip)", true),
        ["endpoint"] = ("endpoint", false),
    };

    public static EndpointsPageData Load(Db db, LogFilter filter, string? sort, string? dir, int page)
    {
        sort = sort is not null && Sorts.ContainsKey(sort) ? sort : "requests";
        bool descending = dir is null ? Sorts[sort].Descending : dir == "desc";
        var (where, args) = filter.Sql();
        using var conn = db.Open();

        var (endpoints, requests, first, last) = Db.Query(conn,
            $"SELECT count(DISTINCT endpoint), count(*), min(ts), max(ts) FROM requests {where}", args,
            r => (r.GetInt64(0), r.GetInt64(1), r.Long(2), r.Long(3)))[0];
        int pages = (int)Math.Max(1, (endpoints + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);

        var paged = new Args { ["$limit"] = PageSize, ["$offset"] = (long)(page - 1) * PageSize };
        foreach (var (key, value) in args) paged[key] = value;
        string order = descending ? "DESC" : "ASC";
        var rows = Db.Query(conn,
            $"""
            SELECT endpoint, max(is_static), count(*),
                   coalesce(sum(status >= 400 AND status < 500), 0), coalesce(sum(status >= 500 AND status < 600), 0),
                   avg(time_taken_ms), max(time_taken_ms), coalesce(sum(bytes_sent), 0), count(DISTINCT client_ip)
            FROM requests {where}
            GROUP BY endpoint
            ORDER BY {Sorts[sort].Order} IS NULL, {Sorts[sort].Order} {order}, count(*) DESC, endpoint
            LIMIT $limit OFFSET $offset
            """, paged,
            r => new EndpointStats(r.GetString(0), r.GetInt64(1) == 1, r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.Double(5), r.Long(6),
                r.GetInt64(7), r.GetInt64(8)));

        long bucket = 0;
        if (first is { } start && last is { } end && rows.Count > 0)
        {
            // The finest of the traffic chart's bucket widths that fits the range into the timeline's columns.
            bucket = DashboardQuery.BucketFor(start, end, TimelineColumns);
            long origin = start - start % bucket;
            int columns = (int)((end - origin) / bucket) + 1;
            var index = rows.Select((row, i) => (row, i)).ToDictionary(x => x.row.Endpoint, x => x.i, StringComparer.Ordinal);
            foreach (var row in rows) row.Timeline = new long[columns];

            var timeline = new Args { ["$origin"] = origin, ["$bucket"] = bucket };
            foreach (var (key, value) in args) timeline[key] = value;
            for (int i = 0; i < rows.Count; i++) timeline["$e" + i] = rows[i].Endpoint;
            string names = string.Join(", ", rows.Select((_, i) => "$e" + i));
            string condition = $"ts IS NOT NULL AND endpoint IN ({names})";
            foreach (var (endpoint, column, count) in Db.Query(conn,
                         $"""
                         SELECT endpoint, (ts - $origin) / $bucket, count(*)
                         FROM requests {(where.Length == 0 ? "WHERE " + condition : where + " AND " + condition)}
                         GROUP BY 1, 2
                         """, timeline, r => (r.GetString(0), r.GetInt32(1), r.GetInt64(2))))
            {
                rows[index[endpoint]].Timeline[column] = count;
            }

            first = origin;
        }

        return new EndpointsPageData
        {
            Rows = rows, Endpoints = endpoints, Requests = requests, Page = page, Sort = sort, Descending = descending,
            FirstTs = first, BucketSeconds = bucket,
        };
    }
}
