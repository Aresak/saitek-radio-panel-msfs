using CustomRadioPanel.Core.Config;
using CustomRadioPanel.Core.Hardware;
using CustomRadioPanel.Core.Sim;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomRadioPanel.Core.Logic;

/// <summary>
/// The bridge: maps radio-panel input to SimConnect actions and renders both rows' displays from
/// the live sim snapshot. This is the single place that decides "what the panel does".
/// </summary>
public sealed class RadioPanelController : IDisposable
{
    private readonly IRadioPanel _panel;
    private readonly ISimConnectService _sim;
    private readonly ConfigService _configService;
    private readonly ILogger<RadioPanelController> _log;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private AppConfig _config;
    private EncoderAccelerator _accel;

    private RadioMode? _topMode;
    private RadioMode? _bottomMode;
    private readonly Dictionary<PanelRow, ITimer> _longPressTimers = new();
    private readonly HashSet<PanelRow> _longFired = new();
    private volatile bool _welcomeActive;

    public RadioPanelController(
        IRadioPanel panel,
        ISimConnectService sim,
        ConfigService configService,
        ILogger<RadioPanelController>? log = null,
        TimeProvider? time = null)
    {
        _panel = panel;
        _sim = sim;
        _configService = configService;
        _log = log ?? NullLogger<RadioPanelController>.Instance;
        _time = time ?? TimeProvider.System;
        _config = _configService.Current;
        _accel = new EncoderAccelerator(_config.Encoder, _time);
    }

    // ---- observable state for the UI ----
    public bool PanelConnected => _panel.IsConnected;
    public bool SimConnected => _sim.IsConnected;
    public RadioMode? TopMode => _topMode;
    public RadioMode? BottomMode => _bottomMode;
    public string TopLeft { get; private set; } = "";
    public string TopRight { get; private set; } = "";
    public string BottomLeft { get; private set; } = "";
    public string BottomRight { get; private set; } = "";

    /// <summary>Raised whenever connection state or display content changes (for UI refresh).</summary>
    public event Action? StateChanged;

    public void Start()
    {
        _config = _configService.Load();
        _accel = new EncoderAccelerator(_config.Encoder, _time);

        _panel.Event += OnPanelEvent;
        _panel.ConnectionChanged += OnPanelConnectionChanged;
        _sim.DataUpdated += OnSimData;
        _sim.ConnectionChanged += _ => RaiseState();

        (_panel as RadioPanelDevice)?.Start();
        _sim.Start();

        if (_config.Welcome.Enabled)
            PlayWelcome();
        else
            RefreshDisplays();
    }

    /// <summary>Re-applies configuration after the user edits settings.</summary>
    public void ApplyConfig(AppConfig config)
    {
        lock (_gate)
        {
            _config = config;
            _configService.Save(config);
            _accel = new EncoderAccelerator(_config.Encoder, _time);
        }
        RefreshDisplays();
    }

    private void OnPanelConnectionChanged(bool _) { RefreshDisplays(); RaiseState(); }
    private void OnSimData() => RefreshDisplays();

