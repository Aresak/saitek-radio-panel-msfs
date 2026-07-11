using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using CustomRadioPanel.Core.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomRadioPanel.Core.Sim;

/// <summary>
/// SimConnect client implemented with direct P/Invoke against the native (x64) SimConnect.dll.
/// We deliberately do NOT use the SDK's managed Microsoft.FlightSimulator.SimConnect.dll — that is a
/// .NET-Framework mixed-mode (C++/CLI) assembly which .NET 10 refuses to load ("architecture not
/// compatible"). The native C API is stable and loads fine.
///
/// A single background thread owns the connection: it polls the dispatch queue and also drains an
/// action queue, so every native SimConnect call happens on that one thread (SimConnect is not
/// designed for concurrent calls from multiple threads).
///
/// IMPORTANT: MSFS frequency SimVars are NOT writable via SetDataOnSimObject — writes are silently
/// ignored. All changes are therefore made with key events (COM/NAV use the *_SET_HZ variants that
/// take a frequency in Hz, which supports 8.33 kHz spacing / VATSIM).
/// </summary>
public sealed class SimConnectService : ISimConnectService
{
    private const string DLL = "SimConnect.dll";

    // SIMCONNECT_RECV_ID
    private const int RECV_ID_EXCEPTION = 1;
    private const int RECV_ID_OPEN = 2;
    private const int RECV_ID_QUIT = 3;
    private const int RECV_ID_SIMOBJECT_DATA = 8;

    // misc constants
    private const uint OBJECT_ID_USER = 0;
    private const uint UNUSED = 0xffffffff;
    private const int DATATYPE_FLOAT64 = 4;
    private const int PERIOD_SECOND = 4;
    private const int REQUEST_FLAG_CHANGED = 1;
    private const uint GROUP_PRIORITY_HIGHEST = 1;
    private const int EVENT_FLAG_GROUPID_IS_PRIORITY = 0x10;

    // read data-definition id
    private const uint DEF_RADIO = 0;

    // request id
    private const uint REQ_RADIO = 0;

    // event ids
    private const uint EVT_COM1_SWAP = 0, EVT_COM2_SWAP = 1, EVT_NAV1_SWAP = 2, EVT_NAV2_SWAP = 3, EVT_BARO_STD = 4;
    private const uint EVT_COM1_SET = 5, EVT_COM2_SET = 6, EVT_NAV1_SET = 7, EVT_NAV2_SET = 8;
    private const uint EVT_XPNDR_SET = 9, EVT_KOHLSMAN_SET = 10, EVT_ADF_SET = 11;
    private const uint EVT_COM1_ACT = 12, EVT_COM2_ACT = 13, EVT_NAV1_ACT = 14, EVT_NAV2_ACT = 15;

    // data offset of SIMCONNECT_RECV_SIMOBJECT_DATA.dwData
    private const int SIMOBJECT_DATA_OFFSET = 40;

    private readonly ILogger<SimConnectService> _log;
    private readonly RadioSnapshot _snapshot = new();
    private readonly ConcurrentQueue<Action> _actions = new();

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private IntPtr _handle = IntPtr.Zero;
    private volatile bool _connected;
    private volatile bool _running;

    static SimConnectService()
    {
        // SimConnect.dll is not shipped with the app; locate the user's copy (MSFS install) at load time.
        NativeLibrary.SetDllImportResolver(typeof(SimConnectService).Assembly, ResolveNative);
    }

