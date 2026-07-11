using CustomRadioPanel.Core.Hardware;

namespace CustomRadioPanel.Core.Sim;

/// <summary>
/// Bridge to MSFS via SimConnect. Exposes a live <see cref="RadioSnapshot"/> and the small set of
/// write actions the radio panel needs. Implementations reconnect automatically.
/// </summary>
public interface ISimConnectService : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Most recent values received from the sim (updated in place; read-only for callers).</summary>
    RadioSnapshot Snapshot { get; }

    event Action<bool>? ConnectionChanged;

    /// <summary>Raised whenever a fresh data packet arrives from the sim.</summary>
    event Action? DataUpdated;

    /// <summary>Begins connection attempts and the message pump (idempotent).</summary>
    void Start();

    /// <summary>Swaps active/standby for a COM or NAV radio.</summary>
    void Swap(RadioMode mode);

    /// <summary>Sets the standby frequency (MHz) for a COM or NAV radio.</summary>
    void SetStandbyMHz(RadioMode mode, double mhz);

    /// <summary>Sets the active frequency (MHz) for a COM or NAV radio.</summary>
    void SetActiveMHz(RadioMode mode, double mhz);

    /// <summary>Sets the active ADF frequency (kHz).</summary>
    void SetAdfKHz(int index, double khz);

    /// <summary>Sets the transponder squawk (4-digit code, e.g. 1200).</summary>
    void SetTransponder(int code);

    /// <summary>Sets the altimeter (Kohlsman) setting in millibars/hPa.</summary>
    void SetKohlsmanMb(double mb);

    /// <summary>Sets standard pressure (1013 hPa).</summary>
    void SetBaroStandard();
}
