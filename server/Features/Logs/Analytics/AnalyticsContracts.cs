using Server.Features.Logs.Shared;

namespace Server.Features.Logs.Analytics;

internal sealed record MetricsRequest(LogQueryFilter Filter, int TrafficBucketMinutes);

internal sealed record HeatmapRequest(LogQueryFilter Filter, int TopN, bool IncludeOther);

internal sealed record LogEntriesRequest(LogQueryFilter Filter, int Page, int PageSize);

internal sealed record ExportRequest(LogQueryFilter Filter, int TopN, bool IncludeOther, ExportMode Mode);

internal sealed record LogMetricsResponse(
    int TotalRequests,
    int ErrorRequests,
    double ErrorRate,
    LatencyPercentiles Latency,
    IReadOnlyList<TrafficPoint> Traffic,
    IReadOnlyList<StatusGroupPoint> StatusGroups);

internal sealed record LatencyPercentiles(
    double? AverageMs,
    long? P50Ms,
    long? P95Ms,
    long? P99Ms,
    int SampleCount);

internal sealed record TrafficPoint(DateTimeOffset BucketStart, int Count, double RequestsPerMinute);

internal sealed record StatusGroupPoint(string Group, int Count);

internal sealed record HeatmapResponse(
    int BucketMinutes,
    IReadOnlyList<string> Endpoints,
    IReadOnlyList<string> Buckets,
    IReadOnlyList<HeatmapCell> Cells);

internal sealed record HeatmapCell(int EndpointIndex, int BucketIndex, int Count);

internal sealed record LogEntriesResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<LogEntryDto> Items);

internal sealed record LogEntryDto(
    long Id,
    DateTimeOffset? Timestamp,
    string? Method,
    string Endpoint,
    string NormalizedEndpoint,
    int? ProtocolStatus,
    string StatusGroup,
    long? TimeTakenMs,
    long? BytesSent,
    long? BytesReceived,
    string? ClientIp,
    string? Username,
    string? UserAgent,
    long LogFileId);

internal sealed record LogEntrySnapshot(
    long Id,
    DateTimeOffset? Timestamp,
    string? Method,
    string? UriStem,
    string? UriQuery,
    int? ProtocolStatus,
    long? TimeTakenMs,
    long? BytesSent,
    long? BytesReceived,
    string? ClientIp,
    string? Username,
    string? UserAgent,
    long LogFileId)
{
    public string NormalizedEndpoint => EndpointNormalizer.Normalize(UriStem);
}

internal enum ExportMode
{
    All,
    Traffic,
    Status,
    Latency,
    Heatmap,
    Entries
}
