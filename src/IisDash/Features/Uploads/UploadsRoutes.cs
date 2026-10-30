using System.Globalization;
using System.Text;
using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;

namespace IisDash.Features.Uploads;

public sealed record UploadRow(
    long Id, string Name, string Format, long SizeBytes, string UploadedAt, int TotalLines, int ParsedLines, int FailedLines,
    int SkippedLines, long? FirstTs, long? LastTs);

/// <summary>
/// Uploading logs: the page, a format check as soon as files are picked, the upload itself, deleting, and the lines a
/// file had that could not be read. The upload endpoint also takes plain <c>curl -F file=@u_ex261030.log …/uploads</c>
/// and answers it in text.
/// </summary>
public static class UploadsRoutes
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/uploads", (Db db) => Html.Page(UploadsPage.Create(new UploadsPage.Args { Files = List(db) })));

        app.MapPost("/uploads/check", async (HttpRequest request, LogIngestion ingestion, CancellationToken ct) =>
        {
            var checks = new List<Precheck.Item>();
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                foreach (var file in form.Files.Where(f => f.Length > 0))
                {
                    try
                    {
                        foreach (var incoming in LogIngestion.Expand(file))
                        {
                            var (format, tooLarge) = await ingestion.PrecheckAsync(incoming, ct);
                            checks.Add(new Precheck.Item(incoming.Name, incoming.Size, format, tooLarge));
                        }
                    }
                    catch (InvalidDataException)
                    {
                        checks.Add(new Precheck.Item(file.FileName, file.Length, Parsing.LogFormat.Unknown, false));
                    }
                }
            }

            return Html.Page(Precheck.Create(checks));
        });

        app.MapPost("/uploads", async (HttpContext http, LogIngestion ingestion, FilterOptions options, Db db, CancellationToken ct) =>
        {
            var outcomes = new List<LogIngestion.FileOutcome>();
            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync(ct);
                foreach (var file in form.Files.Where(f => f.Length > 0))
                {
                    try
                    {
                        foreach (var incoming in LogIngestion.Expand(file))
                        {
                            outcomes.Add(await ingestion.IngestAsync(incoming, ct));
                        }
                    }
                    catch (InvalidDataException)
                    {
                        outcomes.Add(new LogIngestion.FileOutcome(file.FileName, LogIngestion.Outcome.Unsupported, Parsing.LogFormat.Unknown));
                    }
                }
            }

            options.Changed();
            bool browser = http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.Ordinal);
            if (!browser)
            {
                return Results.Text(Summary(outcomes), "text/plain; charset=utf-8",
                    statusCode: outcomes.Count > 0 && outcomes.All(o => o.Outcome == LogIngestion.Outcome.Ingested) ? 200 : 422);
            }

            return Html.Page(UploadsPage.Create(new UploadsPage.Args { Files = List(db), Results = outcomes }));
        }).DisableAntiforgery();

        app.MapPost("/uploads/{id:long}/delete", (long id, HttpContext http, Db db, FilterOptions options) =>
        {
            string? name = db.One("SELECT name FROM log_files WHERE id = $id", new Args { ["$id"] = id }, r => r.GetString(0));
            db.Execute("DELETE FROM log_files WHERE id = $id", new Args { ["$id"] = id });
            options.Changed();
            string lang = Ui.Language(http);
            return name is null
                ? Html.Redirect("/uploads")
                : Html.Redirect("/uploads", msg: I18n.T(lang, "uploads.deleted", new { name }));
        }).DisableAntiforgery();

        app.MapGet("/uploads/{id:long}", (long id, HttpContext http, Db db) =>
        {
            var file = List(db, id).FirstOrDefault();
            if (file is null)
            {
                return Html.Page(Shared.Web.Error.Create(I18n.T(Ui.Language(http), "error.no_upload")), 404);
            }

            var lines = db.Query("SELECT line_no, text, error FROM failed_lines WHERE file_id = $id ORDER BY line_no LIMIT 500",
                new Args { ["$id"] = id }, r => new UploadFailures.Line(r.GetInt32(0), r.GetString(1), r.GetString(2)));
            return Html.Page(UploadFailures.Create(new UploadFailures.Args { File = file, Lines = lines }));
        });
    }

    private static List<UploadRow> List(Db db, long? id = null) => db.Query(
        $"""
        SELECT id, name, format, size_bytes, uploaded_at, total_lines, parsed_lines, failed_lines, skipped_lines, first_ts, last_ts
        FROM log_files {(id is null ? "" : "WHERE id = $id")}
        ORDER BY first_ts DESC, name
        """,
        id is null ? new Args() : new Args { ["$id"] = id },
        r => new UploadRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetString(4), r.GetInt32(5), r.GetInt32(6),
            r.GetInt32(7), r.GetInt32(8), r.Long(9), r.Long(10)));

    /// <summary>The answer for a script: one line per file, tab separated.</summary>
    private static string Summary(List<LogIngestion.FileOutcome> outcomes)
    {
        if (outcomes.Count == 0) return "No files in the request: send them as multipart/form-data, e.g. curl -F file=@u_ex261030.log\n";
        var text = new StringBuilder();
        foreach (var o in outcomes)
        {
            text.Append(o.Name).Append('\t').Append(o.Outcome.ToString().ToLowerInvariant());
            if (o.Outcome == LogIngestion.Outcome.Ingested)
            {
                text.Append('\t').Append(o.Format == Parsing.LogFormat.W3C ? "W3C" : "IIS").Append('\t')
                    .Append(o.Parsed.ToString(CultureInfo.InvariantCulture)).Append(" requests\t")
                    .Append(o.Failed.ToString(CultureInfo.InvariantCulture)).Append(" unreadable");
                if (o.Replaced) text.Append("\treplaced");
            }

            text.Append('\n');
        }

        return text.ToString();
    }
}
