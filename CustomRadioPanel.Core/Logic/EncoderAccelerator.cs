using CustomRadioPanel.Core.Config;
using CustomRadioPanel.Core.Hardware;

namespace CustomRadioPanel.Core.Logic;

/// <summary>
/// Turns raw detents into a step multiplier based on how fast the knob is being spun.
/// Slow turns => 1x (fine control); fast turns => bigger jumps. This is the configurable
/// "knob feel" the user wanted to improve over the fixed SPAD.neXt behaviour.
/// A separate cadence is tracked per (row, knob) so the two knobs never interfere.
/// </summary>
public sealed class EncoderAccelerator
{
    private readonly EncoderConfig _cfg;
    private readonly TimeProvider _time;
    private readonly Dictionary<(PanelRow, Knob), long> _lastTimestamp = new();

    public EncoderAccelerator(EncoderConfig cfg, TimeProvider? time = null)
    {
        _cfg = cfg;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Returns the multiplier to apply to the base step for this detent.</summary>
    public int Multiplier(PanelRow row, Knob knob)
    {
        long now = _time.GetTimestamp();
        int multiplier = 1;

        if (_cfg.AccelerationEnabled && _lastTimestamp.TryGetValue((row, knob), out long prev))
        {
            double ms = _time.GetElapsedTime(prev, now).TotalMilliseconds;
            if (ms <= _cfg.FastThresholdMs)
                multiplier = Math.Max(1, _cfg.FastMultiplier);
            else if (ms <= _cfg.MediumThresholdMs)
                multiplier = Math.Max(1, _cfg.MediumMultiplier);
        }

        _lastTimestamp[(row, knob)] = now;
        return multiplier;
    }

    /// <summary>Clears cadence history (e.g. when the selector changes or the device reconnects).</summary>
    public void Reset() => _lastTimestamp.Clear();
}
