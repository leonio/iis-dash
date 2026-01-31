using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Features.Logs.Analytics;

public static class LogEntriesEndpoint
{
    public static IEndpointRouteBuilder MapLogEntriesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/logs/entries", HandleAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleAsync(
        LogEntriesRequest request,
        IisLogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize <= 0 ? 50 : request.PageSize;
        var page = request.Page <= 0 ? 1 : request.Page;
        var filter = request.Filter;

        var baseQuery = LogAnalyticsQuery.ApplyBaseFilters(dbContext.LogEntries.AsNoTracking(), filter);

        if (LogAnalyticsQuery.RequiresInMemoryFiltering(filter))
        {
            var entries = await baseQuery
                .OrderByDescending(entry => entry.Timestamp)
                .ToListAsync(cancellationToken);

            var filtered = LogAnalyticsQuery.ApplyInMemoryFilters(entries, filter).ToList();
            var total = filtered.Count;
            var filteredPageItems = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(LogAnalyticsQuery.MapToDto)
                .ToList();

            return Results.Ok(new LogEntriesResponse(page, pageSize, total, filteredPageItems));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var pageEntries = await baseQuery
            .OrderByDescending(entry => entry.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                entry.LogFileId))
            .ToListAsync(cancellationToken);

        var pageItems = pageEntries.Select(LogAnalyticsQuery.MapToDto).ToList();
        return Results.Ok(new LogEntriesResponse(page, pageSize, totalCount, pageItems));
    }
}
