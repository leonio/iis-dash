using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using Microsoft.Data.Sqlite;

namespace IisDash.Features.Dashboard;

/// <summary>Everything the dashboard shows, for one filter. Every figure is computed in SQLite; no rows come back but aggregates.</summary>
public sealed class DashboardData
{
    public long Total { get; init; }
    public long ClientErrors { get; init; }
    public long ServerErrors { get; init; }
    public long BytesSent { get; init; }
    public long? FirstTs { get; init; }
    public long? LastTs { get; init; }
    public double? AvgMs { get; init; }
    public long? P50 { get; init; }
    public long? P95 { get; init; }
    public long? P99 { get; init; }
    public long Timed { get; init; }

    public Traffic Traffic { get; init; } = new();
    public long[] Classes { get; init; } = new long[6]; // by status / 100: [0] other, [2] 2xx … [5] 5xx
    public List<(int? Code, long Count)> Codes { get; init; } = [];
    public List<Bucket> Latency { get; init; } = [];
    public List<EndpointRow> Endpoints { get; init; } = [];
    public List<ClientRow> Clients { get; init; } = [];
    public Heatmap Heatmap { get; init; } = new();

    public double Share(long count) => Total == 0 ? 0 : (double)count / Total;
}

/// <summary>Requests over time: equal buckets from the first to the last, empty ones included, counted by status class.</summary>
public sealed class Traffic
{
    public long BucketSeconds { get; init; }
    public List<(long Start, long[] ByClass)> Buckets { get; init; } = [];
    public long Max => Buckets.Count == 0 ? 0 : Buckets.Max(b => b.ByClass.Sum());
}

/// <summary>A latency bucket: from (inclusive) to (exclusive, null for the open last one), in milliseconds.</summary>
public sealed record Bucket(long From, long? To, long Count);

public sealed record EndpointRow(string Endpoint, long Count, long ClientErrors, long ServerErrors, double? AvgMs, long? MaxMs);

public sealed record ClientRow(string Ip, long Count, long Errors, long BytesSent);

/// <summary>The busiest endpoints by the hour of day: Cells[endpoint][hour].</summary>
public sealed class Heatmap
{
    public List<string> Endpoints { get; init; } = [];
    public long[][] Cells { get; init; } = [];
    public long Max => Cells.Length == 0 ? 0 : Cells.Max(row => row.Max());
}

public static class DashboardQuery
{
    /// <summary>Bucket widths for the traffic chart; the finest that gives no more than <see cref="MaxColumns"/> columns is used.</summary>
    private static readonly long[] BucketChoices = [60, 300, 600, 900, 1800, 3600, 2 * 3600, 3 * 3600, 6 * 3600, 12 * 3600, 86400, 7 * 86400];

    private const int MaxColumns = 120;

    /// <summary>Latency bucket edges, in milliseconds: roughly logarithmic, as latencies are.</summary>
    public static readonly long[] LatencyEdges = [0, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000];

    public const int HeatmapRows = 12;

    /// <summary>The dashboard's figures; <paramref name="top"/> is how many endpoints and clients to list (the export wants more).</summary>
    public static DashboardData Load(Db db, LogFilter filter, int top = HeatmapRows)
    {
        var (where, args) = filter.Sql();
        using var conn = db.Open();

        var totals = Db.Query(conn,
            $"""
            SELECT count(*),
                   coalesce(sum(status >= 400 AND status < 500), 0),
                   coalesce(sum(status >= 500 AND status < 600), 0),
                   coalesce(sum(bytes_sent), 0),
                   min(ts), max(ts), avg(time_taken_ms), count(time_taken_ms)
            FROM requests {where}
            """, args,
            r => (Total: r.GetInt64(0), Client: r.GetInt64(1), Server: r.GetInt64(2), Bytes: r.GetInt64(3),
                  First: r.Long(4), Last: r.Long(5), Avg: r.Double(6), Timed: r.GetInt64(7)))[0];

        if (totals.Total == 0)
        {
            return new DashboardData();
        }

        var (p50, p95, p99) = Percentiles(conn, where, args, totals.Timed);
        var endpoints = Endpoints(conn, where, args, Math.Max(top, HeatmapRows));

        var codes = Db.Query(conn, $"SELECT status, count(*) FROM requests {where} GROUP BY status ORDER BY 2 DESC", args,
            r => (Code: r.Int(0), Count: r.GetInt64(1)));
        var classes = new long[6];
        foreach (var (code, count) in codes)
        {
            classes[code is >= 200 and < 600 ? code.Value / 100 : 0] += count;
        }

        return new DashboardData
        {
            Total = totals.Total,
            ClientErrors = totals.Client,
            ServerErrors = totals.Server,
            BytesSent = totals.Bytes,
            FirstTs = totals.First,
            LastTs = totals.Last,
            AvgMs = totals.Avg,
            Timed = totals.Timed,
            P50 = p50,
            P95 = p95,
            P99 = p99,
            Traffic = Traffic(conn, where, args, totals.First, totals.Last),
            Classes = classes,
            Codes = codes,
            Latency = Latency(conn, where, args),
            Endpoints = endpoints,
            Clients = Clients(conn, where, args, top == HeatmapRows ? 10 : top),
            Heatmap = Heatmap(conn, where, args, [.. endpoints.Take(HeatmapRows).Select(e => e.Endpoint)]),
        };
    }

    /// <summary>The bucket width the traffic chart uses for a time range.</summary>
    public static long BucketFor(long first, long last, int columns = MaxColumns)
    {
        long span = Math.Max(1, last - first + 1);
        return BucketChoices.FirstOrDefault(b => span / b < columns, BucketChoices[^1]);
    }

