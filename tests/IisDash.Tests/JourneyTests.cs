using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IisDash.Tests;

/// <summary>The app in memory, end to end: uploading logs, the dashboard and its drill-downs, the requests, the export.</summary>
public sealed class JourneyTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _http = default!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable("IISDASH_DATA", Directory.CreateTempSubdirectory("iis-dash-test").FullName);
        _factory = new WebApplicationFactory<Program>();
        _http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>A day of a W3C log: 40 requests an hour from 08:00 to 11:59, every tenth a 500, plus one line that is not a request.</summary>
    private static string Log()
    {
        var log = new StringBuilder("#Software: Microsoft Internet Information Services 10.0\n#Fields: date time cs-method cs-uri-stem cs-uri-query c-ip sc-status time-taken\n");
        for (int i = 0; i < 160; i++)
        {
            int status = i % 10 == 0 ? 500 : 200;
            log.Append($"2026-10-30 {8 + i / 40:00}:{i % 40:00}:00 GET /api/orders/{1000 + i} - 10.0.0.{1 + i % 4} {status} {(status == 500 ? 1500 : 40)}\n");
        }

        return log.Append("not a request\n").ToString();
    }

    private async Task<HttpResponseMessage> Upload(params (string Name, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, content) in files) form.Add(new ByteArrayContent(content), "files", name);
        return await _http.PostAsync("/uploads", form, Ct);
    }

    private async Task<string> Get(string url)
    {
        var response = await _http.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Fact]
    public async Task From_an_empty_dashboard_to_an_export()
    {
        Assert.Contains("No logs yet", await Get("/"));

        // Upload: a log, a zip with another, and something that is not a log.
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(archive.CreateEntry("web02/u_ex261029.log").Open());
            entry.Write(Log().Replace("2026-10-30", "2026-10-29"));
        }

        var uploaded = await Upload(("u_ex261030.log", Encoding.UTF8.GetBytes(Log())), ("more.zip", zip.ToArray()), ("notes.md", "# Notes\n"u8.ToArray()));
        string page = await uploaded.Content.ReadAsStringAsync(Ct);
        Assert.Contains("160 requests", page);
        Assert.Contains("1 unreadable", page);
        Assert.Contains("web02/u_ex261029.log", page);
        Assert.Contains("Not a W3C or IIS log", page);

        // The dashboard: 320 requests, 10% server errors, the 500s are the slow ones.
        string dashboard = await Get("/");
        Assert.Contains("320 requests from 2026-10-29 08:00 to 2026-10-30 11:39", dashboard);
        Assert.Contains("10%", dashboard);
        Assert.Contains("/api/orders/:id", dashboard);

        // Drill down: a status, an hour, a client. Each narrows everything.
        Assert.Contains("32 requests", await Get("/?status=5xx"));
        Assert.Contains("80 requests", await Get("/?hour=9"));
        Assert.Contains("8 requests", await Get("/?hour=9&client=10.0.0.1&from=2026-10-30T00:00"));
        Assert.Contains("Nothing matches", await Get("/?status=404"));

        // The requests, sorted slowest first over all of them, and one opened.
        string requests = await Get("/requests?status=500&sort=ms&dir=desc");
        Assert.Contains("1–32 of 32 requests", requests);
        string id = System.Text.RegularExpressions.Regex.Match(requests, "/requests/(\\d+)").Groups[1].Value;
        string detail = await Get("/requests/" + id);
        Assert.Contains("1,500", detail);
        Assert.Contains("line ", detail);

        // The export: the figures, then every matching request.
        var summary = await _http.GetAsync("/export/summary.xlsx?status=5xx", Ct);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", summary.Content.Headers.ContentType?.MediaType);
        using (var book = new ZipArchive(await summary.Content.ReadAsStreamAsync(Ct)))
        {
            Assert.Equal(7, book.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)));
        }

        using (var book = new ZipArchive(await (await _http.GetAsync("/export/requests.xlsx?client=10.0.0.2", Ct)).Content.ReadAsStreamAsync(Ct)))
        {
            string sheet = new StreamReader(book.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
            Assert.Equal(81, System.Text.RegularExpressions.Regex.Count(sheet, "<row "));
        }

        // Uploading the same file again replaces it; deleting it takes its requests.
        Assert.Contains("replaced the earlier upload", await (await Upload(("u_ex261030.log", Encoding.UTF8.GetBytes(Log())))).Content.ReadAsStringAsync(Ct));
        string uploads = await Get("/uploads");
        string fileId = System.Text.RegularExpressions.Regex.Match(uploads, "/uploads/(\\d+)/delete").Groups[1].Value;
        var deleted = await _http.PostAsync($"/uploads/{fileId}/delete", null, Ct);
        Assert.Equal(HttpStatusCode.SeeOther, deleted.StatusCode);
        Assert.Contains("160 requests from", await Get("/"));
    }

    [Fact]
    public async Task A_script_uploads_with_curl_and_gets_text()
    {
        using var curl = _factory.CreateClient();
        curl.DefaultRequestHeaders.Accept.Clear();
        curl.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(Log())), "file", "u_ex261030.log" } };
        var response = await curl.PostAsync("/uploads", form, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("u_ex261030.log\tingested\tW3C\t160 requests\t1 unreadable\n", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task German_from_the_cookie_or_the_browser()
    {
        await Upload(("u_ex261030.log", Encoding.UTF8.GetBytes(Log())));
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptLanguage.ParseAdd("de-DE,de;q=0.9");
        string page = await (await _http.SendAsync(request, Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("<html lang=\"de\">", page);
        Assert.Contains("160 Anfragen vom 30.10.2026 08:00 bis 30.10.2026 11:39", page);

        var chosen = await _http.GetAsync("/lang/en?next=/requests", Ct);
        Assert.Equal("/requests", chosen.Headers.Location?.ToString());
        Assert.Contains("lang=en", chosen.Headers.GetValues("Set-Cookie").Single());

        var away = await _http.GetAsync("/lang/de?next=//evil.example", Ct);
        Assert.Equal("/", away.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Static_files_come_from_the_executable()
    {
        var css = await _http.GetAsync("/static/app.css", Ct);
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/static/vendor/htmx-4.0.0.min.js", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/nope", Ct)).StatusCode);
        Assert.Equal("ok", await Get("/healthz"));
    }

    [Fact]
    public async Task Endpoints_count_once_and_static_files_can_be_left_out()
    {
        var log = new StringBuilder("#Fields: date time cs-method cs-uri-stem cs-uri-query sc-status\n");
        for (int i = 0; i < 30; i++) log.Append($"2026-10-30 09:{i:00}:00 GET /ScriptResource.axd d=x{i}&t=1 200\n");
        for (int i = 0; i < 12; i++) log.Append($"2026-10-30 10:{i:00}:00 GET {(i % 2 == 0 ? "/Orders/List.aspx" : "/orders/list.aspx")} page={i} 200\n");
        for (int i = 0; i < 5; i++) log.Append($"2026-10-30 11:{i:00}:00 GET /Scripts/jquery.js - 304\n");
        await Upload(("u_ex261030.log", Encoding.UTF8.GetBytes(log.ToString())));

        string all = await Get("/endpoints");
        Assert.Contains("3 of 3 endpoints, 47 requests", all);
        Assert.Contains("/orders/list.aspx", all);
        Assert.Contains(">Static<", all);

        string pages = await Get("/endpoints?static=hide");
        Assert.Contains("1 of 1 endpoints, 12 requests", pages);
        Assert.DoesNotContain("scriptresource.axd", pages);

        Assert.Contains("35 requests", await Get("/?static=only"));
        Assert.Contains("1–12 of 12 requests", await Get("/requests?static=hide"));
    }
}
