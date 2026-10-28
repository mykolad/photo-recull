using System.Globalization;

namespace PhotoRecull.Core;

public static class FileSizeText
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes, CultureInfo? culture = null)
    {
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        var format = unit == 0 || size >= 100 ? "0" : size >= 10 ? "0.#" : "0.##";
        return $"{size.ToString(format, culture ?? CultureInfo.CurrentCulture)} {Units[unit]}";
    }
}
