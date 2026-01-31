using ClosedXML.Excel;
using Server.Data;

namespace Server.Features.Logs.Analytics;

public static class LogExportEndpoint
{
    public static IEndpointRouteBuilder MapLogExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/logs/export/all", HandleAllAsync)
            .RequireAuthorization();

        app.MapPost("/api/logs/export/traffic", HandleTrafficAsync)
            .RequireAuthorization();

        app.MapPost("/api/logs/export/status", HandleStatusAsync)
            .RequireAuthorization();

        app.MapPost("/api/logs/export/latency", HandleLatencyAsync)
            .RequireAuthorization();

        app.MapPost("/api/logs/export/heatmap", HandleHeatmapAsync)
            .RequireAuthorization();

        app.MapPost("/api/logs/export/entries", HandleEntriesAsync)
            .RequireAuthorization();

        return app;
    }

    private static Task<IResult> HandleAllAsync(
        ExportRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
        => ExportAsync(request, ExportMode.All, dbContext, cancellationToken);

    private static Task<IResult> HandleTrafficAsync(
        ExportRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
        => ExportAsync(request, ExportMode.Traffic, dbContext, cancellationToken);

    private static Task<IResult> HandleStatusAsync(
        ExportRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
        => ExportAsync(request, ExportMode.Status, dbContext, cancellationToken);

    private static Task<IResult> HandleLatencyAsync(
        ExportRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
        => ExportAsync(request, ExportMode.Latency, dbContext, cancellationToken);

    private static Task<IResult> HandleHeatmapAsync(
        ExportRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
        => ExportAsync(request, ExportMode.Heatmap, dbContext, cancellationToken);

    private static Task<IResult> HandleEntriesAsync(
        ExportRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
        => ExportAsync(request, ExportMode.Entries, dbContext, cancellationToken);

    private static async Task<IResult> ExportAsync(
        ExportRequest request,
        ExportMode mode,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var entries = await LogAnalyticsQuery.GetEntrySnapshotsAsync(dbContext, request.Filter, cancellationToken);
        var workbook = new XLWorkbook();

        switch (mode)
        {
            case ExportMode.Traffic:
                AddTrafficSheet(workbook, entries, LogAnalyticsQuery.DefaultTrafficBucketMinutes);
                break;
            case ExportMode.Status:
                AddStatusSheet(workbook, entries);
                break;
            case ExportMode.Latency:
                AddLatencySheet(workbook, entries);
                break;
            case ExportMode.Heatmap:
                AddHeatmapSheet(workbook, entries, LogAnalyticsQuery.NormalizeTopN(request.TopN), request.IncludeOther);
                break;
            case ExportMode.Entries:
                AddEntriesSheet(workbook, entries);
                break;
            case ExportMode.All:
            default:
                AddTrafficSheet(workbook, entries, LogAnalyticsQuery.DefaultTrafficBucketMinutes);
                AddStatusSheet(workbook, entries);
                AddLatencySheet(workbook, entries);
                AddHeatmapSheet(workbook, entries, LogAnalyticsQuery.NormalizeTopN(request.TopN), request.IncludeOther);
                AddEntriesSheet(workbook, entries);
                break;
        }

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var modeLabel = mode.ToString().ToLowerInvariant();
        var fileName = $"iis-logs-export-{modeLabel}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.xlsx";

        return Results.File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void AddTrafficSheet(XLWorkbook workbook, IReadOnlyCollection<LogEntrySnapshot> entries, int bucketMinutes)
    {
        var sheet = workbook.Worksheets.Add("Traffic");
        sheet.Cell(1, 1).Value = "BucketStart";
        sheet.Cell(1, 2).Value = "Count";
        sheet.Cell(1, 3).Value = "RequestsPerMinute";

        var traffic = entries
            .Where(entry => entry.Timestamp.HasValue)
            .GroupBy(entry => LogAnalyticsQuery.TruncateToBucket(entry.Timestamp!.Value, bucketMinutes))
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Bucket = group.Key,
                Count = group.Count(),
                Rate = Math.Round(group.Count() / (double)bucketMinutes, 3)
            })
            .ToList();

        var row = 2;
        foreach (var point in traffic)
        {
            sheet.Cell(row, 1).Value = point.Bucket.UtcDateTime;
            sheet.Cell(row, 2).Value = point.Count;
            sheet.Cell(row, 3).Value = point.Rate;
            row += 1;
        }
    }

    private static void AddStatusSheet(XLWorkbook workbook, IReadOnlyCollection<LogEntrySnapshot> entries)
    {
        var sheet = workbook.Worksheets.Add("Status");
        sheet.Cell(1, 1).Value = "Group";
        sheet.Cell(1, 2).Value = "Count";

        var rows = entries
            .GroupBy(entry => LogAnalyticsQuery.GetStatusGroup(entry.ProtocolStatus))
            .OrderBy(group => group.Key)
            .Select(group => new { Group = LogAnalyticsQuery.StatusGroupLabel(group.Key), Count = group.Count() })
            .ToList();

        var row = 2;
        foreach (var item in rows)
        {
            sheet.Cell(row, 1).Value = item.Group;
            sheet.Cell(row, 2).Value = item.Count;
            row += 1;
        }
    }

    private static void AddLatencySheet(XLWorkbook workbook, IReadOnlyCollection<LogEntrySnapshot> entries)
    {
        var sheet = workbook.Worksheets.Add("Latency");
        sheet.Cell(1, 1).Value = "Metric";
        sheet.Cell(1, 2).Value = "Milliseconds";

        var values = entries
            .Where(entry => entry.TimeTakenMs.HasValue)
            .Select(entry => entry.TimeTakenMs!.Value)
            .OrderBy(value => value)
            .ToList();

        var average = values.Count == 0 ? null : (double?)values.Average();

        var metrics = new Dictionary<string, double?>
        {
            { "Average", average },
            { "P50", LogAnalyticsQuery.Percentile(values, 50) },
            { "P95", LogAnalyticsQuery.Percentile(values, 95) },
            { "P99", LogAnalyticsQuery.Percentile(values, 99) }
        };

        var row = 2;
        foreach (var metric in metrics)
        {
            sheet.Cell(row, 1).Value = metric.Key;
            sheet.Cell(row, 2).Value = metric.Value;
            row += 1;
        }
    }

    private static void AddHeatmapSheet(
        XLWorkbook workbook,
        IReadOnlyCollection<LogEntrySnapshot> entries,
        int topN,
        bool includeOther)
    {
        var sheet = workbook.Worksheets.Add("Heatmap");
        sheet.Cell(1, 1).Value = "Endpoint";
        sheet.Cell(1, 2).Value = "BucketStart";
        sheet.Cell(1, 3).Value = "Count";

        var endpointCounts = entries
            .Select(entry => entry.NormalizedEndpoint)
            .GroupBy(endpoint => endpoint)
            .Select(group => new { Endpoint = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ToList();

        var topEndpoints = endpointCounts.Take(topN).Select(group => group.Endpoint).ToHashSet(StringComparer.Ordinal);
        var row = 2;

        var heatmapRows = entries
            .Where(entry => entry.Timestamp.HasValue)
            .Select(entry => new
            {
                Endpoint = topEndpoints.Contains(entry.NormalizedEndpoint)
                    ? entry.NormalizedEndpoint
                    : includeOther
                        ? "Other"
                        : null,
                BucketIndex = LogAnalyticsQuery.GetHeatmapBucketIndex(entry.Timestamp!.Value)
            })
            .Where(entry => entry.Endpoint is not null && entry.BucketIndex is not null)
            .GroupBy(entry => new { entry.Endpoint, entry.BucketIndex })
            .Select(group => new
            {
                group.Key.Endpoint,
                Bucket = TimeSpan.FromMinutes(group.Key.BucketIndex!.Value * LogAnalyticsQuery.HeatmapBucketMinutes),
                Count = group.Count()
            })
            .OrderBy(group => group.Endpoint)
            .ThenBy(group => group.Bucket)
            .ToList();

        foreach (var item in heatmapRows)
        {
            sheet.Cell(row, 1).Value = item.Endpoint;
            sheet.Cell(row, 2).Value = item.Bucket.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
            sheet.Cell(row, 3).Value = item.Count;
            row += 1;
        }
    }

    private static void AddEntriesSheet(XLWorkbook workbook, IReadOnlyCollection<LogEntrySnapshot> entries)
    {
        var sheet = workbook.Worksheets.Add("Entries");
        sheet.Cell(1, 1).Value = "Timestamp";
        sheet.Cell(1, 2).Value = "Method";
        sheet.Cell(1, 3).Value = "Endpoint";
        sheet.Cell(1, 4).Value = "NormalizedEndpoint";
        sheet.Cell(1, 5).Value = "Status";
        sheet.Cell(1, 6).Value = "StatusGroup";
        sheet.Cell(1, 7).Value = "TimeTakenMs";
        sheet.Cell(1, 8).Value = "BytesSent";
        sheet.Cell(1, 9).Value = "BytesReceived";
        sheet.Cell(1, 10).Value = "ClientIp";
        sheet.Cell(1, 11).Value = "Username";
        sheet.Cell(1, 12).Value = "UserAgent";
        sheet.Cell(1, 13).Value = "LogFileId";

        var row = 2;
        foreach (var entry in entries)
        {
            sheet.Cell(row, 1).Value = entry.Timestamp?.UtcDateTime;
            sheet.Cell(row, 2).Value = entry.Method;
            sheet.Cell(row, 3).Value = LogAnalyticsQuery.BuildEndpoint(entry.UriStem, entry.UriQuery);
            sheet.Cell(row, 4).Value = entry.NormalizedEndpoint;
            sheet.Cell(row, 5).Value = entry.ProtocolStatus;
            sheet.Cell(row, 6).Value = LogAnalyticsQuery.StatusGroupLabel(LogAnalyticsQuery.GetStatusGroup(entry.ProtocolStatus));
            sheet.Cell(row, 7).Value = entry.TimeTakenMs;
            sheet.Cell(row, 8).Value = entry.BytesSent;
            sheet.Cell(row, 9).Value = entry.BytesReceived;
            sheet.Cell(row, 10).Value = entry.ClientIp;
            sheet.Cell(row, 11).Value = entry.Username;
            sheet.Cell(row, 12).Value = entry.UserAgent;
            sheet.Cell(row, 13).Value = entry.LogFileId;
            row += 1;
        }
    }
}