    private static Traffic Traffic(SqliteConnection conn, string where, Args args, long? first, long? last)
    {
        if (first is null || last is null)
        {
            return new Traffic();
        }

        long bucket = BucketFor(first.Value, last.Value);
        var counts = new Dictionary<long, long[]>();
        var withBucket = new Args { ["$bucket"] = bucket };
        foreach (var (key, value) in args) withBucket[key] = value;
        foreach (var (start, cls, count) in Db.Query(conn,
                     $"""
                     SELECT ts - ts % $bucket, CASE WHEN status >= 200 AND status < 600 THEN status / 100 ELSE 0 END, count(*)
                     FROM requests {And(where, "ts IS NOT NULL")}
                     GROUP BY 1, 2
                     """, withBucket, r => (r.GetInt64(0), r.GetInt32(1), r.GetInt64(2))))
        {
            if (!counts.TryGetValue(start, out var byClass)) counts[start] = byClass = new long[6];
            byClass[cls] += count;
        }

        var buckets = new List<(long, long[])>();
        for (long start = first.Value - first.Value % bucket; start <= last.Value; start += bucket)
        {
            buckets.Add((start, counts.GetValueOrDefault(start) ?? new long[6]));
        }

        return new Traffic { BucketSeconds = bucket, Buckets = buckets };
    }

    /// <summary>The 50th, 95th and 99th percentile, read off the sorted times in one pass that stops at the 99th.</summary>
    private static (long?, long?, long?) Percentiles(SqliteConnection conn, string where, Args args, long count)
    {
        if (count == 0) return (null, null, null);
        long k50 = Rank(50), k95 = Rank(95), k99 = Rank(99);
        long? p50 = null, p95 = null, p99 = null;
        using var command = Db.Command(conn,
            $"SELECT time_taken_ms FROM requests {And(where, "time_taken_ms IS NOT NULL")} ORDER BY time_taken_ms", args);
        using var reader = command.ExecuteReader();
        for (long i = 0; reader.Read(); i++)
        {
            if (i == k50) p50 = reader.GetInt64(0);
            if (i == k95) p95 = reader.GetInt64(0);
            if (i == k99) { p99 = reader.GetInt64(0); break; }
        }

        return (p50, p95, p99);

        long Rank(int percentile) => (long)Math.Ceiling(percentile / 100.0 * count) - 1;
    }

    private static List<Bucket> Latency(SqliteConnection conn, string where, Args args)
    {
        var cases = string.Join(" ", LatencyEdges.Skip(1).Select((edge, i) => $"WHEN time_taken_ms < {edge} THEN {i}"));
        var counts = Db.Query(conn,
                $"SELECT CASE {cases} ELSE {LatencyEdges.Length - 1} END, count(*) FROM requests {And(where, "time_taken_ms IS NOT NULL")} GROUP BY 1",
                args, r => (r.GetInt32(0), r.GetInt64(1)))
            .ToDictionary(x => x.Item1, x => x.Item2);
        return [.. LatencyEdges.Select((edge, i) =>
            new Bucket(edge, i + 1 < LatencyEdges.Length ? LatencyEdges[i + 1] : null, counts.GetValueOrDefault(i)))];
    }

    private static List<EndpointRow> Endpoints(SqliteConnection conn, string where, Args args, int limit) => Db.Query(conn,
        $"""
        SELECT endpoint, count(*), coalesce(sum(status >= 400 AND status < 500), 0), coalesce(sum(status >= 500 AND status < 600), 0),
               avg(time_taken_ms), max(time_taken_ms)
        FROM requests {where}
        GROUP BY endpoint ORDER BY 2 DESC, 1 LIMIT {limit}
        """, args,
        r => new EndpointRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.Double(4), r.Long(5)));

    private static List<ClientRow> Clients(SqliteConnection conn, string where, Args args, int limit) => Db.Query(conn,
        $"""
        SELECT client_ip, count(*), coalesce(sum(status >= 400 AND status < 600), 0), coalesce(sum(bytes_sent), 0)
        FROM requests {And(where, "client_ip IS NOT NULL")}
        GROUP BY client_ip ORDER BY 2 DESC, 1 LIMIT {limit}
        """, args,
        r => new ClientRow(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3)));

    private static Heatmap Heatmap(SqliteConnection conn, string where, Args args, List<string> endpoints)
    {
        if (endpoints.Count == 0) return new Heatmap();
        var withEndpoints = new Args();
        foreach (var (key, value) in args) withEndpoints[key] = value;
        for (int i = 0; i < endpoints.Count; i++) withEndpoints["$e" + i] = endpoints[i];
        var names = string.Join(", ", endpoints.Select((_, i) => "$e" + i));

        var cells = endpoints.Select(_ => new long[24]).ToArray();
        var index = endpoints.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i, StringComparer.Ordinal);
        foreach (var (endpoint, hour, count) in Db.Query(conn,
                     $"""
                     SELECT endpoint, (ts % 86400) / 3600, count(*)
                     FROM requests {And(where, $"ts IS NOT NULL AND endpoint IN ({names})")}
                     GROUP BY 1, 2
                     """, withEndpoints, r => (r.GetString(0), r.GetInt32(1), r.GetInt64(2))))
        {
            cells[index[endpoint]][hour] = count;
        }

        return new Heatmap { Endpoints = endpoints, Cells = cells };
    }

    private static string And(string where, string condition) =>
        where.Length == 0 ? "WHERE " + condition : where + " AND " + condition;
}
