using System.Globalization;

namespace IisDash.Shared.Web;

/// <summary>
/// Numbers, durations, sizes and times the way a language writes them: 12,345.6 and 2026-10-30 in English,
/// 12.345,6 and 30.10.2026 in German.
/// </summary>
/// <remarks>
/// The app runs with invariant globalization (no ICU, so the native executable needs nothing installed), which means
/// .NET knows no cultures. The conventions come from the language's catalogue instead (<c>format.*</c> keys), and are
/// applied to the invariant culture's formats.
/// </remarks>
public sealed class Formats
{
    private readonly NumberFormatInfo _numbers;
    private readonly string _datePattern;
    private readonly string _dayPattern;
    private readonly string[] _months;

    public Formats(string language)
    {
        _numbers = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        _numbers.NumberGroupSeparator = I18n.T(language, "format.group");
        _numbers.NumberDecimalSeparator = I18n.T(language, "format.decimal");
        _datePattern = I18n.T(language, "format.date");
        _dayPattern = I18n.T(language, "format.day");
        _months = I18n.T(language, "format.months").Split(',');
        PercentSuffix = I18n.T(language, "format.percent");
    }

    private string PercentSuffix { get; }

    public string N(long value) => value.ToString("N0", _numbers);

    public string N(double value, int decimals) => value.ToString("N" + decimals, _numbers);

    /// <summary>A share as a percentage: 0.042 → "4.2%" / "4,2 %". Small non-zero shares keep a decimal, so they do not read as 0.</summary>
    public string Pct(double fraction)
    {
        double percent = fraction * 100;
        int decimals = percent is > 0 and < 10 ? 1 : 0;
        return percent.ToString("N" + decimals, _numbers) + PercentSuffix;
    }

    /// <summary>A duration in milliseconds: 245 ms, 1.24 s, 12.5 s, 2.1 min.</summary>
    public string Ms(double? ms) => ms switch
    {
        null => "–",
        < 1000 => N(Math.Round(ms.Value), 0) + " ms",
        < 10_000 => N(ms.Value / 1000, 2) + " s",
        < 60_000 => N(ms.Value / 1000, 1) + " s",
        _ => N(ms.Value / 60_000, 1) + " min",
    };

    public string Bytes(long? bytes) => bytes switch
    {
        null => "–",
        < 1024 => N(bytes.Value) + " B",
        < 1024 * 1024 => N(bytes.Value / 1024.0, 1) + " KB",
        < 1024L * 1024 * 1024 => N(bytes.Value / (1024.0 * 1024), 1) + " MB",
        _ => N(bytes.Value / (1024.0 * 1024 * 1024), 2) + " GB",
    };

    /// <summary>A log time (seconds since 1970 on the log's clock) with date and time: 2026-10-30 14:05:12.</summary>
    public string When(long? ts, bool seconds = true)
    {
        if (ts is null) return "–";
        var time = DateTime.UnixEpoch.AddSeconds(ts.Value);
        return time.ToString(_datePattern, CultureInfo.InvariantCulture) + " " +
               time.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
    }

    public string Date(long ts) => DateTime.UnixEpoch.AddSeconds(ts).ToString(_datePattern, CultureInfo.InvariantCulture);

    /// <summary>A short day: Oct 30 / 30. Okt.</summary>
    public string Day(long ts)
    {
        var time = DateTime.UnixEpoch.AddSeconds(ts);
        return _dayPattern.Replace("{day}", time.Day.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                          .Replace("{month}", _months[time.Month - 1], StringComparison.Ordinal);
    }

    public static string Clock(long ts) => DateTime.UnixEpoch.AddSeconds(ts).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A time as the browser's datetime-local input wants it: 2026-10-30T14:05.</summary>
    public static string Input(long? ts) =>
        ts is null ? "" : DateTime.UnixEpoch.AddSeconds(ts.Value).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
}
