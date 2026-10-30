using System.Text.Encodings.Web;
using System.Text.Unicode;
using RazorSlices;

namespace IisDash.Shared.Web;

/// <summary>Rendering slices (the .cshtml files next to each feature) as pages and fragments, and ending form posts.</summary>
public static class Html
{
    public static IResult Page(RazorSlice slice, int status = 200)
    {
        slice.HtmlEncoder = Encoder;
        return Results.RazorSlice(slice, status);
    }

    /// <summary>
    /// Encodes what HTML needs (&lt; &gt; &amp; quotes) and leaves other characters as they are, so German and the
    /// dashes in ranges reach the page as text rather than as &amp;#xFC; and &amp;#x2013;. The page is UTF-8.
    /// </summary>
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>303 See Other with the flash message in the query, like a form post should end.</summary>
    public static IResult Redirect(string url, string msg = "", string err = "")
    {
        var query = new List<string>();
        if (msg.Length > 0) query.Add("msg=" + Uri.EscapeDataString(msg));
        if (err.Length > 0) query.Add("err=" + Uri.EscapeDataString(err));
        if (query.Count > 0) url += (url.Contains('?') ? "&" : "?") + string.Join('&', query);
        return new SeeOther(url);
    }

    /// <summary>A local path to go back to, or "/" for anything else, so a link cannot send people off the site.</summary>
    public static string Local(string? url) =>
        url is { Length: > 0 } && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal) ? url : "/";

    private sealed class SeeOther(string url) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.StatusCode = StatusCodes.Status303SeeOther;
            http.Response.Headers.Location = url;
            return Task.CompletedTask;
        }
    }
}
