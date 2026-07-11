using System.Globalization;

namespace CustomRadioPanel.Core.Logic;

/// <summary>Formatting and squawk/BCD helpers for the 5-digit displays. Pure and unit-testable.</summary>
public static class RadioFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// COM/NAV frequency for the 5-digit display.
    /// <para>When <paramref name="hideHundreds"/> is true (default): drops the always-1 leading digit
    /// and shows 3 decimals, e.g. 123.405 → "23.405" — ideal for 8.33 kHz / VATSIM frequencies.</para>
    /// <para>When false: classic 3 integer digits + 2 decimals, e.g. "118.30".</para>
    /// </summary>
    public static string ComNav(double mhz, bool hideHundreds = true)
    {
        if (mhz <= 0)
            return string.Empty;

        if (hideHundreds)
        {
            double snapped = SnapToGrid(mhz, 0.005);
            return (snapped % 100).ToString("00.000", Inv); // e.g. 23.405
        }

        return mhz.ToString("000.00", Inv);
    }

    /// <summary>ADF frequency in whole kHz, e.g. 350.</summary>
    public static string Adf(double khz) => khz <= 0 ? string.Empty : Math.Round(khz).ToString("0", Inv);

    /// <summary>DME distance, e.g. 12.3; blank when no station.</summary>
    public static string Dme(double nm) => nm <= 0 ? string.Empty : nm.ToString("00.0", Inv);

    /// <summary>Kohlsman setting in whole hPa, e.g. 1013.</summary>
    public static string Kohlsman(double mb) => mb <= 0 ? string.Empty : Math.Round(mb).ToString("0", Inv);

    /// <summary>Squawk (from a BCD16 value) as 4 digits, e.g. 1200.</summary>
    public static string Squawk(double bcd) => BcdToCode(bcd).ToString("0000", Inv);

    /// <summary>Decodes a BCD16 transponder value into a plain 4-digit code (each digit 0-7).</summary>
    public static int BcdToCode(double bcd)
    {
        int v = (int)Math.Round(bcd);
        int d3 = (v >> 12) & 0xF;
        int d2 = (v >> 8) & 0xF;
        int d1 = (v >> 4) & 0xF;
        int d0 = v & 0xF;
        return d3 * 1000 + d2 * 100 + d1 * 10 + d0;
    }

    /// <summary>Adjusts a 4-octal-digit squawk code by a signed amount in base-8 space, wrapping 0000-7777.</summary>
    public static int AdjustSquawk(int code, int deltaBase8)
    {
        int d3 = (code / 1000) % 10, d2 = (code / 100) % 10, d1 = (code / 10) % 10, d0 = code % 10;
        int oct = ((d3 & 7) << 9) | ((d2 & 7) << 6) | ((d1 & 7) << 3) | (d0 & 7);
        oct = ((oct + deltaBase8) % 4096 + 4096) % 4096;
        return (oct >> 9 & 7) * 1000 + (oct >> 6 & 7) * 100 + (oct >> 3 & 7) * 10 + (oct & 7);
    }

    /// <summary>Rounds a value to the nearest multiple of <paramref name="grid"/> (avoids float drift on tuning).</summary>
    public static double SnapToGrid(double value, double grid)
        => grid <= 0 ? value : Math.Round(value / grid) * grid;
}
