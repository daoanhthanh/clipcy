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

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
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
            this.HidePopup();
        }
        else
        {
            ViewModel.RefreshHistory();
            SearchBox.Text = string.Empty;

            // Focus on the list and select first item for immediate keyboard navigation
            if (ViewModel.Items.Count > 0)
            {
                HistoryListView.SelectedIndex = 0;
                HistoryListView.Focus(FocusState.Programmatic);
            }
            else
            {
                SearchBox.Focus(FocusState.Programmatic);
            }
        }

        // Keep acrylic effect active even when not focused
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive = true;
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

    private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipboardItemViewModel item)
        {
            ViewModel.PasteItem(item.Id);
            this.HidePopup();
        }
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
                ViewModel.PasteItem(item.Id);
                this.HidePopup();
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
    }

    public void ShowAtCursor()
    {
        if (GetCursorPos(out POINT cursorPos))
        {
            // Get screen dimensions
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);

            int windowWidth = 400;
            int windowHeight = 500;

            // Adjust position to keep window on screen
            int x = cursorPos.X;
            int y = cursorPos.Y;

            if (x + windowWidth > screenWidth)
            {
                x = screenWidth - windowWidth;
            }
            if (y + windowHeight > screenHeight)
            {
                y = screenHeight - windowHeight;
            }

            this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }

        // Make sure it's topmost when showing
        var hwnd = WindowNative.GetWindowHandle(this);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        this.AppWindow.Show();
        this.Activate();
    }

    public void HidePopup()
    {
        this.AppWindow.Hide();
    }

    public bool IsVisible => this.AppWindow.IsVisible;
}
