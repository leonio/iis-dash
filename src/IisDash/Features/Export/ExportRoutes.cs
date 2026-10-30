using System.Globalization;
using IisDash.Features.Dashboard;
using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;

namespace IisDash.Features.Export;

/// <summary>
/// Excel workbooks of what the page shows, for the same filter: the summary (every chart's figures, a sheet each) or
/// the requests themselves. Written to a temporary file that is deleted once sent, so a large export streams.
/// </summary>
public static class ExportRoutes
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/export/summary.xlsx", (HttpContext http, Db db) =>
            Send("summary", writer => Summary(writer, db, LogFilter.From(http.Request.Query), Ui.Language(http))));

        app.MapGet("/export/requests.xlsx", (HttpContext http, Db db) =>
            Send("requests", writer => Requests(writer, db, LogFilter.From(http.Request.Query), Ui.Language(http))));
    }

    private static IResult Send(string what, Action<XlsxWriter> write)
    {
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
        using (var writer = new XlsxWriter(file))
        {
            write(writer);
            writer.Finish();
        }

        file.Position = 0;
        string name = $"iis-dash-{what}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.xlsx";
        return Results.Stream(file, Xlsx, name);
    }

    private static void Summary(XlsxWriter x, Db db, LogFilter filter, string lang)
    {
        string T(string key) => I18n.T(lang, key);
        var d = DashboardQuery.Load(db, filter, top: 1000);

        x.Sheet(T("export.sheet_summary"), (T("export.figure"), 34), (T("export.value"), 22));
        x.Row(T("export.filter"), filter.IsEmpty ? T("export.no_filter") : filter.ToString());
        x.Row(T("stats.requests"), d.Total);
        x.Row(T("export.first"), Time(d.FirstTs));
        x.Row(T("export.last"), Time(d.LastTs));
        x.Row(T("stats.server_errors"), new Percent(d.Share(d.ServerErrors)));
        x.Row(T("stats.client_errors"), new Percent(d.Share(d.ClientErrors)));
        x.Row(T("export.avg_ms"), d.AvgMs);
        x.Row(T("stats.p50") + " (ms)", d.P50);
        x.Row(T("stats.p95") + " (ms)", d.P95);
        x.Row(T("stats.p99") + " (ms)", d.P99);
        x.Row(T("stats.sent") + " (bytes)", d.BytesSent);

        x.Sheet(T("traffic.title"), (T("export.bucket_start"), 20), (T("stats.requests"), 12), ("2xx", 10), ("3xx", 10), ("4xx", 10), ("5xx", 10), (T("status.other"), 10));
        foreach (var (start, c) in d.Traffic.Buckets)
        {
            x.Row(Time(start), c.Sum(), c[2], c[3], c[4], c[5], c[0]);
        }

        x.Sheet(T("status.title"), (T("requests.status"), 12), (T("stats.requests"), 12), (T("export.share"), 10));
        foreach (var (code, count) in d.Codes)
        {
            x.Row(code is null ? T("status.none") : (long)code, count, new Percent(d.Share(count)));
        }

        x.Sheet(T("latency.title"), (T("export.from_ms"), 12), (T("export.to_ms"), 12), (T("stats.requests"), 12), (T("export.share"), 10));
        foreach (var b in d.Latency)
        {
            x.Row(b.From, b.To, b.Count, new Percent(d.Timed == 0 ? 0 : (double)b.Count / d.Timed));
        }

        x.Sheet(T("endpoints.title"), (T("endpoints.endpoint"), 48), (T("stats.requests"), 12), ("4xx", 10), ("5xx", 10), (T("export.avg_ms"), 12), (T("export.max_ms"), 12));
        foreach (var e in d.Endpoints)
        {
            x.Row(e.Endpoint, e.Count, e.ClientErrors, e.ServerErrors, e.AvgMs, e.MaxMs);
        }

        x.Sheet(T("heatmap.title"), [(T("endpoints.endpoint"), 48), .. Enumerable.Range(0, 24).Select(h => (h.ToString("00", CultureInfo.InvariantCulture), 7.0))]);
        for (int i = 0; i < d.Heatmap.Endpoints.Count; i++)
        {
            x.Row([d.Heatmap.Endpoints[i], .. d.Heatmap.Cells[i].Select(v => (object?)v)]);
        }

        x.Sheet(T("clients.title"), (T("clients.ip"), 40), (T("stats.requests"), 12), (T("endpoints.errors"), 12), (T("stats.sent") + " (bytes)", 16));
        foreach (var c in d.Clients)
        {
            x.Row(c.Ip, c.Count, c.Errors, c.BytesSent);
        }
    }

    private static void Requests(XlsxWriter x, Db db, LogFilter filter, string lang)
    {
        string T(string key) => I18n.T(lang, key);
        string[] fields = ["method", "uri_stem", "uri_query", "status", "sub_status", "win32_status", "time_taken_ms", "bytes_sent",
                           "bytes_received", "client_ip", "username", "user_agent", "referer", "host", "server_ip", "server_port",
                           "protocol_version", "site_name", "server_name"];
        double[] widths = [9, 48, 30, 8, 8, 8, 12, 12, 12, 18, 18, 48, 36, 20, 16, 8, 10, 18, 18];
        x.Sheet(T("requests.title"),
            [(T("requests.time"), 20), (T("request.endpoint"), 36), .. fields.Select((f, i) => (T("field." + f), widths[i])), (T("export.file"), 28), (T("export.line"), 8)]);

        var (where, args) = filter.Sql("r");
        using var conn = db.Open();
        using var command = Db.Command(conn,
            $"""
            SELECT r.ts, r.endpoint, {string.Join(", ", fields.Select(f => "r." + f))}, f.name, r.line_no
            FROM requests r JOIN log_files f ON f.id = r.file_id
            {where}
            ORDER BY r.ts, r.id
            """, args);
        using var reader = command.ExecuteReader();
        var row = new object?[fields.Length + 4];
        while (reader.Read())
        {
            row[0] = reader.IsDBNull(0) ? null : Time(reader.GetInt64(0));
            for (int i = 1; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            if (!x.Row(row)) break;
        }
    }

    private static DateTime? Time(long? ts) => ts is null ? null : DateTime.UnixEpoch.AddSeconds(ts.Value);
}
