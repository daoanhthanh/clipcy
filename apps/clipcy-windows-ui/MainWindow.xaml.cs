using System.Runtime.InteropServices;
using Clipcy.Services;
using Clipcy.ViewModels;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace Clipcy;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private Microsoft.UI.Composition.SystemBackdrops.SystemBackdropConfiguration? _configurationSource;

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

    public MainWindow()
    {
        this.InitializeComponent();
        ConfigureAsPopup();
        SetupAcrylicBackdrop();
        ApplyRoundedCorners();
        MakeToolWindow();

        ViewModel = new MainViewModel();
        ((FrameworkElement)this.Content).DataContext = this;

        // Register for theme management
        if (RootGrid != null)
        {
            Clipcy.Services.ThemeManager.RegisterElement(RootGrid);
            Clipcy.Services.ThemeManager.SetTheme(Clipcy.Services.ThemeManager.CurrentTheme); // Apply current global theme
        }

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
        catch { }
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
        catch { }
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
        catch { }
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
        else if (
            args.WindowActivationState == WindowActivationState.CodeActivated
            || args.WindowActivationState == WindowActivationState.PointerActivated
        )
        {
            // Use async version to avoid blocking UI thread
            _ = ViewModel.RefreshHistoryAsync();
            SearchBox.Text = string.Empty;

            // Don't set any focus - let user navigate with mouse or arrow keys
            // Force focus to RootGrid or ListView to prevent SearchBox from getting it
            HistoryListView.SelectedIndex = -1;
            RootGrid.Focus(FocusState.Programmatic);

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
    }

    private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ClipboardItemViewModel item)
        {
            PasteSelectedItem(item);
        }
    }

    private void PasteSelectedItem(ClipboardItemViewModel item)
    {
        // Hide popup first
        HidePopup();

        try
        {
            App.Bridge?.PasteItem(item.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Paste error: {ex.Message}");
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
                PasteSelectedItem(item);
                return;
            }
        }

        // Handle Up/Down arrow keys for navigation
        if (e.Key == Windows.System.VirtualKey.Down)
        {
            // If search box has focus, move to list
            if (SearchBox.FocusState != FocusState.Unfocused)
            {
                e.Handled = true;
                HistoryListView.Focus(FocusState.Programmatic);
                if (HistoryListView.Items.Count > 0)
                {
                    HistoryListView.SelectedIndex = 0;
                }
                return;
            }

            e.Handled = true;

            if (HistoryListView.SelectedIndex < 0)
            {
                // No selection, select first item
                if (ViewModel.Items.Count > 0)
                {
                    HistoryListView.SelectedIndex = 0;
                    HistoryListView.Focus(FocusState.Keyboard);
                }
            }
            else if (HistoryListView.SelectedIndex < ViewModel.Items.Count - 1)
            {
                // Move down
                HistoryListView.SelectedIndex++;
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            e.Handled = true;

            if (HistoryListView.SelectedIndex > 0)
            {
                // Move up
                HistoryListView.SelectedIndex--;
            }
            else if (HistoryListView.SelectedIndex == 0)
            {
                // At top, deselect - user can manually click search box if needed
                HistoryListView.SelectedIndex = -1;
                RootGrid.Focus(FocusState.Keyboard);
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
        App.Bridge?.SaveForegroundWindow();

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
        HidePreview();
        this.AppWindow.Hide();
    }

    public bool IsVisible => this.AppWindow.IsVisible;

    // Preview Panel Logic
    private DispatcherTimer? _previewDelayTimer;
    private DispatcherTimer? _hideTimer;
    private ClipboardItemViewModel? _pendingPreviewItem;
    private FrameworkElement? _previewTarget;
    private Microsoft.UI.Xaml.Controls.Primitives.Popup? _previewPopup;
    private FrameworkElement? _previewPopupRoot;
    private TextBlock? _previewContentType;
    private TextBlock? _previewText;
    private Image? _previewImage;
    private Border? _previewImageBorder;
    private TextBlock? _previewMetadata;
    private bool _isPointerOverPreview = false;
    private ClipboardItemViewModel? _currentPreviewItem;

    private void HistoryListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is ListViewItem item)
        {
            // Remove old handlers first to avoid duplicates
            item.PointerEntered -= ListViewItem_PointerEntered;
            item.PointerExited -= ListViewItem_PointerExited;
            item.GotFocus -= ListViewItem_GotFocus;
            item.LostFocus -= ListViewItem_LostFocus;

            // Attach new handlers
            item.PointerEntered += ListViewItem_PointerEntered;
            item.PointerExited += ListViewItem_PointerExited;
            item.GotFocus += ListViewItem_GotFocus;
            item.LostFocus += ListViewItem_LostFocus;
        }
    }

    private Microsoft.UI.Xaml.Controls.Primitives.Popup CreatePreviewPopup()
    {
        var popup = new Microsoft.UI.Xaml.Controls.Primitives.Popup
        {
            IsLightDismissEnabled = false,
            ShouldConstrainToRootBounds = false,
        };

        // Create outer border for shadow and rounded corners (Windows 11 style)
        // Use theme resource for acrylic background
        Microsoft.UI.Xaml.Media.Brush backgroundBrush;
        try
        {
            backgroundBrush = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["AcrylicBackgroundFillColorDefaultBrush"];
        }
        catch
        {
            // Fallback to LayerFillColorDefaultBrush if AcrylicBackgroundFillColorDefaultBrush doesn't exist
            try
            {
                backgroundBrush = (Microsoft.UI.Xaml.Media.Brush)
                    Application.Current.Resources["LayerFillColorDefaultBrush"];
            }
            catch
            {
                // Final fallback - create acrylic brush manually
                backgroundBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Application.Current.RequestedTheme == ApplicationTheme.Dark
                        ? Windows.UI.Color.FromArgb(242, 44, 44, 44)
                        : Windows.UI.Color.FromArgb(249, 252, 252, 252)
                );
            }
        }

        Microsoft.UI.Xaml.Media.Brush borderBrush;
        try
        {
            borderBrush = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["SurfaceStrokeColorDefaultBrush"];
        }
        catch
        {
            borderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(20, 0, 0, 0)
            );
        }

        var outerBorder = new Border
        {
            Background = backgroundBrush,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = borderBrush,
            Width = 360,
            MaxHeight = 400,
        };

        // Add shadow effect
        outerBorder.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
        outerBorder.Translation = new System.Numerics.Vector3(0, 0, 32);

        var grid = new Grid { Padding = new Thickness(12, 10, 12, 12), RowSpacing = 10 };

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Track pointer over preview panel
        grid.PointerEntered += (s, e) =>
        {
            _isPointerOverPreview = true;
            _hideTimer?.Stop();
        };

        grid.PointerExited += (s, e) =>
        {
            _isPointerOverPreview = false;
            ScheduleHidePreview();
        };

        // Header
        _previewContentType = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? Microsoft.UI.Colors.White
                    : Windows.UI.Color.FromArgb(255, 32, 32, 32)
            ),
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(_previewContentType, 0);
        grid.Children.Add(_previewContentType);

        // Content
        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 280 };

        var contentStack = new StackPanel { Spacing = 10 };

        _previewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Visibility = Visibility.Collapsed,
            LineHeight = 20,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? Windows.UI.Color.FromArgb(255, 230, 230, 230)
                    : Windows.UI.Color.FromArgb(255, 50, 50, 50)
            ),
        };
        contentStack.Children.Add(_previewText);

        _previewImage = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            MaxWidth = 336,
            MaxHeight = 260,
            Visibility = Visibility.Collapsed,
        };

        // Add rounded corners to image
        _previewImageBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Child = _previewImage,
            Visibility = Visibility.Collapsed,
        };
        contentStack.Children.Add(_previewImageBorder);

        scrollViewer.Content = contentStack;
        Grid.SetRow(scrollViewer, 1);
        grid.Children.Add(scrollViewer);

        // Metadata
        var metadataStack = new StackPanel { Spacing = 4 };
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
                    : Windows.UI.Color.FromArgb(40, 0, 0, 0)
            ),
        };
        metadataStack.Children.Add(divider);

        _previewMetadata = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? Windows.UI.Color.FromArgb(255, 160, 160, 160)
                    : Windows.UI.Color.FromArgb(255, 100, 100, 100)
            ),
            LineHeight = 16,
        };
        metadataStack.Children.Add(_previewMetadata);

        Grid.SetRow(metadataStack, 2);
        grid.Children.Add(metadataStack);

        outerBorder.Child = grid;
        popup.Child = outerBorder;
        _previewPopupRoot = outerBorder;

        // Store the outerBorder reference for animations
        popup.Opened += (s, e) =>
        {
            outerBorder.Opacity = 0;
            outerBorder.Translation = new System.Numerics.Vector3(-10, 0, 32);

            var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
                },
            };

            var slideIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = -10,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
                },
            };

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, outerBorder);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);

            storyboard.Begin();

            // Manually animate translation
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            var elapsed = 0.0;
            timer.Tick += (s2, e2) =>
            {
                elapsed += 16;
                var progress = Math.Min(1.0, elapsed / 150.0);
                var eased = 1 - Math.Pow(1 - progress, 2); // Quadratic ease out
                outerBorder.Translation = new System.Numerics.Vector3((float)(-10 * (1 - eased)), 0, 32);

                if (progress >= 1.0)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        };

        return popup;
    }

    private Microsoft.UI.Xaml.Controls.Primitives.Popup PreviewPopup
    {
        get
        {
            if (_previewPopup == null)
            {
                _previewPopup = CreatePreviewPopup();
            }
            return _previewPopup;
        }
    }

    private void ListViewItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // If SearchBox has focus, clear it by focusing the item or list
        if (SearchBox.FocusState != FocusState.Unfocused)
        {
            // Focus the list view itself or the item to blur search box
            if (sender is ListViewItem item)
            {
                item.Focus(FocusState.Pointer);
            }
        }

        if (sender is ListViewItem li && li.Content is ClipboardItemViewModel viewModel)
        {
            // Schedule preview with delay (same as keyboard navigation)
            ScheduleShowPreview(viewModel, li);
        }
    }

    private void ListViewItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Cancel pending preview
        _pendingPreviewItem = null;
        _previewDelayTimer?.Stop();

        // Schedule hide with delay
        ScheduleHidePreview();
    }

    private void ScheduleShowPreview(ClipboardItemViewModel viewModel, FrameworkElement target)
    {
        // Stop any pending hide timer immediately
        _hideTimer?.Stop();

        // If already showing the same item, do nothing
        if (_currentPreviewItem == viewModel && _previewPopup != null && _previewPopup.IsOpen == true)
            return;

        _pendingPreviewItem = viewModel;
        _previewTarget = target;

        // Delay preview by 300ms
        if (_previewDelayTimer == null)
        {
            _previewDelayTimer = new DispatcherTimer();
            _previewDelayTimer.Interval = TimeSpan.FromMilliseconds(300);
            _previewDelayTimer.Tick += PreviewDelayTimer_Tick;
        }

        _previewDelayTimer.Stop();
        _previewDelayTimer.Start();
    }

    private void ScheduleHidePreview()
    {
        // Create hide timer if needed
        if (_hideTimer == null)
        {
            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(150);
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer?.Stop();
                if (!_isPointerOverPreview)
                {
                    HidePreview();
                }
            };
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void ListViewItem_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewItem item && item.Content is ClipboardItemViewModel viewModel)
        {
            // Treat focus same as hover - schedule preview with delay
            ScheduleShowPreview(viewModel, item);
        }
    }

    private void ListViewItem_LostFocus(object sender, RoutedEventArgs e)
    {
        // Cancel pending and schedule hide
        _previewDelayTimer?.Stop();
        ScheduleHidePreview();
    }

    private void PreviewDelayTimer_Tick(object? sender, object e)
    {
        _previewDelayTimer?.Stop();
        if (_pendingPreviewItem != null)
        {
            ShowPreview(_pendingPreviewItem);
        }
    }

    private async void ShowPreview(ClipboardItemViewModel item)
    {
        // Prevent showing if already visible for the same item
        if (_currentPreviewItem == item && PreviewPopup.IsOpen == true)
            return;

        try
        {
            // Stop all timers
            _previewDelayTimer?.Stop();
            _hideTimer?.Stop();

            // Ensure popup is created (this initializes all UI elements)
            var popup = PreviewPopup;

            if (
                _previewContentType == null
                || _previewText == null
                || _previewImage == null
                || _previewImageBorder == null
                || _previewMetadata == null
            )
                return;

            // Set current preview item
            _currentPreviewItem = item;

            // Set content type header
            _previewContentType.Text = item.ContentType;

            // Reset visibility
            _previewText.Visibility = Visibility.Collapsed;
            _previewImageBorder.Visibility = Visibility.Collapsed;

            // Show preview based on content type
            switch (item.ContentType)
            {
                case "Text":
                case "RichText":
                case "Html":
                    // Get full text from bridge if available
                    var fullText = item.Preview;
                    if (App.Bridge != null && !string.IsNullOrEmpty(item.Id))
                    {
                        var data = App.Bridge.GetItemData(item.Id);
                        if (data != null && data.Length > 0)
                        {
                            fullText = System.Text.Encoding.UTF8.GetString(data);
                        }
                    }
                    _previewText.Text = fullText;
                    _previewText.Visibility = Visibility.Visible;
                    break;

                case "Image":
                    byte[]? imageData = item.Thumbnail;

                    // Try to get full image data
                    if (App.Bridge != null && !string.IsNullOrEmpty(item.Id))
                    {
                        var fullData = await Task.Run(() => App.Bridge.GetItemData(item.Id));
                        if (fullData != null && fullData.Length > 0)
                        {
                            imageData = fullData;
                        }
                    }

                    if (imageData != null && imageData.Length > 0)
                    {
                        using (var stream = new MemoryStream(imageData))
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                            bitmap.SetSource(stream.AsRandomAccessStream());
                            _previewImage.Source = bitmap;
                            _previewImage.Visibility = Visibility.Visible;
                            _previewImageBorder.Visibility = Visibility.Visible;
                        }
                    }
                    break;

                case "Files":
                    _previewText.Text = item.Preview;
                    _previewText.Visibility = Visibility.Visible;
                    break;
            }

            // Build metadata string
            var metadata = new System.Text.StringBuilder();

            // Created time
            metadata.AppendLine($"Copied: {item.RelativeTime}");

            // Content-specific metadata
            switch (item.ContentType)
            {
                case "Text":
                case "RichText":
                case "Html":
                    var charCount = _previewText.Text.Length;
                    var wordCount = _previewText
                        .Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Length;
                    var lineCount = _previewText.Text.Split('\n').Length;
                    metadata.AppendLine($"Characters: {charCount:N0}");
                    metadata.AppendLine($"Words: {wordCount:N0}");
                    if (lineCount > 1)
                        metadata.AppendLine($"Lines: {lineCount:N0}");
                    break;

                case "Image":
                    if (_previewImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bmp)
                    {
                        metadata.AppendLine($"Dimensions: {bmp.PixelWidth} x {bmp.PixelHeight}");
                    }
                    if (item.Thumbnail != null)
                    {
                        var sizeKB = item.Thumbnail.Length / 1024.0;
                        metadata.AppendLine($"Size: {sizeKB:N1} KB");
                    }
                    break;

                case "Files":
                    var files = item.Preview.Split('\n').Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
                    metadata.AppendLine($"Files: {files.Length}");
                    break;
            }

            if (!string.IsNullOrEmpty(item.SourceApp))
            {
                metadata.AppendLine($"Source: {item.SourceApp}");
            }

            if (item.Pinned)
            {
                metadata.AppendLine("Pinned");
            }

            _previewMetadata.Text = metadata.ToString().TrimEnd();

            // Position and show the popup
            if (_previewTarget != null)
            {
                // Set XamlRoot for the popup (required in WinUI 3)
                popup.XamlRoot = _previewTarget.XamlRoot;

                // Get the position of the target element relative to window
                var transform = _previewTarget.TransformToVisual(null);
                var targetPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                // Force measure to get accurate size
                if (_previewPopupRoot != null)
                {
                    _previewPopupRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                }
                var popupWidth = _previewPopupRoot?.DesiredSize.Width ?? 360.0;
                var popupHeight = _previewPopupRoot?.DesiredSize.Height ?? 260.0;
                var spacing = 10.0;

                // Get screen info
                var windowPos = this.AppWindow.Position;
                var windowSize = this.AppWindow.Size;
                var scale = popup.XamlRoot.RasterizationScale;

                var centerPt = new POINT
                {
                    X = windowPos.X + (windowSize.Width / 2),
                    Y = windowPos.Y + (windowSize.Height / 2),
                };

                var hMonitor = MonitorFromPoint(centerPt, MONITOR_DEFAULTTONEAREST);
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                bool showOnLeft = false;
                double verticalAdjust = 0;

                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    var workArea = monitorInfo.rcWork;

                    // Check Horizontal
                    // Calculate right edge of popup if placed on right: (TargetX + Width + Spacing + PopupWidth) * Scale + WindowX
                    var rightEdgeDips = targetPos.X + _previewTarget.ActualWidth + spacing + popupWidth;
                    var rightEdgePixels = windowPos.X + (int)(rightEdgeDips * scale);

                    if (rightEdgePixels > workArea.Right)
                    {
                        showOnLeft = true;
                    }

                    // Check Vertical
                    // Calculate bottom edge of popup: (TargetY + PopupHeight) * Scale + WindowY
                    var bottomEdgeDips = targetPos.Y + popupHeight;
                    var bottomEdgePixels = windowPos.Y + (int)(bottomEdgeDips * scale);

                    if (bottomEdgePixels > workArea.Bottom)
                    {
                        var excessPixels = bottomEdgePixels - workArea.Bottom;
                        // Move up by excess + spacing to ensure it's fully visible and not flush against the edge
                        verticalAdjust = -(excessPixels / scale) - spacing;
                    }
                }

                // Calculate offset
                double horizontalOffset;
                if (showOnLeft)
                {
                    // Position to the left: TargetX - PopupWidth - Spacing
                    horizontalOffset = targetPos.X - popupWidth - spacing;
                }
                else
                {
                    // Position to the right: TargetX + TargetWidth + Spacing
                    horizontalOffset = targetPos.X + _previewTarget.ActualWidth + spacing;
                }

                double verticalOffset = targetPos.Y + verticalAdjust;

                popup.HorizontalOffset = horizontalOffset;
                popup.VerticalOffset = verticalOffset;

                popup.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShowPreview error: {ex.Message}");
            HidePreview();
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HidePreview();
    }

    private void HidePreview()
    {
        _previewDelayTimer?.Stop();
        _hideTimer?.Stop();
        if (_previewPopup != null)
        {
            _previewPopup.IsOpen = false;
        }
        _currentPreviewItem = null;
    }
}
