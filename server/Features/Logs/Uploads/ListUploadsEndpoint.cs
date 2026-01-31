using Microsoft.EntityFrameworkCore;
using Server.Data;

namespace Server.Features.Logs.Uploads;

public static class ListUploadsEndpoint
{
    public static IEndpointRouteBuilder MapLogUploadsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/logs/uploads", HandleAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleAsync(IisLogDbContext dbContext, CancellationToken cancellationToken)
    {
        var uploads = await dbContext.LogFiles
            .AsNoTracking()
            .OrderByDescending(file => file.UploadedAt)
            .Select(file => new LogUploadDto(
                file.Id,
                file.OriginalFileName,
                file.FileHash,
                file.Format,
                file.FileSizeBytes,
                file.UploadedAt,
                file.TotalLines,
                file.ParsedLines,
                file.FailedLines,
                file.SkippedLines))
            .ToListAsync(cancellationToken);

        return Results.Ok(uploads);
    }

    public sealed record LogUploadDto(
        long Id,
        string OriginalFileName,
        string FileHash,
        string Format,
        long FileSizeBytes,
        DateTimeOffset UploadedAt,
        int TotalLines,
        int ParsedLines,
        int FailedLines,
        int SkippedLines);
}
