using Server.Data;

namespace Server.Features.Logs.Analytics;

public static class LogMetricsEndpoint
{
    public static IEndpointRouteBuilder MapLogMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/logs/metrics", HandleAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        MetricsRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var bucketMinutes = request.TrafficBucketMinutes <= 0
            ? LogAnalyticsQuery.DefaultTrafficBucketMinutes
            : request.TrafficBucketMinutes;

        var entries = await LogAnalyticsQuery.GetEntrySnapshotsAsync(dbContext, request.Filter, cancellationToken);

        var totalRequests = entries.Count;
        var errorRequests = entries.Count(entry =>
            entry.ProtocolStatus.HasValue && entry.ProtocolStatus.Value >= 400 && entry.ProtocolStatus.Value < 600);
        var errorRate = totalRequests == 0 ? 0 : (double)errorRequests / totalRequests;

        var latencyValues = entries
            .Where(entry => entry.TimeTakenMs.HasValue)
            .Select(entry => entry.TimeTakenMs!.Value)
            .OrderBy(value => value)
            .ToList();

        var latency = new LatencyPercentiles(
            latencyValues.Count == 0 ? null : (double?)latencyValues.Average(),
            LogAnalyticsQuery.Percentile(latencyValues, 50),
            LogAnalyticsQuery.Percentile(latencyValues, 95),
            LogAnalyticsQuery.Percentile(latencyValues, 99),
            latencyValues.Count);

        var traffic = entries
            .Where(entry => entry.Timestamp.HasValue)
            .GroupBy(entry => LogAnalyticsQuery.TruncateToBucket(entry.Timestamp!.Value, bucketMinutes))
            .OrderBy(group => group.Key)
            .Select(group => new TrafficPoint(
                group.Key,
                group.Count(),
                Math.Round(group.Count() / (double)bucketMinutes, 3)))
            .ToList();

        var statusGroups = entries
            .GroupBy(entry => LogAnalyticsQuery.GetStatusGroup(entry.ProtocolStatus))
            .OrderBy(group => group.Key)
            .Select(group => new StatusGroupPoint(
                LogAnalyticsQuery.StatusGroupLabel(group.Key),
                group.Count()))
            .ToList();

        var response = new LogMetricsResponse(
            totalRequests,
            errorRequests,
            errorRate,
            latency,
            traffic,
            statusGroups);

        return Results.Ok(response);
    }
}
