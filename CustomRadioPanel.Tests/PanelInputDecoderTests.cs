using CustomRadioPanel.Core.Hardware;

namespace CustomRadioPanel.Tests;

public class PanelInputDecoderTests
{
    private static byte[] Report(int value24) =>
        new[] { (byte)(value24 & 0xFF), (byte)((value24 >> 8) & 0xFF), (byte)((value24 >> 16) & 0xFF) };

    [Fact]
    public void FirstReport_EmitsSelectorPositions_ForBothRows()
    {
        var dec = new PanelInputDecoder();
        // bit 0 = top COM1, bit 9 = bottom NAV1 (7 + 2)
        var events = dec.Decode(Report((1 << 0) | (1 << 9)));

        Assert.Contains(events, e => e is SelectorChanged { Row: PanelRow.Top, Mode: RadioMode.Com1 });
        Assert.Contains(events, e => e is SelectorChanged { Row: PanelRow.Bottom, Mode: RadioMode.Nav1 });
    }

    [Fact]
    public void SelectorChange_EmittedOnlyOnChange()
    {
        var dec = new PanelInputDecoder();
        dec.Decode(Report(1 << 0)); // top COM1
        var again = dec.Decode(Report(1 << 0));
        Assert.DoesNotContain(again, e => e is SelectorChanged { Row: PanelRow.Top });

        var moved = dec.Decode(Report(1 << 1)); // top COM2
        Assert.Contains(moved, e => e is SelectorChanged { Row: PanelRow.Top, Mode: RadioMode.Com2 });
    }

    [Fact]
    public void EncoderRisingEdge_EmitsOneTick()
    {
        var dec = new PanelInputDecoder();
        dec.Decode(Report(1 << 0));                    // establish baseline (top COM1)
        var turned = dec.Decode(Report((1 << 0) | (1 << 16))); // top inner CW pulse

        var tick = Assert.Single(turned, e => e is EncoderTurned) as EncoderTurned;
        Assert.Equal(PanelRow.Top, tick!.Row);
        Assert.Equal(Knob.Inner, tick.Knob);
        Assert.Equal(TurnDirection.Clockwise, tick.Direction);
    }

    [Fact]
    public void OuterEncoderCounterClockwise_OnBottomRow()
    {
        var dec = new PanelInputDecoder();
        dec.Decode(Report(1 << 7)); // bottom COM1 baseline
        var turned = dec.Decode(Report((1 << 7) | (1 << 23))); // bottom outer CCW

        var tick = turned.OfType<EncoderTurned>().Single();
        Assert.Equal(PanelRow.Bottom, tick.Row);
        Assert.Equal(Knob.Outer, tick.Knob);
        Assert.Equal(TurnDirection.CounterClockwise, tick.Direction);
    }

    [Fact]
    public void Button_EmitsDownThenUp()
    {
        var dec = new PanelInputDecoder();
        dec.Decode(Report(1 << 0));
        var down = dec.Decode(Report((1 << 0) | (1 << 14)));
        Assert.Contains(down, e => e is ActStbyDown { Row: PanelRow.Top });

        var up = dec.Decode(Report(1 << 0));
        Assert.Contains(up, e => e is ActStbyUp { Row: PanelRow.Top });
    }
}
