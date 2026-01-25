using Clipcy.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipcy;

/// <summary>
/// Settings window for configuring Clipcy
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private SettingsViewModel _settings;
    private double _lastValidMaxHistory;
    private SystemBackdropConfiguration? _configurationSource;
    private bool _isSidebarExpanded = true;
    private DispatcherTimer? _saveWindowSizeTimer;
    private bool _isRestoringSize = false;

    // Responsive thresholds
    private const int MinWindowWidth = 500;
    private const int MinWindowHeight = 400;
    private const int SidebarCollapseThreshold = 720;

    public SettingsWindow()
    {
        InitializeComponent();

        SetupAcrylicBackdrop();
        SetupTitleBar();
        SetupWindowSizeTracking();
        this.Activated += SettingsWindow_Activated;
        this.Closed += SettingsWindow_Closed;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Restore window size or use default
        RestoreWindowSize();

        // Set minimum size using presenter
        var presenter = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = true;
            presenter.IsMinimizable = true;
            presenter.IsMaximizable = true;
        }

        // Load current settings
        _settings = App.Bridge?.GetSettings() ?? new SettingsViewModel();
        LoadSettingsToUI();

        // Subscribe to theme changes and apply initial theme
        Services.ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyTheme(Services.ThemeManager.CurrentTheme);

        // Update title bar colors after content is loaded
        if (Content is FrameworkElement rootElement)
        {
            rootElement.Loaded += (s, e) => UpdateTitleBarColors();
        }
    }

    private void SetupTitleBar()
    {
        try
        {
            // Extend content into title bar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            UpdateTitleBarColors();
        }
        catch { }
    }

    private void UpdateTitleBarColors()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[TIMING] SettingsWindow.UpdateTitleBarColors started at {DateTime.Now:HH:mm:ss.fff}");
        try
        {
            if (AppWindow == null || AppWindow.TitleBar == null)
                return;

            var titleBar = AppWindow.TitleBar;

            // Use centralized ThemeManager for consistent, fast dark mode detection
            bool isDarkMode = Services.ThemeManager.IsDarkTheme();

            // Set button foreground colors based on theme
            // ALWAYS white in dark mode, ALWAYS black in light mode
            var foregroundColor = isDarkMode ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            var inactiveForegroundColor = isDarkMode
                ? Windows.UI.Color.FromArgb(255, 180, 180, 180)
                : Windows.UI.Color.FromArgb(255, 100, 100, 100);
            var hoverBgColor = isDarkMode
                ? Windows.UI.Color.FromArgb(20, 255, 255, 255)
                : Windows.UI.Color.FromArgb(20, 0, 0, 0);
            var pressedBgColor = isDarkMode
                ? Windows.UI.Color.FromArgb(30, 255, 255, 255)
                : Windows.UI.Color.FromArgb(30, 0, 0, 0);

            // Button colors
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            // Button foreground (icon) colors
            titleBar.ButtonForegroundColor = foregroundColor;
            titleBar.ButtonInactiveForegroundColor = inactiveForegroundColor;

            // Hover colors
            titleBar.ButtonHoverBackgroundColor = hoverBgColor;
            titleBar.ButtonHoverForegroundColor = foregroundColor;

            // Pressed colors
            titleBar.ButtonPressedBackgroundColor = pressedBgColor;
            titleBar.ButtonPressedForegroundColor = foregroundColor;
        }
        catch { }
        Console.WriteLine($"[TIMING] SettingsWindow.UpdateTitleBarColors finished at {sw.ElapsedMilliseconds}ms");
    }

    private void SetupAcrylicBackdrop()
    {
        try
        {
            if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                _configurationSource = new SystemBackdropConfiguration { IsInputActive = true };
            }
        }
        catch { }
    }

    private void SettingsWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private void OnThemeChanged(Services.AppTheme theme)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[TIMING] SettingsWindow.OnThemeChanged started at {DateTime.Now:HH:mm:ss.fff}");
        if (this.Content is FrameworkElement content)
        {
            content.DispatcherQueue.TryEnqueue(() =>
            {
                Console.WriteLine($"[TIMING] SettingsWindow dispatched to UI thread after {sw.ElapsedMilliseconds}ms");
                ApplyTheme(theme);
                UpdateTitleBarColors();
            });
        }
        Console.WriteLine($"[TIMING] SettingsWindow.OnThemeChanged finished at {sw.ElapsedMilliseconds}ms");
    }

    private void ApplyTheme(Services.AppTheme theme)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"[TIMING] SettingsWindow.ApplyTheme started at {DateTime.Now:HH:mm:ss.fff}");
        if (this.Content is FrameworkElement content)
        {
            content.RequestedTheme = Services.ThemeManager.GetElementTheme(theme);
        }
        Console.WriteLine($"[TIMING] SettingsWindow.ApplyTheme finished at {sw.ElapsedMilliseconds}ms");
    }

    private void SetupWindowSizeTracking()
    {
        // Initialize debounce timer for saving window size
        _saveWindowSizeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500), // Save 500ms after user stops resizing
        };
        _saveWindowSizeTimer.Tick += SaveWindowSizeTimer_Tick;

        // Subscribe to AppWindow.Changed event to track size changes
        if (AppWindow != null)
        {
            AppWindow.Changed += AppWindow_Changed;
        }
    }

    private void AppWindow_Changed(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowChangedEventArgs args
    )
    {
        // Track size changes
        if (args.DidSizeChange && !_isRestoringSize)
        {
            System.Diagnostics.Debug.WriteLine($"Window size changed: {sender.Size.Width}x{sender.Size.Height}");

            // Restart debounce timer
            if (_saveWindowSizeTimer != null)
            {
                _saveWindowSizeTimer.Stop();
                _saveWindowSizeTimer.Start();
            }
        }
    }

    private void RestoreWindowSize()
    {
        try
        {
            _isRestoringSize = true;
            System.Diagnostics.Debug.WriteLine("Attempting to restore window size...");

            var windowSizeFile = GetWindowSizeFilePath();
            if (File.Exists(windowSizeFile))
            {
                var lines = File.ReadAllLines(windowSizeFile);
                if (
                    lines.Length >= 2
                    && int.TryParse(lines[0], out var width)
                    && int.TryParse(lines[1], out var height)
                )
                {
                    System.Diagnostics.Debug.WriteLine($"Found saved size: {width}x{height}");
                    var w = Math.Max(width, MinWindowWidth);
                    var h = Math.Max(height, MinWindowHeight);

                    AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = w, Height = h });
                    System.Diagnostics.Debug.WriteLine($"Restored window size to: {w}x{h}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Invalid window size file format");
                    SetDefaultWindowSize();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"No saved size found at {windowSizeFile}, using default: 1400x720");
                SetDefaultWindowSize();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restore window size: {ex.Message}");
            SetDefaultWindowSize();
        }
        finally
        {
            _isRestoringSize = false;
        }
    }

    private void SetDefaultWindowSize()
    {
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1400, Height = 720 });
        }
        catch { }
    }

    private static string GetWindowSizeFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var clipcyDir = Path.Combine(localAppData, "clipcy");
        Directory.CreateDirectory(clipcyDir); // Ensure directory exists
        return Path.Combine(clipcyDir, "window-size.txt");
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        // Unsubscribe from theme changes
        Services.ThemeManager.ThemeChanged -= OnThemeChanged;

        // Unsubscribe from AppWindow.Changed
        if (AppWindow != null)
        {
            AppWindow.Changed -= AppWindow_Changed;
        }

        // Clean up timer
        if (_saveWindowSizeTimer != null)
        {
            _saveWindowSizeTimer.Stop();
            _saveWindowSizeTimer.Tick -= SaveWindowSizeTimer_Tick;
            _saveWindowSizeTimer = null;
        }

        // Final save of window size
        SaveWindowSize();
    }

    private void SaveWindowSizeTimer_Tick(object? sender, object e)
    {
        _saveWindowSizeTimer?.Stop();
        SaveWindowSize();
    }

    private void SaveWindowSize()
    {
        if (_isRestoringSize || AppWindow == null)
            return;

        try
        {
            var width = AppWindow.Size.Width;
            var height = AppWindow.Size.Height;

            // Only save if size is valid
            if (width >= MinWindowWidth && height >= MinWindowHeight)
            {
                var windowSizeFile = GetWindowSizeFilePath();
                File.WriteAllLines(windowSizeFile, new[] { width.ToString(), height.ToString() });
                System.Diagnostics.Debug.WriteLine($"Saved window size to {windowSizeFile}: {width}x{height}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save window size: {ex.Message}");
        }
    }

    private void LoadSettingsToUI()
    {
        // Skip if controls aren't loaded yet
        if (MaxHistoryBox == null || ThemeComboBox == null)
            return;

        MaxHistoryBox.Value = _settings.MaxHistoryItems;
        _lastValidMaxHistory = _settings.MaxHistoryItems;

        UpdateHotkeyDisplay(_settings.Hotkey);

        StartOnBootToggle.IsOn = _settings.StartOnBoot;
        ShowPreviewToggle.IsOn = _settings.ShowPreviewPanel;
        ShowNotificationsToggle.IsOn = _settings.ShowNotifications;

        // Set theme combo box
        var themeIndex = _settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0, // System
        };
        ThemeComboBox.SelectedIndex = themeIndex;
    }

    private void UpdateHotkeyDisplay(string hotkey)
    {
        // Skip if controls aren't loaded yet
        if (WinKeyDisplayButton == null || MainKeyDisplayButton == null)
            return;

        WinKeyDisplayButton.Visibility = Visibility.Collapsed;
        CtrlKeyDisplayButton.Visibility = Visibility.Collapsed;
        AltKeyDisplayButton.Visibility = Visibility.Collapsed;
        ShiftKeyDisplayButton.Visibility = Visibility.Collapsed;
        MainKeyDisplayButton.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(hotkey))
            return;

        var parts = hotkey.Split('+');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed)
            {
                case "Win":
                case "Windows":
                    WinKeyDisplayButton.Visibility = Visibility.Visible;
                    break;
                case "Ctrl":
                case "Control":
                    CtrlKeyDisplayButton.Visibility = Visibility.Visible;
                    break;
                case "Alt":
                    AltKeyDisplayButton.Visibility = Visibility.Visible;
                    break;
                case "Shift":
                    ShiftKeyDisplayButton.Visibility = Visibility.Visible;
                    break;
                default:
                    MainKeyDisplayText.Text = trimmed;
                    MainKeyDisplayButton.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private void NavListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Skip if pages aren't loaded yet
        if (GeneralPage == null || BehaviorPage == null || AppearancePage == null || AboutPage == null)
            return;

        if (NavListView.SelectedItem is ListViewItem item && item.Tag is string tag)
        {
            // Hide all pages
            GeneralPage.Visibility = Visibility.Collapsed;
            BehaviorPage.Visibility = Visibility.Collapsed;
            AppearancePage.Visibility = Visibility.Collapsed;
            AboutPage.Visibility = Visibility.Collapsed;

            // Show selected page
            switch (tag)
            {
                case "General":
                    GeneralPage.Visibility = Visibility.Visible;
                    break;
                case "Behavior":
                    BehaviorPage.Visibility = Visibility.Visible;
                    break;
                case "Appearance":
                    AppearancePage.Visibility = Visibility.Visible;
                    break;
                case "About":
                    AboutPage.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Skip if UI elements aren't loaded yet
        if (SidebarPanel == null || HamburgerButton == null || SidebarSpacer == null)
            return;

        var width = e.NewSize.Width;

        // Enforce minimum size by resizing window if needed
        if (AppWindow != null)
        {
            var currentSize = AppWindow.Size;
            var needsResize = false;
            var newWidth = currentSize.Width;
            var newHeight = currentSize.Height;

            if (currentSize.Width < MinWindowWidth)
            {
                newWidth = MinWindowWidth;
                needsResize = true;
            }
            if (currentSize.Height < MinWindowHeight)
            {
                newHeight = MinWindowHeight;
                needsResize = true;
            }

            if (needsResize)
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = newWidth, Height = newHeight });
                return;
            }
        }

        // Toggle sidebar visibility based on width
        if (width < SidebarCollapseThreshold && _isSidebarExpanded)
        {
            CollapseSidebar();
        }
        else if (width >= SidebarCollapseThreshold && !_isSidebarExpanded)
        {
            ExpandSidebar();
        }
    }

    private void CollapseSidebar()
    {
        _isSidebarExpanded = false;
        SidebarPanel.Visibility = Visibility.Collapsed;
        SidebarSpacer.Visibility = Visibility.Collapsed;
        SidebarOverlayBackdrop.Visibility = Visibility.Collapsed;
        HamburgerButton.Visibility = Visibility.Visible;

        // Set solid background for overlay mode
        SidebarPanel.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        // Make content span full width
        Grid.SetColumn(ContentPanel, 0);
        Grid.SetColumnSpan(ContentPanel, 2);
    }

    private void ExpandSidebar()
    {
        _isSidebarExpanded = true;
        SidebarPanel.Visibility = Visibility.Visible;
        SidebarSpacer.Visibility = Visibility.Visible;
        SidebarOverlayBackdrop.Visibility = Visibility.Collapsed;
        HamburgerButton.Visibility = Visibility.Collapsed;

        // Remove background for inline mode
        SidebarPanel.Background = null;

        // Restore content to right column
        Grid.SetColumn(ContentPanel, 1);
        Grid.SetColumnSpan(ContentPanel, 1);
    }

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle sidebar overlay in compact mode
        if (SidebarPanel.Visibility == Visibility.Collapsed)
        {
            // Show sidebar as overlay
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarOverlayBackdrop.Visibility = Visibility.Visible;
        }
        else
        {
            // Hide sidebar overlay
            SidebarPanel.Visibility = Visibility.Collapsed;
            SidebarOverlayBackdrop.Visibility = Visibility.Collapsed;
        }
    }

    private void SidebarOverlayBackdrop_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // Close sidebar when tapping outside
        if (!_isSidebarExpanded)
        {
            SidebarPanel.Visibility = Visibility.Collapsed;
            SidebarOverlayBackdrop.Visibility = Visibility.Collapsed;
        }
    }

    private async void EditHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutPickerDialog(_settings.Hotkey) { XamlRoot = this.Content.XamlRoot };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _settings.Hotkey = dialog.Hotkey;
            UpdateHotkeyDisplay(dialog.Hotkey);
            SaveSettings();
        }
    }

    private void MaxHistoryBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            _settings.MaxHistoryItems = (uint)args.NewValue;
            _lastValidMaxHistory = args.NewValue;
            SaveSettings();
        }
    }

    private void MaxHistoryBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(MaxHistoryBox.Value))
        {
            MaxHistoryBox.Value = _lastValidMaxHistory;
        }
    }

    private void StartOnBootToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.StartOnBoot = StartOnBootToggle.IsOn;
        SaveSettings();
    }

    private void ShowPreviewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.ShowPreviewPanel = ShowPreviewToggle.IsOn;
        SaveSettings();
    }

    private void ShowNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.ShowNotifications = ShowNotificationsToggle.IsOn;
        SaveSettings();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            var themeTag = item.Tag?.ToString() ?? "System";
            _settings.Theme = themeTag;
            SaveSettings();

            // Apply theme globally using ThemeManager
            Services.ThemeManager.SetTheme(Services.ThemeManager.ParseTheme(themeTag));
        }
    }

    private void SaveSettings()
    {
        try
        {
            App.Bridge?.SaveSettings(_settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = new SettingsViewModel();
        LoadSettingsToUI();
        SaveSettings();
        // Reset theme to default (system)
        Services.ThemeManager.SetTheme(Services.ThemeManager.ParseTheme(_settings.Theme));
    }
}