    private static IntPtr ResolveNative(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.StartsWith("SimConnect", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero; // let the default resolver handle anything else

        var path = SimConnectLocator.Find();
        if (path is not null && NativeLibrary.TryLoad(path, out var handle))
            return handle;

        return IntPtr.Zero; // fall back to default probing (app dir / PATH)
    }

    public SimConnectService(ILogger<SimConnectService>? log = null)
        => _log = log ?? NullLogger<SimConnectService>.Instance;

    public bool IsConnected => _connected;
    public RadioSnapshot Snapshot => _snapshot;

    public event Action<bool>? ConnectionChanged;
    public event Action? DataUpdated;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RadioData
    {
        public double Com1Active, Com1Standby, Com2Active, Com2Standby;
        public double Nav1Active, Nav1Standby, Nav2Active, Nav2Standby;
        public double Adf1, Adf2;
        public double Dme1, Dme2;
        public double Xpdr, Kohlsman;
    }

    #region native
    [DllImport(DLL, CharSet = CharSet.Ansi)]
    private static extern int SimConnect_Open(out IntPtr phSimConnect, string szName, IntPtr hWnd, uint userEventWin32, IntPtr hEventHandle, uint configIndex);

    [DllImport(DLL)]
    private static extern int SimConnect_Close(IntPtr hSimConnect);

    [DllImport(DLL)]
    private static extern int SimConnect_GetNextDispatch(IntPtr hSimConnect, out IntPtr ppData, out uint pcbData);

    [DllImport(DLL, CharSet = CharSet.Ansi)]
    private static extern int SimConnect_AddToDataDefinition(IntPtr h, uint defineID, string datumName, string unitsName, int datumType, float epsilon, uint datumID);

    [DllImport(DLL)]
    private static extern int SimConnect_RequestDataOnSimObject(IntPtr h, uint requestID, uint defineID, uint objectID, int period, int flags, uint origin, uint interval, uint limit);

    [DllImport(DLL, CharSet = CharSet.Ansi)]
    private static extern int SimConnect_MapClientEventToSimEvent(IntPtr h, uint eventID, string eventName);

    [DllImport(DLL)]
    private static extern int SimConnect_TransmitClientEvent(IntPtr h, uint objectID, uint eventID, uint data, uint groupID, int flags);
    #endregion

    public void Start()
    {
        if (_thread is not null)
            return;

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => Run(_cts.Token))
        {
            IsBackground = true,
            Name = "SimConnectPump",
        };
        _thread.Start();
    }

    private void Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (SimConnect_Open(out _handle, "CustomRadioPanel", IntPtr.Zero, 0, IntPtr.Zero, 0) != 0)
                {
                    _handle = IntPtr.Zero;
                    Thread.Sleep(2000); // sim not running yet
                    continue;
                }

                Configure();
                _running = true;

