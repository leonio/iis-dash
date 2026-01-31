using System.IO.Compression;
using Server.Features.Logs.Shared;

namespace Server.Features.Logs.Upload;

public static class UploadLogsEndpoint
{
    public static IEndpointRouteBuilder MapLogUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/logs/upload", HandleAsync)
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

        var filesToProcess = new List<LogIngestionService.IngestFile>();
        foreach (var file in form.Files)
        {
            if (file.Length == 0)
            {
                continue;
            }

            if (Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await using var zipStream = file.OpenReadStream();
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

                var hasEntry = false;
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    hasEntry = true;
                    if (entry.Length > LogIngestionService.MaxFileSizeBytes)
                    {
                        filesToProcess.Add(new LogIngestionService.IngestFile(entry.FullName, entry.Length, Stream.Null));
                        continue;
                    }

                    await using var entryStream = entry.Open();
                    var buffer = new MemoryStream();
                    await entryStream.CopyToAsync(buffer, cancellationToken);
                    buffer.Position = 0;
                    filesToProcess.Add(new LogIngestionService.IngestFile(entry.FullName, entry.Length, buffer));
                }

                if (!hasEntry)
                {
                    return Results.BadRequest(new { message = "Zip file contains no entries." });
                }
            }
            else
            {
                var stream = file.OpenReadStream();
                filesToProcess.Add(new LogIngestionService.IngestFile(file.FileName, file.Length, stream));
            }
        }

        if (filesToProcess.Count == 0)
        {
            return Results.BadRequest(new { message = "No valid files were found for ingestion." });
        }

        var summary = await ingestionService.IngestAsync(filesToProcess, cancellationToken);
        foreach (var file in filesToProcess)
        {
            await file.Stream.DisposeAsync();
        }

        return Results.Ok(summary);
    }
}
