using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using IisDash.Shared.Filters;
using Microsoft.AspNetCore.Html;

namespace IisDash.Shared.Web;

/// <summary>
/// What every page needs, per request: the language, its texts and formats, the filter in the URL, the flash
/// message, icons and navigation links. Slices get it with <c>@inject Ui Ui</c> (_ViewImports.cshtml).
/// </summary>
public sealed class Ui(IHttpContextAccessor accessor)
{
    private Formats? _formats;
    private LogFilter? _filter;

    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("No request.");

    /// <summary>The language from the <c>lang</c> cookie (the menu in the header), else the browser's Accept-Language.</summary>
    public string Lang => Language(Http);

    public Formats F => _formats ??= new Formats(Lang);

    /// <summary>The filter in this request's URL.</summary>
    public LogFilter Filter => _filter ??= LogFilter.From(Http.Request.Query);

    public string Path => Http.Request.Path.Value ?? "/";

    /// <summary>The page itself, for links that come back here (the language menu).</summary>
    public string Here => Http.Request.Path + Http.Request.QueryString;

    public string Msg => Http.Request.Query["msg"].ToString();
    public string Err => Http.Request.Query["err"].ToString();

    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0")
        .Split('+')[0];

    /// <summary>The page is being loaded into the slide-in panel: it comes without the page around it.</summary>
    public bool Embedded => Http.Request.Headers["HX-Target"].ToString() == SheetTarget;

    public const string SheetTarget = "div#sheet-body";

    public string T(string key) => I18n.T(Lang, key);

    public string T<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TValues>(string key, TValues values) => I18n.T(Lang, key, values);

    public static string Language(HttpContext http) =>
        I18n.Pick(http.Request.Cookies["lang"], http.Request.Headers.AcceptLanguage.ToString());

    /// <summary>
    /// A link that moves within the app: an ordinary href (it works without JavaScript, opens in a new tab, can be
    /// copied), which htmx loads in place: the new page is morphed into #app, so the scroll position and the header
    /// stay, and the address bar follows.
    /// </summary>
    public static HtmlString Go(string href)
    {
        string url = WebUtility.HtmlEncode(href);
        return new HtmlString($"href=\"{url}\" hx-get=\"{url}\" hx-target=\"#app\" hx-select=\"#app\" hx-swap=\"outerMorph\" hx-push-url=\"true\"");
    }

    /// <summary>A link that opens its page in the slide-in panel (and is still an ordinary link to that page).</summary>
    public static HtmlString Sheet(string href, string title)
    {
        string url = WebUtility.HtmlEncode(href);
        return new HtmlString($"href=\"{url}\" hx-get=\"{url}\" hx-target=\"#sheet-body\" data-sheet=\"{WebUtility.HtmlEncode(title)}\"");
    }

    /// <summary>A request's path with its query string after it, muted: written in one piece so no space comes between.</summary>
    public static HtmlString PathAndQuery(string? stem, string? query) => new(
        WebUtility.HtmlEncode(stem ?? "/") +
        (query is null ? "" : $"<span class=\"id-query\">?{WebUtility.HtmlEncode(query)}</span>"));

    public static string Q(string? value) => Uri.EscapeDataString(value ?? "");

    public static HtmlString Raw(string? html) => new(html ?? "");

    /// <summary>The colour class of a status code or class: s2 … s5, or s0 for anything else.</summary>
    public static string StatusTone(int? status) => status switch
    {
        >= 200 and < 300 => "id-s2",
        >= 300 and < 400 => "id-s3",
        >= 400 and < 500 => "id-s4",
        >= 500 and < 600 => "id-s5",
        _ => "id-s0",
    };

    /// <summary>
    /// A line icon as inline SVG, so pages need no icon font and work offline. Drawn on a 24px grid in the style of
    /// Lucide. 1rem square; <c>id-icon-xs</c>, <c>id-icon-s</c> and <c>id-icon-l</c> size it.
    /// </summary>
    public static HtmlString Icon(string name, string classes = "") => new(
        $"<svg class=\"id-icon {WebUtility.HtmlEncode(classes)}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
        $"stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{Paths[name]}</svg>");

    private static readonly Dictionary<string, string> Paths = new()
    {
        ["activity"] = """<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>""",
        ["alert"] = """<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0"/><path d="M12 9v4M12 17h.01"/>""",
        ["arrow-left"] = """<path d="M19 12H5M12 19l-7-7 7-7"/>""",
        ["arrow-right"] = """<path d="M5 12h14M12 5l7 7-7 7"/>""",
        ["chart"] = """<path d="M3 3v18h18"/><path d="M7 16v-5M11 16V8M15 16v-3M19 16V6"/>""",
        ["check"] = """<path d="M20 6 9 17l-5-5"/>""",
        ["chevron-down"] = """<path d="m6 9 6 6 6-6"/>""",
        ["chevron-left"] = """<path d="m15 18-6-6 6-6"/>""",
        ["chevron-right"] = """<path d="m9 18 6-6-6-6"/>""",
        ["clock"] = """<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>""",
        ["download"] = """<path d="M12 3v12M7 10l5 5 5-5"/><path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"/>""",
        ["external"] = """<path d="M14 4h6v6M20 4l-9 9"/><path d="M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"/>""",
        ["file"] = """<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/>""",
        ["filter"] = """<path d="M3 5h18l-7 8v6l-4 2v-8z"/>""",
        ["globe"] = """<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9s1.3-6.4 3.8-9"/>""",
        ["grid"] = """<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/>""",
        ["list"] = """<path d="M9 6h11M9 12h11M9 18h11"/><path d="M4.5 6h.01M4.5 12h.01M4.5 18h.01" stroke-width="2.5"/>""",
        ["moon"] = """<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>""",
        ["search"] = """<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/>""",
        ["server"] = """<rect x="3" y="4" width="18" height="7" rx="1"/><rect x="3" y="13" width="18" height="7" rx="1"/><path d="M7 7.5h.01M7 16.5h.01" stroke-width="2.5"/>""",
        ["sort"] = """<path d="m7 15 5 5 5-5M7 9l5-5 5 5"/>""",
        ["sort-asc"] = """<path d="m7 14 5-5 5 5"/>""",
        ["sort-desc"] = """<path d="m7 10 5 5 5-5"/>""",
        ["sun"] = """<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/>""",
        ["trash"] = """<path d="M4 7h16M10 11v6M14 11v6"/><path d="M6 7l1 13a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-13M9 7V4h6v3"/>""",
        ["upload"] = """<path d="M12 15V3M7 8l5-5 5 5"/><path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2"/>""",
        ["user"] = """<circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/>""",
        ["x"] = """<path d="M18 6 6 18M6 6l12 12"/>""",
        ["zoom-out"] = """<circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5M8 11h6"/>""",
    };
}
