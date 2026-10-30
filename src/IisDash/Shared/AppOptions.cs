using System.Globalization;

namespace IisDash;

/// <summary>
/// The few settings there are, from the environment (IISDASH_DATA, IISDASH_MAX_FILE_MB) or the command line
/// (--data, --max-file-mb). Where the app listens is ASP.NET Core's own: ASPNETCORE_URLS, ASPNETCORE_HTTP_PORTS or --urls.
/// </summary>
public sealed class AppOptions
{
    /// <summary>The folder for the database, <c>iis-dash.db</c>; "data" under the working folder unless set.</summary>
    public string DataDir { get; init; } = "data";

    /// <summary>The largest log file accepted (unpacked size for files in a .zip). A request may carry several.</summary>
    public long MaxFileBytes { get; init; } = 512L * 1024 * 1024;

    public string DbPath => Path.Combine(DataDir, "iis-dash.db");

    public static AppOptions From(IConfiguration config)
    {
        string? megabytes = config["IISDASH_MAX_FILE_MB"] ?? config["max-file-mb"];
        return new AppOptions
        {
            DataDir = config["IISDASH_DATA"] ?? config["data"] ?? "data",
            MaxFileBytes = long.TryParse(megabytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out long mb) && mb > 0
                ? mb * 1024 * 1024
                : 512L * 1024 * 1024,
        };
    }
}
