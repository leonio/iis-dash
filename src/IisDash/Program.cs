// IIS Dash: upload IIS logs, see what your sites did. ASP.NET Core minimal APIs with the pages rendered on the server
// (RazorSlices), htmx 4 and UIkit in the browser, SQLite underneath, compiled to one native executable. See README.md.

using System.Reflection;
using IisDash;
using IisDash.Features.Dashboard;
using IisDash.Features.Endpoints;
using IisDash.Features.Export;
using IisDash.Features.Requests;
using IisDash.Features.Uploads;
using IisDash.Shared.Data;
using IisDash.Shared.Filters;
using IisDash.Shared.Web;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateSlimBuilder(args);
var options = AppOptions.From(builder.Configuration);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
if (builder.Configuration["ASPNETCORE_URLS"] is null && builder.Configuration["urls"] is null && builder.Configuration["ASPNETCORE_HTTP_PORTS"] is null)
{
    builder.WebHost.UseUrls("http://localhost:5080");
}

// Uploads: several log files of up to MaxFileBytes each in one request.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = long.MaxValue);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new Db(options.DbPath));
builder.Services.AddSingleton<FilterOptions>();
builder.Services.AddSingleton<LogIngestion>();
builder.Services.AddScoped<Ui>();
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// The static files are embedded in the executable; their URLs carry the version, so they can be cached for good.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "Static"),
    RequestPath = "/static",
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public, max-age=604800",
});

app.MapGet("/healthz", (Db db) => db.Scalar("SELECT 1") == 1 ? Results.Text("ok") : Results.StatusCode(500));

// The language menu: remembered in a cookie for a year, then back to the page it was chosen on.
app.MapGet("/lang/{code}", (string code, string? next, HttpContext http) =>
{
    if (I18n.Languages.Contains(code))
    {
        http.Response.Cookies.Append("lang", code, new CookieOptions { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, HttpOnly = true });
    }

    return Results.Redirect(Html.Local(next));
});

DashboardRoutes.Map(app);
EndpointsRoutes.Map(app);
RequestsRoutes.Map(app);
UploadsRoutes.Map(app);
ExportRoutes.Map(app);
app.MapFallback((HttpContext http) => Html.Page(Error.Create(I18n.T(Ui.Language(http), "error.not_found")), 404));

app.Logger.LogInformation("IIS Dash {Version}: data in {Path}", Ui.Version, Path.GetFullPath(options.DbPath));
app.Run();

/// <summary>For the tests, which host the app in memory.</summary>
public partial class Program;
