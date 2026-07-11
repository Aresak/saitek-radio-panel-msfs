namespace CustomRadioPanel.Core.Hardware;

/// <summary>
/// Stateless-per-call decoder that turns successive 3-byte HID input reports from the
/// radio panel into high-level <see cref="PanelEvent"/>s. It keeps the previous report so it
/// can detect edges (button presses and encoder detents) and selector position changes.
///
/// Bit layout of the 24-bit report (verified against bjanders/fpanels):
///   bits  0-6  top selector    COM1,COM2,NAV1,NAV2,ADF,DME,XPDR
///   bits  7-13 bottom selector COM1,COM2,NAV1,NAV2,ADF,DME,XPDR
///   bit  14    top ACT/STBY,    bit 15 bottom ACT/STBY
///   bits 16-19 top    inner-CW, inner-CCW, outer-CW, outer-CCW
///   bits 20-23 bottom inner-CW, inner-CCW, outer-CW, outer-CCW
/// </summary>
public sealed class PanelInputDecoder
{
    private int? _previous;
    private RadioMode? _topMode;
    private RadioMode? _bottomMode;

    /// <summary>Forgets prior state so the next report re-initialises selectors (used on reconnect).</summary>
    public void Reset()
    {
        _previous = null;
        _topMode = null;
        _bottomMode = null;
    }

    /// <summary>Decodes one 3-byte report into zero or more events.</summary>
    public IReadOnlyList<PanelEvent> Decode(ReadOnlySpan<byte> report)
    {
        if (report.Length < 3)
            return Array.Empty<PanelEvent>();

        int cur = report[0] | (report[1] << 8) | (report[2] << 16);
        var events = new List<PanelEvent>();

        var top = DecodeSelector(cur, 0);
        if (top is RadioMode tm && tm != _topMode)
        {
            _topMode = tm;
            events.Add(new SelectorChanged(PanelRow.Top, tm));
        }

        var bottom = DecodeSelector(cur, 7);
        if (bottom is RadioMode bm && bm != _bottomMode)
        {
            _bottomMode = bm;
            events.Add(new SelectorChanged(PanelRow.Bottom, bm));
        }

        if (_previous is int prev)
        {
            EdgeButton(events, prev, cur, 14, PanelRow.Top);
            EdgeButton(events, prev, cur, 15, PanelRow.Bottom);

            Rising(events, prev, cur, 16, PanelRow.Top, Knob.Inner, TurnDirection.Clockwise);
            Rising(events, prev, cur, 17, PanelRow.Top, Knob.Inner, TurnDirection.CounterClockwise);
            Rising(events, prev, cur, 18, PanelRow.Top, Knob.Outer, TurnDirection.Clockwise);
            Rising(events, prev, cur, 19, PanelRow.Top, Knob.Outer, TurnDirection.CounterClockwise);
            Rising(events, prev, cur, 20, PanelRow.Bottom, Knob.Inner, TurnDirection.Clockwise);
            Rising(events, prev, cur, 21, PanelRow.Bottom, Knob.Inner, TurnDirection.CounterClockwise);
            Rising(events, prev, cur, 22, PanelRow.Bottom, Knob.Outer, TurnDirection.Clockwise);
            Rising(events, prev, cur, 23, PanelRow.Bottom, Knob.Outer, TurnDirection.CounterClockwise);
        }

        _previous = cur;
        return events;
    }

    private static RadioMode? DecodeSelector(int value, int baseBit)
    {
        for (int i = 0; i < 7; i++)
        {
            if ((value & (1 << (baseBit + i))) != 0)
                return (RadioMode)i;
        }
        return null; // between detents / not yet reported
    }

    private static bool Bit(int value, int bit) => (value & (1 << bit)) != 0;

    private static void EdgeButton(List<PanelEvent> events, int prev, int cur, int bit, PanelRow row)
    {
        bool was = Bit(prev, bit), now = Bit(cur, bit);
        if (!was && now)
            events.Add(new ActStbyDown(row));
        else if (was && !now)
            events.Add(new ActStbyUp(row));
    }

    private static void Rising(List<PanelEvent> events, int prev, int cur, int bit, PanelRow row, Knob knob, TurnDirection dir)
    {
        if (!Bit(prev, bit) && Bit(cur, bit))
            events.Add(new EncoderTurned(row, knob, dir, 1));
    }
}
