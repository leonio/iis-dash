using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Data.Entities;
using Server.Features.Logs.Shared;

namespace Server.Features.Logs.Analytics;

internal static class LogAnalyticsQuery
{
    public const int DefaultTrafficBucketMinutes = 15;
    public const int HeatmapBucketMinutes = 15;

    public static IQueryable<LogEntry> ApplyBaseFilters(IQueryable<LogEntry> query, LogQueryFilter filter)
    {
        if (filter.Start.HasValue)
        {
            query = query.Where(entry => entry.Timestamp >= filter.Start);
        }

        if (filter.End.HasValue)
        {
            query = query.Where(entry => entry.Timestamp <= filter.End);
        }

        if (!string.IsNullOrWhiteSpace(filter.Method))
        {
            query = query.Where(entry => entry.Method == filter.Method);
        }

        if (!string.IsNullOrWhiteSpace(filter.ClientIp))
        {
            query = query.Where(entry => entry.ClientIp == filter.ClientIp);
        }

        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            query = query.Where(entry => entry.Username == filter.Username);
        }

        if (filter.LogFileId.HasValue)
        {
            query = query.Where(entry => entry.LogFileId == filter.LogFileId.Value);
        }

        if (filter.StatusGroup.HasValue)
        {
            var group = filter.StatusGroup.Value;
            query = group switch
            {
                2 => query.Where(entry => entry.ProtocolStatus >= 200 && entry.ProtocolStatus < 300),
                3 => query.Where(entry => entry.ProtocolStatus >= 300 && entry.ProtocolStatus < 400),
                4 => query.Where(entry => entry.ProtocolStatus >= 400 && entry.ProtocolStatus < 500),
                5 => query.Where(entry => entry.ProtocolStatus >= 500 && entry.ProtocolStatus < 600),
                _ => query.Where(entry => entry.ProtocolStatus == null || entry.ProtocolStatus < 200)
            };
        }

