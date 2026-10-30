namespace IisDash.Features.Uploads.Parsing;

/// <summary>
/// Whether a request was for a static file: a script, a style sheet, an image, a font, a source map, media; or one
/// of ASP.NET's resource handlers (<c>ScriptResource.axd</c>, <c>WebResource.axd</c>), which Web Forms pages load
/// scripts through, often uncached and many times per page. Decided once, when the log is read, and kept with the
/// request, so the filter can leave them out (or show nothing else) at no cost.
/// </summary>
public static class StaticFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".map",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".avif", ".bmp", ".tif", ".tiff",
        ".woff", ".woff2", ".ttf", ".otf", ".eot",
        ".mp3", ".mp4", ".webm", ".ogg", ".wav",
        ".swf", ".xap",
    };

    private static readonly HashSet<string> Handlers = new(StringComparer.OrdinalIgnoreCase)
    {
        "scriptresource.axd", "webresource.axd",
    };

    public static bool IsStatic(string? uriStem)
    {
        if (string.IsNullOrEmpty(uriStem)) return false;
        var stem = uriStem.AsSpan();
        int query = stem.IndexOf('?');
        if (query >= 0) stem = stem[..query];
        var name = stem[(stem.LastIndexOf('/') + 1)..];
        int dot = name.LastIndexOf('.');
        return dot >= 0 && (Extensions.Contains(name[dot..].ToString()) || Handlers.Contains(name.ToString()));
    }
}
