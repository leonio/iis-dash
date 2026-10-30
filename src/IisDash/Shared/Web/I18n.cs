using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IisDash.Shared.Web;

/// <summary>
/// UI text: flat key → text catalogues in Locales/ (embedded in the executable), one per language, English and German.
/// </summary>
/// <remarks>
/// Adding a language is adding its file with the same keys as en.json (a test checks), its own name under
/// <c>language.self</c>, and its number and date conventions under <c>format.*</c>. The language comes from the
/// <c>lang</c> cookie (the menu in the header), else the browser's Accept-Language, else English.
/// </remarks>
public static partial class I18n
{
    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> Catalogues = Load();

    /// <summary>English first: it is the fallback for missing texts and unknown languages.</summary>
    public static readonly IReadOnlyList<string> Languages = ["en", .. Catalogues.Keys.Where(c => c != "en").Order()];

    public static IReadOnlyDictionary<string, string> Catalogue(string language) => Catalogues[language];

    public static string T(string language, string key)
    {
        var texts = Catalogues.GetValueOrDefault(language) ?? Catalogues["en"];
        return texts.GetValueOrDefault(key) ?? Catalogues["en"].GetValueOrDefault(key) ?? key;
    }

    /// <summary>
    /// The text for <paramref name="key"/>, with <c>{name}</c> placeholders filled from <paramref name="values"/>'s
    /// properties: <c>T(lang, "requests.count", new { count = n })</c>. The type is a generic parameter so that the
    /// native build keeps those properties.
    /// </summary>
    public static string T<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TValues>(string language, string key, TValues values)
    {
        string text = T(language, key);
        if (values is null) return text;
        var properties = typeof(TValues).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return Placeholder().Replace(text, m =>
            properties.FirstOrDefault(p => p.Name == m.Groups[1].Value)?.GetValue(values)?.ToString() ?? m.Value);
    }

    /// <summary>A language's name in itself, for the language menu: English, Deutsch.</summary>
    public static string OwnName(string language) => Catalogues[language].GetValueOrDefault("language.self", language);

    public static string Pick(string? cookie, string? acceptLanguage)
    {
        if (cookie is not null && Languages.Contains(cookie)) return cookie;
        foreach (string part in (acceptLanguage ?? "").Split(','))
        {
            string code = part.Split(';')[0].Trim().ToLowerInvariant();
            code = code.Length > 2 ? code[..2] : code;
            if (Languages.Contains(code)) return code;
        }

        return "en";
    }

    private static FrozenDictionary<string, FrozenDictionary<string, string>> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var catalogues = new Dictionary<string, FrozenDictionary<string, string>>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Locales.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            catalogues[name["Locales.".Length..^".json".Length]] =
                json.RootElement.EnumerateObject().ToFrozenDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }

        return catalogues.ToFrozenDictionary();
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
