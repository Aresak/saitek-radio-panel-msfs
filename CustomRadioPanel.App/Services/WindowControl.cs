using System.Runtime.InteropServices;
using Microsoft.JSInterop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CustomRadioPanel.App.Services;

/// <summary>Window operations the borderless UI needs (drag, minimize, close).</summary>
public interface IWindowControl
{
    void Minimize();
    void Close();
}

/// <summary>
/// Controls the single native window. Because the window is borderless, the title bar (drag,
/// minimize, close) is drawn in HTML and driven from here. The AppWindow is captured once at
/// startup via <see cref="ConfigureAtStartup"/>.
/// </summary>
public sealed class WindowControl : IWindowControl
{
    private static AppWindow? _appWindow;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr prevProc, IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004;

    // Kept alive for the process lifetime so the delegate isn't collected while it is the window proc.
    private static WndProcDelegate? _hook;
    private static IntPtr _originalProc;

    /// <summary>
    /// Removes the whole non-client frame (the light Windows-11 border ring) by making the client
    /// area cover the entire window. Presenter/DWM tricks don't reliably kill it; this does.
    /// </summary>
    private static void RemoveWindowFrame(IntPtr hwnd)
    {
        _hook = HookProc;
        _originalProc = GetWindowLongPtr(hwnd, GWLP_WNDPROC);
        SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_hook));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private static IntPtr HookProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Returning 0 for a WM_NCCALCSIZE with wParam=TRUE tells Windows the client area is the
        // full window rectangle — no non-client border is drawn.
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            return IntPtr.Zero;
        return CallWindowProc(_originalProc, hwnd, msg, wParam, lParam);
    }

    /// <summary>Called from the MAUI lifecycle when the native window is created.</summary>
    public static void ConfigureAtStartup(Microsoft.UI.Xaml.Window window, int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        _appWindow = appWindow;

        var presenter = appWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = true;
        presenter.SetBorderAndTitleBar(false, false); // borderless
        appWindow.SetPresenter(presenter);

        RemoveWindowFrame(hwnd);

        double scale = GetDpiForWindow(hwnd) / 96.0;
        appWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
        CenterOnScreen(appWindow);
    }

    private static void CenterOnScreen(AppWindow appWindow)
    {
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        if (area is null)
            return;
        int x = (area.WorkArea.Width - appWindow.Size.Width) / 2 + area.WorkArea.X;
        int y = (area.WorkArea.Height - appWindow.Size.Height) / 2 + area.WorkArea.Y;
        appWindow.Move(new PointInt32(x, y));
    }

    public void Minimize()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
            presenter.Minimize();
    }

    public void Close() => Application.Current?.Quit();

    // ---- JS interop for dragging the borderless window ----

    [JSInvokable]
    public static int[] GetWindowPosition() =>
        _appWindow is { } w ? new[] { w.Position.X, w.Position.Y } : new[] { 0, 0 };

    [JSInvokable]
    public static void MoveWindow(int x, int y) => _appWindow?.Move(new PointInt32(x, y));
}
