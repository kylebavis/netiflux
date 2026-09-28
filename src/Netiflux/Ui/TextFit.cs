namespace Netiflux.Ui;

internal static class TextFit
{
    /// <summary>Cuts <paramref name="value"/> to <paramref name="max"/> columns, ending in an ellipsis when shortened.</summary>
    public static string Truncate(string value, int max)
    {
        if (max <= 0)
        {
            return "";
        }

        if (value.Length <= max)
        {
            return value;
        }

        return max <= 1 ? value[..max] : value[..(max - 1)] + "…";
    }

    /// <summary>Hard-cuts or pads <paramref name="value"/> to exactly <paramref name="width"/> columns.</summary>
    public static string Fit(string value, int width) =>
        value.Length >= width ? value[..width] : value.PadRight(width);
}