                while (!ct.IsCancellationRequested && _running)
                {
                    while (SimConnect_GetNextDispatch(_handle, out IntPtr pData, out _) == 0 && pData != IntPtr.Zero)
                        Dispatch(pData);

                    while (_actions.TryDequeue(out var action))
                    {
                        try { action(); }
                        catch (Exception ex) { _log.LogDebug(ex, "SimConnect write action failed."); }
                    }

                    Thread.Sleep(30);
                }
            }
            catch (DllNotFoundException)
            {
                _log.LogWarning("SimConnect.dll not found — the sim link is disabled. Install/run MSFS, " +
                                "set the SIMCONNECT_DLL environment variable, or copy SimConnect.dll next to the app.");
                return; // no point retrying if the library isn't on the machine
            }
            catch (BadImageFormatException)
            {
                _log.LogWarning("SimConnect.dll is the wrong architecture (need 64-bit). The sim link is disabled.");
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "SimConnect loop error; will retry.");
            }
            finally
            {
                Cleanup();
            }

            if (!ct.IsCancellationRequested)
                Thread.Sleep(2000);
        }
    }

    private void Configure()
    {
        // read definition (order must match RadioData)
        Add("COM ACTIVE FREQUENCY:1", "MHz");
        Add("COM STANDBY FREQUENCY:1", "MHz");
        Add("COM ACTIVE FREQUENCY:2", "MHz");
        Add("COM STANDBY FREQUENCY:2", "MHz");
        Add("NAV ACTIVE FREQUENCY:1", "MHz");
        Add("NAV STANDBY FREQUENCY:1", "MHz");
        Add("NAV ACTIVE FREQUENCY:2", "MHz");
        Add("NAV STANDBY FREQUENCY:2", "MHz");
        Add("ADF ACTIVE FREQUENCY:1", "KHz");
        Add("ADF ACTIVE FREQUENCY:2", "KHz");
        Add("NAV DME:1", "Nautical miles");
        Add("NAV DME:2", "Nautical miles");
        Add("TRANSPONDER CODE:1", "BCO16"); // binary-coded octal (each nibble = one squawk digit)
        Add("KOHLSMAN SETTING MB", "Millibars");

        // swap events
        Map(EVT_COM1_SWAP, "COM_STBY_RADIO_SWAP");
        Map(EVT_COM2_SWAP, "COM2_RADIO_SWAP");
        Map(EVT_NAV1_SWAP, "NAV1_RADIO_SWAP");
        Map(EVT_NAV2_SWAP, "NAV2_RADIO_SWAP");

        // set-standby-frequency events (value in Hz; support 8.33 kHz)
        Map(EVT_COM1_SET, "COM_STBY_RADIO_SET_HZ");
        Map(EVT_COM2_SET, "COM2_STBY_RADIO_SET_HZ");
        Map(EVT_NAV1_SET, "NAV1_STBY_SET_HZ");
        Map(EVT_NAV2_SET, "NAV2_STBY_SET_HZ");

        // set-active-frequency events (value in Hz)
        Map(EVT_COM1_ACT, "COM_RADIO_SET_HZ");
        Map(EVT_COM2_ACT, "COM2_RADIO_SET_HZ");
        Map(EVT_NAV1_ACT, "NAV1_RADIO_SET_HZ");
        Map(EVT_NAV2_ACT, "NAV2_RADIO_SET_HZ");

        // other radios
        Map(EVT_ADF_SET, "ADF_ACTIVE_SET_HZ");   // value in Hz (verify token against your build)
        Map(EVT_XPNDR_SET, "XPNDR_SET");          // value = BCD16 code
        Map(EVT_KOHLSMAN_SET, "KOHLSMAN_SET");    // value = millibars * 16
        Map(EVT_BARO_STD, "BAROMETRIC_STD_PRESSURE");

        // stream the read data once per second, only on change
        SimConnect_RequestDataOnSimObject(_handle, REQ_RADIO, DEF_RADIO, OBJECT_ID_USER,
            PERIOD_SECOND, REQUEST_FLAG_CHANGED, 0, 0, 0);
    }

    private void Add(string name, string unit) =>
        SimConnect_AddToDataDefinition(_handle, DEF_RADIO, name, unit, DATATYPE_FLOAT64, 0f, UNUSED);

    private void Map(uint ev, string name) =>
        SimConnect_MapClientEventToSimEvent(_handle, ev, name);

    private void Dispatch(IntPtr pData)
    {
        int id = Marshal.ReadInt32(pData, 8); // SIMCONNECT_RECV.dwID
        switch (id)
        {
            case RECV_ID_OPEN:
                _connected = true;
                _log.LogInformation("Connected to MSFS via SimConnect.");
                ConnectionChanged?.Invoke(true);
                break;

            case RECV_ID_QUIT:
                _log.LogInformation("MSFS closed the SimConnect connection.");
                _running = false;
                break;

            case RECV_ID_EXCEPTION:
                _log.LogDebug("SimConnect exception {Code}.", (uint)Marshal.ReadInt32(pData, 12));
                break;

            case RECV_ID_SIMOBJECT_DATA:
                uint request = (uint)Marshal.ReadInt32(pData, 12);
                if (request == REQ_RADIO)
                {
                    var d = Marshal.PtrToStructure<RadioData>(IntPtr.Add(pData, SIMOBJECT_DATA_OFFSET));
                    _snapshot.Com1Active = d.Com1Active;
                    _snapshot.Com1Standby = d.Com1Standby;
                    _snapshot.Com2Active = d.Com2Active;
                    _snapshot.Com2Standby = d.Com2Standby;
                    _snapshot.Nav1Active = d.Nav1Active;
                    _snapshot.Nav1Standby = d.Nav1Standby;
                    _snapshot.Nav2Active = d.Nav2Active;
                    _snapshot.Nav2Standby = d.Nav2Standby;
                    _snapshot.Adf1 = d.Adf1;
                    _snapshot.Adf2 = d.Adf2;
                    _snapshot.Dme1 = d.Dme1;
                    _snapshot.Dme2 = d.Dme2;
                    _snapshot.TransponderCode = d.Xpdr;
                    _snapshot.KohlsmanMb = d.Kohlsman;
                    DataUpdated?.Invoke();
                }
                break;
        }
    }

    // ---- write actions (queued onto the pump thread) ----

    public void Swap(RadioMode mode)
    {
        uint? ev = mode switch
        {
            RadioMode.Com1 => EVT_COM1_SWAP,
            RadioMode.Com2 => EVT_COM2_SWAP,
            RadioMode.Nav1 => EVT_NAV1_SWAP,
            RadioMode.Nav2 => EVT_NAV2_SWAP,
            _ => null,
        };
        if (ev is uint e)
            Transmit(e, 0);
    }

    public void SetStandbyMHz(RadioMode mode, double mhz)
    {
        uint? ev = mode switch
        {
            RadioMode.Com1 => EVT_COM1_SET,
            RadioMode.Com2 => EVT_COM2_SET,
            RadioMode.Nav1 => EVT_NAV1_SET,
            RadioMode.Nav2 => EVT_NAV2_SET,
            _ => null,
        };
        if (ev is uint e)
            Transmit(e, (uint)Math.Round(mhz * 1_000_000)); // MHz -> Hz
    }

    public void SetActiveMHz(RadioMode mode, double mhz)
    {
        uint? ev = mode switch
        {
            RadioMode.Com1 => EVT_COM1_ACT,
            RadioMode.Com2 => EVT_COM2_ACT,
            RadioMode.Nav1 => EVT_NAV1_ACT,
            RadioMode.Nav2 => EVT_NAV2_ACT,
            _ => null,
        };
        if (ev is uint e)
            Transmit(e, (uint)Math.Round(mhz * 1_000_000));
    }

    public void SetAdfKHz(int index, double khz) =>
        Transmit(EVT_ADF_SET, (uint)Math.Round(khz * 1_000)); // kHz -> Hz

    public void SetTransponder(int code) => Transmit(EVT_XPNDR_SET, DecimalToBcd(code));

    public void SetKohlsmanMb(double mb) => Transmit(EVT_KOHLSMAN_SET, (uint)Math.Round(mb * 16)); // hPa * 16

    public void SetBaroStandard() => Transmit(EVT_BARO_STD, 0);

    private void Transmit(uint ev, uint data)
    {
        if (_connected && _handle != IntPtr.Zero)
            _actions.Enqueue(() =>
                SimConnect_TransmitClientEvent(_handle, OBJECT_ID_USER, ev, data, GROUP_PRIORITY_HIGHEST, EVENT_FLAG_GROUPID_IS_PRIORITY));
    }

    /// <summary>Transponder SimVar expects a BCD16 value; convert a plain 4-digit code (e.g. 1200).</summary>
    private static uint DecimalToBcd(int code)
    {
        code = Math.Clamp(code, 0, 7777);
        uint bcd = 0;
        for (int shift = 0; shift < 16; shift += 4)
        {
            bcd |= (uint)(code % 10) << shift;
            code /= 10;
        }
        return bcd;
    }

    private void Cleanup()
    {
        _running = false;
        if (_connected)
        {
            _connected = false;
            ConnectionChanged?.Invoke(false);
        }

        while (_actions.TryDequeue(out _)) { }

        if (_handle != IntPtr.Zero)
        {
            try { SimConnect_Close(_handle); } catch { /* ignore */ }
            _handle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _thread?.Join(2500);
        }
        catch { /* ignore */ }
        finally
        {
            Cleanup();
            _cts?.Dispose();
        }
    }
}
