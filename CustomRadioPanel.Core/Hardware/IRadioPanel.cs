namespace CustomRadioPanel.Core.Hardware;

/// <summary>Abstraction over the physical radio panel so the bridge and UI can be tested with a fake.</summary>
public interface IRadioPanel
{
    /// <summary>True when the USB device is currently open.</summary>
    bool IsConnected { get; }

    /// <summary>Raised (on the read thread) for every decoded input event.</summary>
    event Action<PanelEvent>? Event;

    /// <summary>Raised when the device connects (true) or disconnects (false).</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>Raised for every raw 3-byte input report (for the debug monitor page).</summary>
    event Action<byte[]>? RawReport;

    /// <summary>Pushes a 22-byte display buffer (see <see cref="SevenSegment"/>) to the hardware.</summary>
    void SetDisplay(ReadOnlySpan<byte> buffer22);
}
