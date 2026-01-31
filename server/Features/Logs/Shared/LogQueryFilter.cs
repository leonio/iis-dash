namespace Server.Features.Logs.Shared;

public sealed record LogQueryFilter(
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? Method,
    string? Endpoint,
    string? ClientIp,
    string? Username,
    int? StatusGroup,
    long? LogFileId,
    int? TimeOfDayStartMinutes,
    int? TimeOfDayEndMinutes);
