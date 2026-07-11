using HidSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomRadioPanel.Core.Hardware;

/// <summary>
/// Talks to the Logitech/Saitek Pro Flight Radio Panel over USB HID: opens the device by
/// VID/PID, runs a background read loop that decodes input reports into <see cref="PanelEvent"/>s,
/// and writes the 5-digit displays via a HID feature report. Automatically reconnects.
/// </summary>
public sealed class RadioPanelDevice : IRadioPanel, IDisposable
{
    public const int VendorId = 0x06A3;
    public const int ProductId = 0x0D05;

    private readonly ILogger<RadioPanelDevice> _log;
    private readonly PanelInputDecoder _decoder = new();
    private readonly object _writeLock = new();

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private HidStream? _stream;
    private volatile bool _connected;

    // Last buffer requested by the controller; resent whenever the device (re)connects.
    private byte[] _lastDisplay = SevenSegment.AllOff();

    public RadioPanelDevice(ILogger<RadioPanelDevice>? log = null)
        => _log = log ?? NullLogger<RadioPanelDevice>.Instance;

    public bool IsConnected => _connected;

    public event Action<PanelEvent>? Event;
    public event Action<bool>? ConnectionChanged;
    public event Action<byte[]>? RawReport;

    /// <summary>Starts the supervisor thread (idempotent).</summary>
    public void Start()
    {
        if (_thread is not null)
            return;

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Supervise(_cts.Token))
        {
            IsBackground = true,
            Name = "RadioPanelReader",
        };
        _thread.Start();
    }

    public void SetDisplay(ReadOnlySpan<byte> buffer22)
    {
        var copy = buffer22.Length == SevenSegment.BufferLength
            ? buffer22.ToArray()
            : Resize(buffer22);

        lock (_writeLock)
        {
            _lastDisplay = copy;
            WriteFeatureLocked(copy);
        }
    }

    private static byte[] Resize(ReadOnlySpan<byte> src)
    {
        var b = SevenSegment.AllOff();
        src.Slice(0, Math.Min(src.Length, b.Length)).CopyTo(b);
        return b;
    }

    private void Supervise(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HidStream? stream = null;
            try
            {
                var device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault();
                if (device is null || !device.TryOpen(out stream))
                {
                    Thread.Sleep(1000);
                    continue;
                }

                stream.ReadTimeout = 500;
                _stream = stream;
                _decoder.Reset();
                SetConnected(true);

                // Push whatever the controller last asked for so the displays are correct on reconnect.
                lock (_writeLock)
                    WriteFeatureLocked(_lastDisplay);

                ReadLoop(stream, device, ct);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Radio panel read loop ended; will retry.");
            }
            finally
            {
                _stream = null;
                stream?.Dispose();
                if (_connected)
                    SetConnected(false);
            }

            if (!ct.IsCancellationRequested)
                Thread.Sleep(1000);
        }
    }

    private void ReadLoop(HidStream stream, HidDevice device, CancellationToken ct)
    {
        int max = Math.Max(4, device.GetMaxInputReportLength());
        var buffer = new byte[max];

        while (!ct.IsCancellationRequested)
        {
            int count;
            try
            {
                count = stream.Read(buffer, 0, buffer.Length);
            }
            catch (TimeoutException)
            {
                continue; // lets us observe cancellation and keep the connection alive
            }

            if (count <= 0)
                continue;

            // On Windows the report is prefixed with a report-ID byte (0); the 3 data bytes follow.
            int offset = count >= 4 ? 1 : 0;
            if (count - offset < 3)
                continue;

            var data = new byte[3];
            Array.Copy(buffer, offset, data, 0, 3);
            RawReport?.Invoke(data);

            foreach (var ev in _decoder.Decode(data))
                Event?.Invoke(ev);
        }
    }

    private void WriteFeatureLocked(byte[] data22)
    {
        var stream = _stream;
        if (stream is null)
            return;

        try
        {
            int len = 23; // 1 report-ID byte + 22 data bytes
            var feature = new byte[len];
            feature[0] = 0x00; // report ID
            Array.Copy(data22, 0, feature, 1, Math.Min(data22.Length, len - 1));
            stream.SetFeature(feature);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to write radio panel display.");
        }
    }

    private void SetConnected(bool value)
    {
        _connected = value;
        _log.LogInformation("Radio panel {State}.", value ? "connected" : "disconnected");
        ConnectionChanged?.Invoke(value);
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _thread?.Join(1500);
        }
        catch
        {
            // ignore
        }
        finally
        {
            _stream?.Dispose();
            _cts?.Dispose();
        }
    }
}
