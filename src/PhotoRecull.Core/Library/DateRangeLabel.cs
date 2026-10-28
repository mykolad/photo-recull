using System.Globalization;

namespace PhotoRecull.Core.Library;

public static class DateRangeLabel
{
    public static string Format(DateOnly from, DateOnly to, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (from == to)
            return from.ToString("d MMM yyyy", culture);
        return from.Year == to.Year
            ? $"{from.ToString("d MMM", culture)} – {to.ToString("d MMM yyyy", culture)}"
            : $"{from.ToString("d MMM yyyy", culture)} – {to.ToString("d MMM yyyy", culture)}";
    }

    public static string FormatUntilToday(DateOnly from, CultureInfo? culture = null) =>
        $"{from.ToString("d MMM yyyy", culture ?? CultureInfo.CurrentCulture)} – today";
}
