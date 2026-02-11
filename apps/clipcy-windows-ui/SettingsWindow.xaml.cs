using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipcy.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Clipcy;

/// <summary>
/// Settings window for configuring Clipcy
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    private bool _isShowingAlreadyOpenCue;
    private SettingsViewModel _settings;
    private double _lastValidMaxHistory;
    private SystemBackdropConfiguration? _configurationSource;
    private bool _isSidebarExpanded = true;
    private DispatcherTimer? _saveWindowSizeTimer;
    private Microsoft.UI.Windowing.AppWindowTitleBar? _titleBar;
    private bool _isRestoringSize = false;
    private static readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
    private static readonly System.Threading.SemaphoreSlim _updateStateLock = new(1, 1);

    private const string GitHubRepo = "daoanhthanh/clipcy-public";
    private const string GitHubReleasePageUrl = "https://github.com/" + GitHubRepo + "/releases";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
    private static readonly HttpClient _updateHttpClient = CreateUpdateHttpClient();

    private UpdateState _updateState = new();
    private bool _isCheckingUpdates;
    private bool _isInitializingUpdateControls;

    // Responsive thresholds
    private const int MinWindowWidth = 500;
    private const int MinWindowHeight = 400;
    private const int SidebarCollapseThreshold = 720;
    private const double ExcludedAppsTextBoxMinHeight = 120;
    private const double ExcludedAppsTextBoxLineHeight = 28;
    private const double ExcludedAppsTextBoxVerticalPadding = 24;

    private readonly ObservableCollection<string> _hotkeyParts = new();
    public ObservableCollection<string> HotkeyParts => _hotkeyParts;
    public string AppVersion { get; private set; } = "Version";

    public SettingsWindow()
    {
        AppVersion = GetBridgeAppVersion();
        InitializeComponent();
        RootGrid.DataContext = this;

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
        InitializeUpdateState();

        // Subscribe to theme changes and apply initial theme
        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyTheme(ThemeManager.CurrentTheme);

        // Update title bar colors after content is loaded
        if (Content is FrameworkElement rootElement)
        {
            rootElement.Loaded += (s, e) => UpdateTitleBarColors();
        }
    }

    public static void ShowOrActivate()
    {
        if (_instance == null)
        {
            _instance = new SettingsWindow();
            _instance.Activate();
            return;
        }

        _instance.Activate();
        _instance.TriggerAlreadyOpenVisualCue();
    }

    private void TriggerAlreadyOpenVisualCue()
    {
        if (_isShowingAlreadyOpenCue || AppTitleBar == null)
            return;

        _isShowingAlreadyOpenCue = true;
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            var originalOpacity = AppTitleBar.Opacity;

            try
            {
                for (var i = 0; i < 2; i++)
                {
                    AppTitleBar.Opacity = 0.55;
                    await Task.Delay(80);
                    AppTitleBar.Opacity = originalOpacity;
                    await Task.Delay(80);
                }
            }
            finally
            {
                AppTitleBar.Opacity = originalOpacity;
                _isShowingAlreadyOpenCue = false;
            }
        });
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
        try
        {
            if (AppWindow == null)
                return;

            // Cache title bar reference
            if (_titleBar == null)
            {
                _titleBar = AppWindow.TitleBar;
            }

            if (_titleBar == null)
                return;

            // Use centralized ThemeManager for consistent, fast dark mode detection
            bool isDarkMode = ThemeManager.IsDarkTheme();

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
            _titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            _titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            // Button foreground (icon) colors
            _titleBar.ButtonForegroundColor = foregroundColor;
            _titleBar.ButtonInactiveForegroundColor = inactiveForegroundColor;

            // Hover colors
            _titleBar.ButtonHoverBackgroundColor = hoverBgColor;
            _titleBar.ButtonHoverForegroundColor = foregroundColor;

            // Pressed colors
            _titleBar.ButtonPressedBackgroundColor = pressedBgColor;
            _titleBar.ButtonPressedForegroundColor = foregroundColor;
        }
        catch { }
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
        if (this.Content is FrameworkElement content)
        {
            content.DispatcherQueue.TryEnqueue(() =>
            {
                ApplyTheme(theme);
                UpdateTitleBarColors();
            });
        }
    }

    private void ApplyTheme(Services.AppTheme theme)
    {
        if (this.Content is FrameworkElement content)
        {
            content.RequestedTheme = ThemeManager.GetElementTheme(theme);
        }
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
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }

        // Unsubscribe from theme changes
        ThemeManager.ThemeChanged -= OnThemeChanged;

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
        if (MaxHistoryBox == null || ThemeComboBox == null || ExcludedAppsTextBox == null)
            return;

        MaxHistoryBox.Value = _settings.MaxHistoryItems;
        _lastValidMaxHistory = _settings.MaxHistoryItems;

        UpdateHotkeyDisplay(_settings.Hotkey);

        StartOnBootToggle.IsOn = _settings.StartOnBoot;
        ShowPreviewToggle.IsOn = _settings.ShowPreviewPanel;
        ShowNotificationsToggle.IsOn = _settings.ShowNotifications;
        ExcludedAppsTextBox.Text = string.Join(Environment.NewLine, _settings.ExcludedApps);
        AdjustExcludedAppsTextBoxHeight();

        // Set theme combo box
        var themeIndex = _settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0, // System
        };
        ThemeComboBox.SelectedIndex = themeIndex;
    }

    private void InitializeUpdateState()
    {
        _isInitializingUpdateControls = true;
        LoadUpdateState();

        if (AutoUpdateToggle != null)
        {
            AutoUpdateToggle.IsOn = _updateState.AutoUpdateEnabled;
        }

        if (UpdateStatusText != null)
        {
            UpdateStatusText.Text = "Update status: Not checked yet";
        }

        if (UpdateLastCheckedText != null)
        {
            UpdateLastCheckedText.Text = FormatLastCheckedLabel(_updateState.LastCheckedLocalDate);
        }

        _isInitializingUpdateControls = false;
    }

    private void LoadUpdateState()
    {
        var path = GetUpdateStateFilePath();
        if (!File.Exists(path))
        {
            _updateState = new UpdateState();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            _updateState = JsonSerializer.Deserialize<UpdateState>(json) ?? new UpdateState();
        }
        catch
        {
            _updateState = new UpdateState();
        }
    }

    private async Task SaveUpdateStateAsync()
    {
        var path = GetUpdateStateFilePath();
        var json = JsonSerializer.Serialize(_updateState, new JsonSerializerOptions { WriteIndented = true });

        await _updateStateLock.WaitAsync();
        try
        {
            File.WriteAllText(path, json);
        }
        catch { }
        finally
        {
            _updateStateLock.Release();
        }
    }

    private static string FormatLastCheckedLabel(DateTime? lastChecked)
    {
        return lastChecked.HasValue ? $"Last checked: {lastChecked.Value:MMM d, yyyy}" : "Last checked: never";
    }

    private void UpdateHotkeyDisplay(string hotkey)
    {
        _hotkeyParts.Clear();

        if (string.IsNullOrEmpty(hotkey))
            return;

        var parts = hotkey.Split('+');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var label = trimmed switch
            {
                "Windows" => "Win",
                "Control" => "Ctrl",
                _ => trimmed,
            };

            _hotkeyParts.Add(label);
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
                    _ = CheckForUpdatesIfNeededAsync();
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

        await dialog.ShowAsync();

        if (dialog.IsSaved)
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

    private void ExcludedAppsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var excludedApps = ParseExcludedApps(ExcludedAppsTextBox.Text);
        _settings.ExcludedApps = excludedApps;

        var normalizedText = string.Join(Environment.NewLine, excludedApps);
        if (!string.Equals(ExcludedAppsTextBox.Text, normalizedText, StringComparison.Ordinal))
        {
            ExcludedAppsTextBox.Text = normalizedText;
        }

        SaveSettings();
    }

    private void ExcludedAppsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        AdjustExcludedAppsTextBoxHeight();
    }

    private void AdjustExcludedAppsTextBoxHeight()
    {
        if (ExcludedAppsTextBox == null)
            return;

        var lineCount = CountLines(ExcludedAppsTextBox.Text);
        var targetHeight = Math.Max(
            ExcludedAppsTextBoxMinHeight,
            (lineCount * ExcludedAppsTextBoxLineHeight) + ExcludedAppsTextBoxVerticalPadding
        );

        ExcludedAppsTextBox.Height = targetHeight;
    }

    private static int CountLines(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 1;
        }

        var lineCount = 1;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\r' || value[i] == '\n')
            {
                lineCount++;

                if (value[i] == '\r' && i + 1 < value.Length && value[i + 1] == '\n')
                {
                    i++;
                }
            }
        }

        return lineCount;
    }

    private static List<string> ParseExcludedApps(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new List<string>();
        }

        return rawInput
            .Split(
                new[] { '\r', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .Where(app => !string.IsNullOrWhiteSpace(app))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            var themeTag = item.Tag?.ToString() ?? "System";
            _settings.Theme = themeTag;
            SaveSettings();

            // Apply theme globally using ThemeManager
            ThemeManager.SetTheme(ThemeManager.ParseTheme(themeTag));
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(force: true);
    }

    private async void OpenReleasePageButton_Click(object sender, RoutedEventArgs e)
    {
        await TryLaunchUriAsync(GitHubReleasePageUrl);
    }

    private async void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializingUpdateControls)
            return;

        _updateState.AutoUpdateEnabled = AutoUpdateToggle.IsOn;
        await SaveUpdateStateAsync();
    }

    private async Task CheckForUpdatesIfNeededAsync()
    {
        if (_updateState.LastCheckedLocalDate?.Date == DateTime.Today)
        {
            if (UpdateLastCheckedText != null)
            {
                UpdateLastCheckedText.Text = FormatLastCheckedLabel(_updateState.LastCheckedLocalDate);
            }
            return;
        }

        await CheckForUpdatesAsync(force: false);
    }

    private async Task CheckForUpdatesAsync(bool force)
    {
        if (_isCheckingUpdates)
            return;

        if (!force && _updateState.LastCheckedLocalDate?.Date == DateTime.Today)
            return;

        _isCheckingUpdates = true;
        SetUpdateCheckingState(true);

        _updateState.LastCheckedLocalDate = DateTime.Today;
        await SaveUpdateStateAsync();
        if (UpdateLastCheckedText != null)
        {
            UpdateLastCheckedText.Text = FormatLastCheckedLabel(_updateState.LastCheckedLocalDate);
        }

        try
        {
            var release = await GetLatestReleaseAsync();
            if (release == null)
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = "Unable to check for updates.";
                }
                return;
            }

            if (release.Draft || release.Prerelease)
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = "No stable updates available.";
                }
                return;
            }

            var latestVersionLabel = GetReleaseVersionLabel(release);
            var currentVersion = TryParseVersion(GetBridgeRawAppVersion());
            var latestVersion = TryParseVersion(latestVersionLabel);

            if (currentVersion == null || latestVersion == null)
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = $"Latest release: {latestVersionLabel}";
                }
                return;
            }

            if (latestVersion > currentVersion)
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = $"Update available: {latestVersionLabel}";
                }

                if (_updateState.AutoUpdateEnabled)
                {
                    await StartUpdateAsync(release, latestVersionLabel);
                }
                else
                {
                    await PromptForUpdateAsync(release, latestVersionLabel);
                }
            }
            else
            {
                if (UpdateStatusText != null)
                {
                    UpdateStatusText.Text = "You're up to date.";
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            if (UpdateStatusText != null)
            {
                UpdateStatusText.Text = "Unable to check for updates.";
            }
        }
        finally
        {
            _isCheckingUpdates = false;
            SetUpdateCheckingState(false);
        }
    }

    private void SetUpdateCheckingState(bool isChecking)
    {
        if (UpdateStatusText != null && isChecking)
        {
            UpdateStatusText.Text = "Checking for updates...";
        }

        if (CheckUpdatesButton != null)
        {
            CheckUpdatesButton.IsEnabled = !isChecking;
            CheckUpdatesButton.Content = isChecking ? "Checking..." : "Check now";
        }
    }

    private async Task PromptForUpdateAsync(GitHubRelease release, string latestVersionLabel)
    {
        if (Content?.XamlRoot == null)
            return;

        var dialog = new ContentDialog
        {
            Title = $"Update available ({latestVersionLabel})",
            Content = "A newer version of Clipcy is available. Would you like to download and install it?",
            PrimaryButtonText = "Download and install",
            SecondaryButtonText = "Auto update",
            CloseButtonText = "Not now",
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await StartUpdateAsync(release, latestVersionLabel);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            _updateState.AutoUpdateEnabled = true;
            if (AutoUpdateToggle != null)
            {
                _isInitializingUpdateControls = true;
                AutoUpdateToggle.IsOn = true;
                _isInitializingUpdateControls = false;
            }
            await SaveUpdateStateAsync();
            await StartUpdateAsync(release, latestVersionLabel);
        }
    }

    private async Task StartUpdateAsync(GitHubRelease release, string latestVersionLabel)
    {
        var installUrl = GetInstallAssetUrl(release);
        var targetUrl = installUrl ?? release.HtmlUrl ?? GitHubReleasePageUrl;

        if (UpdateStatusText != null && installUrl == null)
        {
            UpdateStatusText.Text = $"Update available: {latestVersionLabel}. Opening releases...";
        }

        var launched = await TryLaunchUriAsync(targetUrl);
        if (!launched && UpdateStatusText != null)
        {
            UpdateStatusText.Text = "Unable to launch the installer.";
        }
    }

    private static async Task<bool> TryLaunchUriAsync(string url)
    {
        try
        {
            return await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch
        {
            return false;
        }
    }

    private static string GetReleaseVersionLabel(GitHubRelease release)
    {
        if (!string.IsNullOrWhiteSpace(release.TagName))
            return release.TagName;

        if (!string.IsNullOrWhiteSpace(release.Name))
            return release.Name;

        return "latest";
    }

    private static Version? TryParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }

        var splitIndex = trimmed.IndexOfAny(new[] { '-', '+' });
        if (splitIndex >= 0)
        {
            trimmed = trimmed[..splitIndex];
        }

        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    private static string? GetInstallAssetUrl(GitHubRelease release)
    {
        if (release.Assets == null || release.Assets.Count == 0)
            return null;

        string[] preferredExtensions = { ".appinstaller", ".msix", ".exe", ".msi" };
        foreach (var extension in preferredExtensions)
        {
            foreach (var asset in release.Assets)
            {
                if (string.IsNullOrWhiteSpace(asset.Name) || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                    continue;

                if (asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return asset.BrowserDownloadUrl;
            }
        }

        return null;
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubLatestReleaseApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _updateHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitHubRelease>(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateUpdateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipcyUpdateChecker/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetUpdateStateFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var clipcyDir = Path.Combine(localAppData, "clipcy");
        Directory.CreateDirectory(clipcyDir);
        return Path.Combine(clipcyDir, "update-state.json");
    }

    private static string GetBridgeRawAppVersion()
    {
        try
        {
            return App.Bridge?.GetAppVersion() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async void SaveSettings()
    {
        var settingsToSave = _settings;
        await Task.Run(async () =>
        {
            await _saveLock.WaitAsync();
            try
            {
                App.Bridge?.SaveSettings(settingsToSave);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        });
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = new SettingsViewModel();
        LoadSettingsToUI();
        SaveSettings();
        // Reset theme to default (system)
        ThemeManager.SetTheme(ThemeManager.ParseTheme(_settings.Theme));
    }

    private sealed class UpdateState
    {
        public DateTime? LastCheckedLocalDate { get; set; }
        public bool AutoUpdateEnabled { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }

    private static string GetBridgeAppVersion()
    {
        try
        {
            var version = GetBridgeRawAppVersion();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return $"Version {version}";
            }
        }
        catch { }

        return "Version";
    }
}
