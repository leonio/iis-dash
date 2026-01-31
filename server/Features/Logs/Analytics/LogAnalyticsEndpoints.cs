namespace Server.Features.Logs.Analytics;

public static class LogAnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapLogAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapLogMetricsEndpoints();
        app.MapLogHeatmapEndpoints();
        app.MapLogEntriesEndpoints();
        app.MapLogExportEndpoints();

        return app;
    }
}
