using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition.SystemBackdrops;
using Clipcy.ViewModels;
using Clipcy.Services;
using Windows.Foundation;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Clipcy;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration? _configurationSource;
    private IntPtr _previousForegroundWindow = IntPtr.Zero;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    // Window style constants
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_BORDER = 0x00800000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    // SetWindowPos constants
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    // System metrics
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    // Monitor constants
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // Input constants for SendInput
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint CLIPCY_EXTRA_INFO = 0xFFC3D44F; // AutoHotkey compatibility marker
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
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

    public MainWindow()
    {
        this.InitializeComponent();
        ConfigureAsPopup();
        SetupAcrylicBackdrop();
        ApplyRoundedCorners();
        MakeToolWindow();

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;

        this.Activated += MainWindow_Activated;
        ViewModel.RefreshHistory();
    }

    private void ConfigureAsPopup()
    {
        var presenter = this.AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        this.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 400, Height = 500 });
    }

    private void MakeToolWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);

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
        catch
        {
        }
    }

    private void SetupAcrylicBackdrop()
    {
        try
        {
            if (DesktopAcrylicController.IsSupported())
            {
                // Use the simple approach which works well for popups
                this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();

                // Setup configuration for keeping backdrop active
                _configurationSource = new Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration();
                _configurationSource.IsInputActive = true;
            }
        }
        catch
        {
        }
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);

            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            uint borderColor = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
        }
        catch
        {
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // Only hide if the window was previously visible
            // This prevents hiding immediately after showing
            if (this.AppWindow.IsVisible)
            {
                this.HidePopup();
            }
        }
        else if (args.WindowActivationState == WindowActivationState.CodeActivated ||
                 args.WindowActivationState == WindowActivationState.PointerActivated)
        {
            ViewModel.RefreshHistory();
            SearchBox.Text = string.Empty;

            // Focus on the list and select first item for immediate keyboard navigation
            // Users can start typing to auto-focus search or use arrow keys to navigate
            if (ViewModel.Items.Count > 0)
            {
                HistoryListView.SelectedIndex = 0;
                HistoryListView.Focus(FocusState.Programmatic);
            }
            else
            {
                // If no items, focus search box
                SearchBox.Focus(FocusState.Programmatic);
            }

            // Keep acrylic effect active
            if (_configurationSource != null)
            {
                _configurationSource.IsInputActive = true;
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Search(SearchBox.Text);

        // After search, select first item if available
        if (ViewModel.Items.Count > 0)
        {
            HistoryListView.SelectedIndex = 0;
        }
    }

    private async void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipboardItemViewModel item)
        {
            PasteSelectedItem(item);
        }
    }

    private async void PasteSelectedItem(ClipboardItemViewModel item)
    {
        // Hide popup first
        this.HidePopup();

        // Restore focus to the previous window
        if (_previousForegroundWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_previousForegroundWindow);

            // Wait for focus to complete
            await Task.Delay(100);

            // Get item data and set to clipboard
            try
            {
                // Get the item content as text (assuming text for now)
                var itemText = item.Preview; // or get from Bridge if needed

                // Set to Windows clipboard directly
                SetTextToClipboard(itemText);

                // Wait for clipboard to be set
                await Task.Delay(50);

                // Simulate Ctrl+V with AutoHotkey compatibility marker
                SimulateCtrlV();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
            }
        }
    }

    private void SetTextToClipboard(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                System.Diagnostics.Debug.WriteLine("Failed to open clipboard");
                return;
            }

            EmptyClipboard();

            // Convert text to Unicode and allocate global memory
            var bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);

            if (hGlobal == IntPtr.Zero)
            {
                CloseClipboard();
                return;
            }

            var ptr = GlobalLock(hGlobal);
            if (ptr != IntPtr.Zero)
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                GlobalUnlock(hGlobal);
                SetClipboardData(CF_UNICODETEXT, hGlobal);
            }

            CloseClipboard();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetTextToClipboard error: {ex.Message}");
        }
    }

    private void SimulateCtrlV()
    {
        var inputs = new INPUT[4];

        // Ctrl down
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT
        {
            wVk = VK_CONTROL,
            wScan = 0x1D,
            dwFlags = 0,
            time = 0,
            dwExtraInfo = (IntPtr)CLIPCY_EXTRA_INFO
        };

        // V down
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki = new KEYBDINPUT
        {
            wVk = VK_V,
            wScan = 0x2F,
            dwFlags = 0,
            time = 0,
            dwExtraInfo = (IntPtr)CLIPCY_EXTRA_INFO
        };

        // V up
        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].U.ki = new KEYBDINPUT
        {
            wVk = VK_V,
            wScan = 0x2F,
            dwFlags = KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = (IntPtr)CLIPCY_EXTRA_INFO
        };

        // Ctrl up
        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].U.ki = new KEYBDINPUT
        {
            wVk = VK_CONTROL,
            wScan = 0x1D,
            dwFlags = KEYEVENTF_KEYUP,
            time = 0,
            dwExtraInfo = (IntPtr)CLIPCY_EXTRA_INFO
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Activate();
        this.HidePopup();
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            this.HidePopup();
            return;
        }

        // Handle Enter to paste selected item
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (HistoryListView.SelectedItem is ClipboardItemViewModel item)
            {
                e.Handled = true;
                PasteSelectedItem(item);
                return;
            }
        }

        // Handle Up/Down arrow keys for navigation
        if (e.Key == Windows.System.VirtualKey.Down)
        {
            if (SearchBox.FocusState != FocusState.Unfocused && ViewModel.Items.Count > 0)
            {
                e.Handled = true;
                HistoryListView.SelectedIndex = 0;
                HistoryListView.Focus(FocusState.Programmatic);
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            if (HistoryListView.SelectedIndex == 0)
            {
                e.Handled = true;
                SearchBox.Focus(FocusState.Programmatic);
            }
        }
        // Auto-focus search box when user types alphanumeric characters
        else if (SearchBox.FocusState == FocusState.Unfocused)
        {
            // Check if it's a character key (letter, number, space, or symbol)
            if (IsCharacterKey(e.Key))
            {
                // Focus the search box - the character will be typed there
                SearchBox.Focus(FocusState.Programmatic);
                // Don't mark as handled so the character propagates to the search box
            }
        }
    }

    private bool IsCharacterKey(Windows.System.VirtualKey key)
    {
        // Letters (A-Z)
        if (key >= Windows.System.VirtualKey.A && key <= Windows.System.VirtualKey.Z)
            return true;

        // Numbers (0-9)
        if (key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Number9)
            return true;

        // Numpad numbers
        if (key >= Windows.System.VirtualKey.NumberPad0 && key <= Windows.System.VirtualKey.NumberPad9)
            return true;

        // Space
        if (key == Windows.System.VirtualKey.Space)
            return true;

        // Common punctuation and symbols that might be typed
        switch (key)
        {
            case Windows.System.VirtualKey.Subtract:
            case Windows.System.VirtualKey.Add:
            case (Windows.System.VirtualKey)186: // Semicolon
            case (Windows.System.VirtualKey)187: // Equals
            case (Windows.System.VirtualKey)188: // Comma
            case (Windows.System.VirtualKey)189: // Minus
            case (Windows.System.VirtualKey)190: // Period
            case (Windows.System.VirtualKey)191: // Slash
            case (Windows.System.VirtualKey)192: // Backtick
            case (Windows.System.VirtualKey)219: // Open bracket
            case (Windows.System.VirtualKey)220: // Backslash
            case (Windows.System.VirtualKey)221: // Close bracket
            case (Windows.System.VirtualKey)222: // Quote
                return true;
        }

        return false;
    }

    public void ShowAtCursor()
    {
        // Save the current foreground window BEFORE showing our popup
        _previousForegroundWindow = GetForegroundWindow();

        if (GetCursorPos(out POINT cursorPos))
        {
            // Get the monitor where the cursor is
            IntPtr hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);

            MONITORINFO monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

            int windowWidth = 400;
            int windowHeight = 500;

            int x = cursorPos.X;
            int y = cursorPos.Y;

            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                // Use work area (excludes taskbar)
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

            this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        // Get window handle
        var hwnd = WindowNative.GetWindowHandle(this);

        // Show and activate the window
        this.AppWindow.Show();

        // Set as topmost and force foreground
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        SetForegroundWindow(hwnd);

        // Activate to ensure focus
        this.Activate();
    }

    public void HidePopup()
    {
        this.AppWindow.Hide();
    }

    public bool IsVisible => this.AppWindow.IsVisible;
}
