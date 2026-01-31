using System.IO.Compression;
using Server.Features.Logs.Shared;

namespace Server.Features.Logs.Precheck;

public static class PrecheckLogsEndpoint
{
    public static IEndpointRouteBuilder MapLogPrecheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/logs/precheck", HandleAsync)
            .DisableAntiforgery()
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleAsync(HttpRequest request, LogIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "Multipart/form-data is required." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.Count == 0)
        {
            return Results.BadRequest(new { message = "No files were provided." });
        }

        var file = form.Files[0];
        if (file.Length == 0)
        {
            return Results.BadRequest(new { message = "File is empty." });
        }

        if (Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var zipStream = file.OpenReadStream();
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            var firstEntry = archive.Entries.FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Name));
            if (firstEntry is null)
            {
                return Results.BadRequest(new { message = "Zip file contains no entries." });
            }

            if (firstEntry.Length > LogIngestionService.MaxFileSizeBytes)
            {
                return Results.BadRequest(new { message = "First zip entry exceeds 10MB limit." });
            }

            await using var entryStream = firstEntry.Open();
            var result = await ingestionService.PrecheckAsync(
                new LogIngestionService.IngestFile(firstEntry.FullName, firstEntry.Length, entryStream),
                cancellationToken);
            return Results.Ok(result);
        }

        await using var fileStream = file.OpenReadStream();
        var precheck = await ingestionService.PrecheckAsync(
            new LogIngestionService.IngestFile(file.FileName, file.Length, fileStream),
            cancellationToken);

        return Results.Ok(precheck);
    }
}
