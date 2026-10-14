using System.Globalization;

namespace Barline.Services;

internal static class MetricsFormatter
{
    /// <summary>One placeholder for every metric that has no value yet.</summary>
    public const string Missing = "—";

    public static string Percent(double? value) =>
        value.HasValue ? $"{Math.Clamp(Math.Round(value.Value), 0, 100):0}%" : Missing;

    public static string Temperature(double? value) =>
        value.HasValue ? $"{Math.Clamp(Math.Round(value.Value), 0, 199):0}°" : Missing;

    public static string FramesPerSecond(double? value) =>
        value.HasValue ? $"{Math.Clamp(Math.Round(value.Value), 0, 999):0}" : Missing;

    /// <summary>
    /// Traffic in megabits per second with no unit label: the unit is always the same,
    /// so 0,12 · 9,35 · 93,4 · 934 read unambiguously. Decimal prefixes, like providers use.
    /// </summary>
    public static string DataRate(double? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue)
            return Missing;

        // The cell is sized for "9999"; faster links are shown at that ceiling.
        var megabits = Math.Clamp(bytesPerSecond.Value * 8 / 1_000_000, 0, 9999);
        var numberFormat = megabits < 0.005
            ? "0"
            : megabits < 9.995
                ? "0.00"
                : megabits < 99.95
                    ? "0.0"
                    : "0";

        return megabits.ToString(numberFormat, CultureInfo.CurrentCulture);
    }
}
