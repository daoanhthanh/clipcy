using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace Clipcy.Services;

public class WindowService : IWindowService
{
    private readonly Window _window;
    private SystemBackdropConfiguration? _configurationSource;
    public const int PopupWidthDip = 400;
    public const int PopupBaseHeightDip = 500;
    public const int PinnedRowHeightDip = 36;
    public const int PinnedMaxVisibleRows = 3;
    // Pinned list top margin (4) + separator top margin (4) + separator (1).
    public const int PinnedSectionChromeDip = 9;

    private int _extraHeightDip;

    // LoadCursor returns a shared system cursor; it must not be destroyed.
    private static readonly IntPtr ArrowCursor = LoadCursor(IntPtr.Zero, new IntPtr(32512));

    public static void RestoreArrowCursor()
    {
        if (ArrowCursor != IntPtr.Zero)
            SetCursor(ArrowCursor);
    }

    #region Native Methods

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", ExactSpelling = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr cursorName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags
    );

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_BORDER = 0x00800000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion

    public AppWindow AppWindow => _window.AppWindow;
    public bool IsVisible => _window.AppWindow.IsVisible;

    public WindowService(Window window)
    {
        _window = window;
    }

    public void Initialize()
    {
        ConfigureWindowStyle();
        SetupAcrylicBackdrop();
        ApplyRoundedCorners();
        MakeToolWindow();
    }

    public void Hide()
    {
        _window.AppWindow.Hide();
    }

    public void SetExtraHeightDip(int extraDip)
    {
        extraDip = Math.Max(0, extraDip);
        if (extraDip == _extraHeightDip)
            return;

        _extraHeightDip = extraDip;
        var (width, height) = ApplyPopupSize();
        var pos = _window.AppWindow.Position;
        if (IsVisible && pos.X > -2000 && pos.Y > -2000)
            ClampToWorkArea(width, height);
    }

    private (int Width, int Height) ApplyPopupSize()
    {
        var scale = GetDpiScale();
        int width = (int)(PopupWidthDip * scale);
        int height = (int)((PopupBaseHeightDip + _extraHeightDip) * scale);
        var size = _window.AppWindow.Size;
        if (size.Width != width || size.Height != height)
        {
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        }

        return (width, height);
    }

    private void ClampToWorkArea(int windowWidth, int windowHeight)
    {
        var pos = _window.AppWindow.Position;
        if (!TryGetWorkArea(pos.X + windowWidth / 2, pos.Y + windowHeight / 2, out var workArea))
            return;

        int x = pos.X;
        int y = pos.Y;
        if (x + windowWidth > workArea.X + workArea.Width)
            x = workArea.X + workArea.Width - windowWidth;
        if (x < workArea.X)
            x = workArea.X;
        if (y + windowHeight > workArea.Y + workArea.Height)
            y = workArea.Y + workArea.Height - windowHeight;
        if (y < workArea.Y)
            y = workArea.Y;

        if (x != pos.X || y != pos.Y)
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void ConfigureWindowStyle()
    {
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        // Apply DPI scaling to window dimensions
        ApplyPopupSize();
    }

    private void MakeToolWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);

            // Remove window border styles
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~WS_CAPTION;
            style &= ~WS_THICKFRAME;
            style &= ~WS_BORDER;
            SetWindowLong(hwnd, GWL_STYLE, style);

            // Make it a tool window (no taskbar entry)
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            // Set as topmost
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED);
        }
        catch { }
    }

    private void SetupAcrylicBackdrop()
    {
        try
        {
            if (DesktopAcrylicController.IsSupported())
            {
                _window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();

                _configurationSource = new SystemBackdropConfiguration();
                _configurationSource.IsInputActive = true;

                // Transition to transparent after backdrop composes
                _window.DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.High,
                    () =>
                    {
                        if (_window.Content is Panel root)
                        {
                            root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                        }
                    });
            }
        }
        catch { }
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);

            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            uint borderColor = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
        }
        catch { }
    }

    public async Task WarmUpCompositorAsync()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);

            // Move to off-screen position (far left)
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(-10000, -10000));

            // Show window to trigger compositor allocation
            _window.AppWindow.Show();

            // Wait for DWM to compose backdrop
            await Task.Delay(100);

            // Hide window (keeps compositor state alive)
            _window.AppWindow.Hide();

            Console.WriteLine("[WindowService] Compositor warm-up complete");
        }
        catch (Exception ex)
        {
            // Warm-up failure is non-fatal
            Console.WriteLine($"[WindowService] Warm-up failed: {ex.Message}");
        }
    }

    public void EnsureBackdropActive()
    {
        // Keep acrylic effect active
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive = true;
        }
    }

    public double GetDpiScale()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);
            var dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    public bool TryGetWorkArea(int screenX, int screenY, out Windows.Graphics.RectInt32 workArea)
    {
        workArea = default;
        var monitor = MonitorFromPoint(new POINT { X = screenX, Y = screenY }, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        workArea = new Windows.Graphics.RectInt32
        {
            X = info.rcWork.Left,
            Y = info.rcWork.Top,
            Width = info.rcWork.Right - info.rcWork.Left,
            Height = info.rcWork.Bottom - info.rcWork.Top,
        };
        return workArea.Width > 0 && workArea.Height > 0;
    }

    public void ShowAtCursor()
    {
        // Save the current foreground window BEFORE showing our popup
        App.Bridge?.SaveForegroundWindow();

        if (GetCursorPos(out POINT cursorPos))
        {
            IntPtr hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);

            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            var (windowWidth, windowHeight) = ApplyPopupSize();

            int x = cursorPos.X;
            int y = cursorPos.Y;

            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                RECT workArea = monitorInfo.rcWork;

                // Adjust position to keep window on screen
                if (x + windowWidth > workArea.Right)
                {
                    x = workArea.Right - windowWidth;
                }
                if (x < workArea.Left)
                {
                    x = workArea.Left;
                }

                if (y + windowHeight > workArea.Bottom)
                {
                    y = workArea.Bottom - windowHeight;
                }
                if (y < workArea.Top)
                {
                    y = workArea.Top;
                }
            }

            _window.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        _window.AppWindow.Show();

        // Set as topmost and force foreground
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd);

        _window.Activate();
    }
}
