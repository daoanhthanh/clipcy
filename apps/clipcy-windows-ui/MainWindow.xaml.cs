using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Clipcy.ViewModels;
using Clipcy.Services;
using Windows.Foundation;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace Clipcy;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

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
        }

        this.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 400, Height = 500 });
    }

    private void SetupAcrylicBackdrop()
    {
        try
        {
            if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
            {
                this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
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
            SearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.Search(SearchBox.Text);
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
        }
    }

    public void ShowAtCursor()
    {
        if (GetCursorPos(out POINT cursorPos))
        {
            this.AppWindow.Move(new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y));
        }

        this.AppWindow.Show();
        this.Activate();
    }

    public void HidePopup()
    {
        this.AppWindow.Hide();
    }

    public bool IsVisible => this.AppWindow.IsVisible;
}
