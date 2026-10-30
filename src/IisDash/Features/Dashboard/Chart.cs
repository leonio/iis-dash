using System.Globalization;
using IisDash.Shared.Web;

namespace IisDash.Features.Dashboard;

/// <summary>The arithmetic of the charts: round axis maxima, percentages for CSS, ticks along a time axis.</summary>
public static class Chart
{
    /// <summary>The smallest 1, 2 or 5 × 10ⁿ at or above <paramref name="value"/> (and divisible by 4 for the grid lines).</summary>
    public static long Nice(long value)
    {
        if (value <= 4) return 4;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (double step in new[] { 1, 2, 4, 5, 8, 10 })
        {
            long nice = (long)(step * magnitude);
            if (nice >= value && nice % 4 == 0) return nice;
        }

        return (long)(10 * magnitude);
    }

    /// <summary>A share as a CSS percentage: "37.5%".</summary>
    public static string Pct(double part, double whole) =>
        (whole <= 0 ? 0 : Math.Min(100, part / whole * 100)).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    /// <summary>The middle of column <paramref name="index"/> of <paramref name="count"/>, as a CSS left (or its start for the first).</summary>
    public static string Position(int index, int count, bool start = false) =>
        Pct(index + (start ? 0 : 0.5), count);

    public static string BucketText(Ui ui, long seconds) => seconds switch
    {
        60 => ui.T("chart.per_minute"),
        < 3600 => ui.T("chart.per_minutes", new { n = seconds / 60 }),
        3600 => ui.T("chart.per_hour"),
        < 86400 => ui.T("chart.per_hours", new { n = seconds / 3600 }),
        86400 => ui.T("chart.per_day"),
        _ => ui.T("chart.per_week"),
    };

    /// <summary>
    /// Labels along the time axis, about seven: at midnight with the day (strong) when the range spans days, otherwise
    /// at even steps with the time, and the day where it changes.
    /// </summary>
    public static List<(int Index, string Label, bool Strong)> TimeTicks(Ui ui, Traffic traffic)
    {
        var ticks = new List<(int, string, bool)>();
        var buckets = traffic.Buckets;
        long width = traffic.BucketSeconds;
        if (buckets.Count == 0) return ticks;
        long span = buckets.Count * width;

        if (width >= 86400)
        {
            int step = Math.Max(1, (int)Math.Ceiling(buckets.Count / 7.0));
            for (int i = 0; i < buckets.Count; i += step) ticks.Add((i, ui.F.Day(buckets[i].Start), false));
            return ticks;
        }

        if (span > 2 * 86400)
        {
            var midnights = Enumerable.Range(0, buckets.Count).Where(i => buckets[i].Start % 86400 == 0).ToList();
            int every = Math.Max(1, (int)Math.Ceiling(midnights.Count / 8.0));
            foreach (int i in midnights.Where((_, n) => n % every == 0)) ticks.Add((i, ui.F.Day(buckets[i].Start), true));
            if (ticks.Count == 0 || ticks[0].Item1 > buckets.Count / 5) ticks.Insert(0, (0, ui.F.Day(buckets[0].Start), false));
            return ticks;
        }

        // Steps of a round duration: the smallest of these that gives at most seven labels.
        long[] rounds = [300, 600, 900, 1800, 3600, 2 * 3600, 3 * 3600, 6 * 3600, 12 * 3600];
        long every2 = rounds.FirstOrDefault(r => r >= width && span / r <= 7, 12 * 3600);
        long? lastDay = null;
        for (int i = 0; i < buckets.Count; i++)
        {
            long start = buckets[i].Start;
            if (start % every2 != 0 && i != 0) continue;
            if (i != 0 && i < buckets.Count / 14) continue; // not crowding the first label
            long day = start / 86400;
            bool newDay = lastDay != day;
            lastDay = day;
            ticks.Add((i, newDay ? $"{ui.F.Day(start)} {Formats.Clock(start)}" : Formats.Clock(start), newDay));
        }

        return ticks;
    }
}
