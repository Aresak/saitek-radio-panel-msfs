using CustomRadioPanel.Core.Logic;

namespace CustomRadioPanel.Tests;

public class RadioFormatTests
{
    [Fact]
    public void ComNav_HidesLeadingDigit_AndShowsThreeDecimals_ByDefault()
    {
        Assert.Equal("23.405", RadioFormat.ComNav(123.405));
        Assert.Equal("18.300", RadioFormat.ComNav(118.30));
        Assert.Equal("08.000", RadioFormat.ComNav(108.0));
    }

    [Fact]
    public void ComNav_ClassicStyle_WhenHideHundredsFalse()
    {
        Assert.Equal("118.30", RadioFormat.ComNav(118.30, hideHundreds: false));
        Assert.Equal("108.00", RadioFormat.ComNav(108.0, hideHundreds: false));
    }

    [Fact]
    public void Bcd_RoundTrips_ForSquawk()
    {
        // 1200 decimal squawk -> BCD 0x1200 (4608)
        Assert.Equal(4608, ToBcd(1200));
        Assert.Equal(1200, RadioFormat.BcdToCode(4608));
        Assert.Equal("1200", RadioFormat.Squawk(4608));
    }

    [Fact]
    public void AdjustSquawk_WrapsInOctal()
    {
        Assert.Equal(1201, RadioFormat.AdjustSquawk(1200, 1));
        Assert.Equal(1210, RadioFormat.AdjustSquawk(1207, 1)); // 7 -> carry into next octal digit
        Assert.Equal(7777, RadioFormat.AdjustSquawk(0, -1));   // wrap-around at the bottom
    }

    [Fact]
    public void SnapToGrid_RoundsToNearestStep()
    {
        Assert.Equal(118.025, RadioFormat.SnapToGrid(118.03, 0.025), 3);
        Assert.Equal(110.05, RadioFormat.SnapToGrid(110.06, 0.05), 3);
    }

    private static int ToBcd(int code)
    {
        int bcd = 0;
        for (int shift = 0, tmp = code; shift < 16; shift += 4, tmp /= 10)
            bcd |= (tmp % 10) << shift;
        return bcd;
    }
}