        return query;
    }

    public static bool RequiresInMemoryFiltering(LogQueryFilter filter)
        => !string.IsNullOrWhiteSpace(filter.Endpoint)
           || filter.TimeOfDayStartMinutes.HasValue
           || filter.TimeOfDayEndMinutes.HasValue;

    public static IEnumerable<LogEntry> ApplyInMemoryFilters(IEnumerable<LogEntry> entries, LogQueryFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Endpoint))
        {
            var normalized = EndpointNormalizer.Normalize(filter.Endpoint);
            entries = entries.Where(entry => EndpointNormalizer.Normalize(entry.UriStem) == normalized);
        }

        if (filter.TimeOfDayStartMinutes.HasValue && filter.TimeOfDayEndMinutes.HasValue)
        {
            var start = filter.TimeOfDayStartMinutes.Value;
            var end = filter.TimeOfDayEndMinutes.Value;
            entries = entries.Where(entry => entry.Timestamp.HasValue && IsInTimeOfDayRange(entry.Timestamp.Value, start, end));
        }

        return entries;
    }

    public static IEnumerable<LogEntrySnapshot> ApplyInMemoryFilters(
        IEnumerable<LogEntrySnapshot> entries,
        LogQueryFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Endpoint))
        {
            var normalized = EndpointNormalizer.Normalize(filter.Endpoint);
            entries = entries.Where(entry => EndpointNormalizer.Normalize(entry.UriStem) == normalized);
        }

        if (filter.TimeOfDayStartMinutes.HasValue && filter.TimeOfDayEndMinutes.HasValue)
        {
            var start = filter.TimeOfDayStartMinutes.Value;
            var end = filter.TimeOfDayEndMinutes.Value;
            entries = entries.Where(entry => entry.Timestamp.HasValue && IsInTimeOfDayRange(entry.Timestamp.Value, start, end));
        }

        return entries;
    }

    public static async Task<List<LogEntrySnapshot>> GetEntrySnapshotsAsync(
        IisLogDbContext dbContext,
        LogQueryFilter filter,
        CancellationToken cancellationToken)
    {
        var query = ApplyBaseFilters(dbContext.LogEntries.AsNoTracking(), filter)
            .Select(entry => new LogEntrySnapshot(
                entry.Id,
                entry.Timestamp,
                entry.Method,
                entry.UriStem,
                entry.UriQuery,
                entry.ProtocolStatus,
                entry.TimeTakenMs,
                entry.BytesSent,
                entry.BytesReceived,
                entry.ClientIp,
                entry.Username,
                entry.UserAgent,
                entry.LogFileId));

        var entries = await query.ToListAsync(cancellationToken);
        var filtered = ApplyInMemoryFilters(entries, filter).ToList();

        return filtered;
    }

    public static LogEntryDto MapToDto(LogEntrySnapshot entry)
    {
        var endpoint = BuildEndpoint(entry.UriStem, entry.UriQuery);
        var normalized = EndpointNormalizer.Normalize(entry.UriStem);
        var statusGroup = GetStatusGroup(entry.ProtocolStatus);

        return new LogEntryDto(
            entry.Id,
            entry.Timestamp,
            entry.Method,
            endpoint,
            normalized,
            entry.ProtocolStatus,
            StatusGroupLabel(statusGroup),
            entry.TimeTakenMs,
            entry.BytesSent,
            entry.BytesReceived,
            entry.ClientIp,
            entry.Username,
            entry.UserAgent,
            entry.LogFileId);
    }

    public static LogEntryDto MapToDto(LogEntry entry)
    {
        var endpoint = BuildEndpoint(entry.UriStem, entry.UriQuery);
        var normalized = EndpointNormalizer.Normalize(entry.UriStem);
        var statusGroup = GetStatusGroup(entry.ProtocolStatus);

        return new LogEntryDto(
            entry.Id,
            entry.Timestamp,
            entry.Method,
            endpoint,
            normalized,
            entry.ProtocolStatus,
            StatusGroupLabel(statusGroup),
            entry.TimeTakenMs,
            entry.BytesSent,
            entry.BytesReceived,
            entry.ClientIp,
            entry.Username,
            entry.UserAgent,
            entry.LogFileId);
    }

    public static string BuildEndpoint(string? uriStem, string? uriQuery)
    {
        if (string.IsNullOrWhiteSpace(uriStem))
        {
            return "/";
        }

        if (string.IsNullOrWhiteSpace(uriQuery))
        {
            return uriStem;
        }

        return uriQuery.StartsWith('?') ? $"{uriStem}{uriQuery}" : $"{uriStem}?{uriQuery}";
    }

    public static int NormalizeTopN(int topN)
    {
        return topN switch
        {
            <= 0 => 10,
            > 30 => 30,
            _ => topN
        };
    }

    public static int GetStatusGroup(int? statusCode)
    {
        if (!statusCode.HasValue)
        {
            return 0;
        }

        var group = statusCode.Value / 100;
        return group is >= 2 and <= 5 ? group : 0;
    }

    public static string StatusGroupLabel(int group)
        => group switch
        {
            2 => "2xx",
            3 => "3xx",
            4 => "4xx",
            5 => "5xx",
            _ => "Other"
        };

    public static int? GetHeatmapBucketIndex(DateTimeOffset timestamp)
    {
        var minutes = (int)timestamp.TimeOfDay.TotalMinutes;
        var bucketIndex = minutes / HeatmapBucketMinutes;
        return bucketIndex is >= 0 and < (24 * 60 / HeatmapBucketMinutes) ? bucketIndex : null;
    }

    public static bool IsInTimeOfDayRange(DateTimeOffset timestamp, int startMinutes, int endMinutes)
    {
        var minutes = (int)timestamp.TimeOfDay.TotalMinutes;
        return minutes >= startMinutes && minutes < endMinutes;
    }

    public static DateTimeOffset TruncateToBucket(DateTimeOffset timestamp, int bucketMinutes)
    {
        var minutes = (int)timestamp.TimeOfDay.TotalMinutes;
        var bucketStartMinutes = minutes / bucketMinutes * bucketMinutes;
        var date = new DateTimeOffset(timestamp.Year, timestamp.Month, timestamp.Day, 0, 0, 0, timestamp.Offset);
        return date.AddMinutes(bucketStartMinutes);
    }

    public static long? Percentile(IReadOnlyList<long> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        var position = percentile / 100d * (sortedValues.Count - 1);
        var index = (int)Math.Round(position, MidpointRounding.AwayFromZero);
        index = Math.Clamp(index, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}
