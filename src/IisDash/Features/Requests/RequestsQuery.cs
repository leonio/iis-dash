using IisDash.Shared.Data;
using IisDash.Shared.Filters;

namespace IisDash.Features.Requests;

public sealed record RequestRow(
    long Id, long? Ts, string? Method, string? UriStem, string? UriQuery, int? Status, int? SubStatus,
    long? TimeTakenMs, long? BytesSent, string? ClientIp, string? Username);

/// <summary>A page of requests: sorted and paged in SQLite, so the order is over every match, not just the rows shown.</summary>
public sealed class RequestsPageData
{
    public List<RequestRow> Rows { get; init; } = [];
    public long Total { get; init; }
    public int Page { get; init; }
    public string Sort { get; init; } = "time";
    public bool Descending { get; init; } = true;
    public int Pages => (int)Math.Max(1, (Total + RequestsQuery.PageSize - 1) / RequestsQuery.PageSize);
}

public static class RequestsQuery
{
    public const int PageSize = 50;

    /// <summary>The columns people can sort by: the key in the URL, the column, and whether it starts descending.</summary>
    public static readonly Dictionary<string, (string Column, bool Descending)> Sorts = new()
    {
        ["time"] = ("ts", true),
        ["method"] = ("method", false),
        ["path"] = ("uri_stem", false),
        ["status"] = ("status", true),
        ["ms"] = ("time_taken_ms", true),
        ["size"] = ("bytes_sent", true),
        ["client"] = ("client_ip", false),
        ["user"] = ("username", false),
    };

    public static RequestsPageData Load(Db db, LogFilter filter, string? sort, string? dir, int page)
    {
        sort = sort is not null && Sorts.ContainsKey(sort) ? sort : "time";
        bool descending = dir is null ? Sorts[sort].Descending : dir == "desc";
        var (where, args) = filter.Sql();
        long total = db.Scalar($"SELECT count(*) FROM requests {where}", args);
        int pages = (int)Math.Max(1, (total + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pages);

        // Rows without the value last either way ("IS NULL" first in the ORDER BY, rather than NULLS LAST, which the
        // SQLite in older Windows releases does not know).
        string order = descending ? "DESC" : "ASC";
        var paged = new Args { ["$limit"] = PageSize, ["$offset"] = (long)(page - 1) * PageSize };
        foreach (var (key, value) in args) paged[key] = value;
        var rows = db.Query(
            $"""
            SELECT id, ts, method, uri_stem, uri_query, status, sub_status, time_taken_ms, bytes_sent, client_ip, username
            FROM requests {where}
            ORDER BY {Sorts[sort].Column} IS NULL, {Sorts[sort].Column} {order}, id {order}
            LIMIT $limit OFFSET $offset
            """, paged,
            r => new RequestRow(r.GetInt64(0), r.Long(1), r.Text(2), r.Text(3), r.Text(4), r.Int(5), r.Int(6), r.Long(7), r.Long(8), r.Text(9), r.Text(10)));

        return new RequestsPageData { Rows = rows, Total = total, Page = page, Sort = sort, Descending = descending };
    }
}