    private void OnPanelEvent(PanelEvent ev)
    {
        try
        {
            switch (ev)
            {
                case SelectorChanged sc:
                    if (sc.Row == PanelRow.Top) _topMode = sc.Mode; else _bottomMode = sc.Mode;
                    _accel.Reset();
                    _log.LogDebug("Selector {Row} -> {Mode}", sc.Row, sc.Mode);
                    RefreshDisplays();
                    break;

                case EncoderTurned et:
                    HandleEncoder(et);
                    break;

                case ActStbyDown down:
                    StartLongPressTimer(down.Row);
                    break;

                case ActStbyUp up:
                    HandleButtonRelease(up.Row);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error handling panel event {Event}.", ev);
        }
    }

    private RadioMode? ModeFor(PanelRow row) => row == PanelRow.Top ? _topMode : _bottomMode;

    private void HandleEncoder(EncoderTurned et)
    {
        var mode = ModeFor(et.Row);
        if (mode is not RadioMode m)
            return;

        int dir = (int)et.Direction;
        int mult = (int)Math.Max(1, _accel.Multiplier(et.Row, et.Knob) * _config.Encoder.GlobalStepScale);
        var snap = _sim.Snapshot;

        switch (m)
        {
            case RadioMode.Com1 or RadioMode.Com2 or RadioMode.Nav1 or RadioMode.Nav2:
                TuneComNav(m, et.Knob, dir, mult, snap);
                break;

            case RadioMode.Adf:
                double adfStep = (et.Knob == Knob.Outer ? 10 : 1) * mult * dir;
                double adf = Clamp(RadioFormat.SnapToGrid(snap.Adf1 + adfStep, 1), 190, 1799);
                snap.Adf1 = adf;
                _sim.SetAdfKHz(1, adf);
                _log.LogDebug("Tune ADF1 {Knob} dir {Dir} x{Mult} -> {Adf} kHz", et.Knob, dir, mult, adf);
                break;

            case RadioMode.Xpdr:
                if (et.Knob == Knob.Inner)
                {
                    // Exactly one squawk step per detent — a discrete code must never accelerate/skip.
                    int current = RadioFormat.BcdToCode(snap.TransponderCode);
                    int code = RadioFormat.AdjustSquawk(current, dir);
                    _sim.SetTransponder(code);
                    snap.TransponderCode = ToBcd(code); // optimistic BCO16 for the local snapshot
                    _log.LogDebug("Squawk dir {Dir}: {Current:0000} -> {Code:0000}", dir, current, code);
                }
                else
                {
                    double mb = Clamp(RadioFormat.SnapToGrid(snap.KohlsmanMb + dir * mult, 1), 900, 1100);
                    snap.KohlsmanMb = mb;
                    _sim.SetKohlsmanMb(mb);
                    _log.LogDebug("QNH dir {Dir} x{Mult} -> {Mb} hPa", dir, mult, mb);
                }
                break;

            case RadioMode.Dme:
                return; // read-only display; nothing to tune
        }

        RefreshDisplays();
    }

    private void TuneComNav(RadioMode m, Knob knob, int dir, int mult, RadioSnapshot snap)
    {
        bool com = m is RadioMode.Com1 or RadioMode.Com2;
        double inner = com ? (_config.Display.Com833Spacing ? 0.005 : 0.025) : 0.05;
        double step = (knob == Knob.Outer ? 1.0 : inner) * mult * dir;

        double current = m switch
        {
            RadioMode.Com1 => snap.Com1Standby,
            RadioMode.Com2 => snap.Com2Standby,
            RadioMode.Nav1 => snap.Nav1Standby,
            _ => snap.Nav2Standby,
        };

        double lo = com ? 118.0 : 108.0;
        double hi = com ? 136.99 : 117.95;
        double grid = com ? inner : 0.05;
        double next = Clamp(RadioFormat.SnapToGrid(current + step, grid), lo, hi);

        switch (m)
        {
            case RadioMode.Com1: snap.Com1Standby = next; break;
            case RadioMode.Com2: snap.Com2Standby = next; break;
            case RadioMode.Nav1: snap.Nav1Standby = next; break;
            default: snap.Nav2Standby = next; break;
        }
        _sim.SetStandbyMHz(m, next);
        _log.LogDebug("Tune {Mode} STBY {Knob} dir {Dir} x{Mult} -> {Next:000.000} MHz", m, knob, dir, mult, next);
    }

    // Long-press fires on its own after LongPressMs while the button is still held (not on release).
    private void StartLongPressTimer(PanelRow row)
    {
        lock (_gate)
        {
            _longFired.Remove(row);
            if (_longPressTimers.TryGetValue(row, out var existing))
                existing.Dispose();
            _longPressTimers[row] = _time.CreateTimer(
                _ => OnLongPress(row), null, TimeSpan.FromMilliseconds(_config.LongPressMs), Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelLongPressTimer(PanelRow row)
    {
        lock (_gate)
        {
            if (_longPressTimers.TryGetValue(row, out var t))
            {
                t.Dispose();
                _longPressTimers.Remove(row);
            }
        }
    }

    private void OnLongPress(PanelRow row)
    {
        lock (_gate)
            _longFired.Add(row);

        var mode = ModeFor(row);
        if (mode is not RadioMode m)
            return;

        if (m == RadioMode.Xpdr)
        {
            _sim.SetBaroStandard();
            _log.LogInformation("Long-press {Row}: set standard pressure (STD).", row);
            RefreshDisplays();
        }
        // Other modes have no long-press action.
    }

    private void HandleButtonRelease(PanelRow row)
    {
        CancelLongPressTimer(row);

        bool longAlreadyFired;
        lock (_gate)
            longAlreadyFired = _longFired.Remove(row);
        if (longAlreadyFired)
            return; // the long-press already did its thing while the button was held

        var mode = ModeFor(row);
        if (mode is not RadioMode m)
            return;

        if (m is RadioMode.Com1 or RadioMode.Com2 or RadioMode.Nav1 or RadioMode.Nav2)
        {
            _sim.Swap(m);
            _log.LogInformation("Short-press {Row}: swap {Mode} active/standby.", row, m);
            RefreshDisplays();
        }
    }

    private void PlayWelcome()
    {
        var w = _config.Welcome;
        // Right-to-left scroll: 10 leading blanks so it starts empty and text enters from the right.
        // Each row spans both 5-digit displays => a 10-char window (left 5 + right 5).
        string up = new string(' ', 10) + (w.UpperMessage ?? "") + new string(' ', 10);
        string lo = new string(' ', 10) + (w.LowerMessage ?? "") + new string(' ', 10);
        int intervalMs = Math.Max(60, 1000 / Math.Max(1, w.Speed));
        _welcomeActive = true;
        _log.LogInformation("Playing welcome message for {Seconds}s.", w.Time);

        _ = Task.Run(async () =>
        {
            long start = _time.GetTimestamp();
            int pos = 0;
            try
            {
                while (_welcomeActive && _time.GetElapsedTime(start).TotalSeconds < w.Time)
                {
                    var (ul, ur) = Split(up, pos);
                    var (ll, lr) = Split(lo, pos);
                    // UI-only: the panel's 7-seg font is digits-only, so text can't render on the
                    // hardware. We update the on-screen displays and leave the physical panel clear.
                    lock (_gate)
                    {
                        TopLeft = ul; TopRight = ur; BottomLeft = ll; BottomRight = lr;
                    }
                    RaiseState();
                    pos++;
                    await Task.Delay(intervalMs);
                }
            }
            finally
            {
                _welcomeActive = false;
                RefreshDisplays(); // hand over to normal radio display
            }
        });
    }

    // Returns the 10-char window at position pos (looping), split into two 5-char halves.
    private static (string left, string right) Split(string padded, int pos)
    {
        int frames = Math.Max(1, padded.Length - 10);
        int p = pos % frames;
        string window = padded.Substring(p, Math.Min(10, padded.Length - p)).PadRight(10);
        return (window.Substring(0, 5), window.Substring(5, 5));
    }

    private void RefreshDisplays()
    {
        if (_welcomeActive) return;

        lock (_gate)
        {
            var snap = _sim.Snapshot;
            (TopLeft, TopRight) = DisplayFor(_topMode, snap);
            (BottomLeft, BottomRight) = DisplayFor(_bottomMode, snap);

            var buffer = SevenSegment.BuildBuffer(TopLeft, TopRight, BottomLeft, BottomRight);
            _panel.SetDisplay(buffer);
        }
        RaiseState();
    }

    private (string left, string right) DisplayFor(RadioMode? mode, RadioSnapshot s)
    {
        switch (mode)
        {
            case RadioMode.Com1: return (Freq(s.Com1Active), MaybeBlank(s.Com1Active, s.Com1Standby));
            case RadioMode.Com2: return (Freq(s.Com2Active), MaybeBlank(s.Com2Active, s.Com2Standby));
            case RadioMode.Nav1: return (Freq(s.Nav1Active), MaybeBlank(s.Nav1Active, s.Nav1Standby));
            case RadioMode.Nav2: return (Freq(s.Nav2Active), MaybeBlank(s.Nav2Active, s.Nav2Standby));
            case RadioMode.Adf: return (RadioFormat.Adf(s.Adf1), RadioFormat.Adf(s.Adf2));
            case RadioMode.Dme: return (RadioFormat.Dme(s.Dme1), RadioFormat.Dme(s.Dme2));
            case RadioMode.Xpdr: return (RadioFormat.Kohlsman(s.KohlsmanMb), RadioFormat.Squawk(s.TransponderCode));
            default: return ("", "");
        }
    }

    private string Freq(double mhz) => RadioFormat.ComNav(mhz, _config.Display.HideHundredsDigit);

    private string MaybeBlank(double active, double standby)
        => _config.Display.BlankStandbyWhenEqual && Math.Abs(active - standby) < 0.0005
            ? string.Empty
            : Freq(standby);

    private static double ToBcd(int code)
    {
        int bcd = 0;
        for (int shift = 0, tmp = code; shift < 16; shift += 4, tmp /= 10)
            bcd |= (tmp % 10) << shift;
        return bcd;
    }

    private static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));

    private void RaiseState() => StateChanged?.Invoke();

    public void Dispose()
    {
        _panel.Event -= OnPanelEvent;
        _panel.ConnectionChanged -= OnPanelConnectionChanged;
        _sim.DataUpdated -= OnSimData;

        lock (_gate)
        {
            foreach (var t in _longPressTimers.Values)
                t.Dispose();
            _longPressTimers.Clear();
        }
    }
}
