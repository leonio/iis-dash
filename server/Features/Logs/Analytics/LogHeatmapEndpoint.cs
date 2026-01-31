using Server.Data;

namespace Server.Features.Logs.Analytics;

public static class LogHeatmapEndpoint
{
    public static IEndpointRouteBuilder MapLogHeatmapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/logs/heatmap", HandleAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HeatmapRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var topN = LogAnalyticsQuery.NormalizeTopN(request.TopN);
        var entries = await LogAnalyticsQuery.GetEntrySnapshotsAsync(dbContext, request.Filter, cancellationToken);

        var endpointCounts = entries
            .Select(entry => entry.NormalizedEndpoint)
            .GroupBy(endpoint => endpoint)
            .Select(group => new { Endpoint = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ToList();

        var topEndpoints = endpointCounts
            .Take(topN)
            .Select(group => group.Endpoint)
            .ToList();

        var topEndpointSet = topEndpoints.ToHashSet(StringComparer.Ordinal);

        var endpoints = new List<string>(topEndpoints);
        var includeOther = request.IncludeOther && endpointCounts.Count > topEndpoints.Count;
        if (includeOther)
        {
            endpoints.Add("Other");
        }

        var bucketLabels = Enumerable.Range(0, 24 * 60 / LogAnalyticsQuery.HeatmapBucketMinutes)
            .Select(index => TimeSpan.FromMinutes(index * LogAnalyticsQuery.HeatmapBucketMinutes).ToString(@"hh\:mm"))
            .ToList();

        var endpointIndex = endpoints
            .Select((endpoint, index) => new { endpoint, index })
            .ToDictionary(item => item.endpoint, item => item.index, StringComparer.Ordinal);

        var cells = entries
            .Where(entry => entry.Timestamp.HasValue)
            .Select(entry => new
            {
                Endpoint = topEndpointSet.Contains(entry.NormalizedEndpoint)
                    ? entry.NormalizedEndpoint
                    : includeOther
                        ? "Other"
                        : null,
                BucketIndex = LogAnalyticsQuery.GetHeatmapBucketIndex(entry.Timestamp!.Value)
            })
            .Where(entry => entry.Endpoint is not null && entry.BucketIndex is not null)
            .GroupBy(entry => new { entry.Endpoint, entry.BucketIndex })
            .Select(group => new HeatmapCell(
                endpointIndex[group.Key.Endpoint!],
                group.Key.BucketIndex!.Value,
                group.Count()))
            .ToList();

        var response = new HeatmapResponse(
            LogAnalyticsQuery.HeatmapBucketMinutes,
            endpoints,
            bucketLabels,
            cells);

        return Results.Ok(response);
    }
}
