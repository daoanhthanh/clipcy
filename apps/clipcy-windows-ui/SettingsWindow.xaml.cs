using System.IO;
using Microsoft.UI.Composition.SystemBackdrops;
using Clipcy.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

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

    // Responsive thresholds
    private const int MinWindowWidth = 500;
    private const int MinWindowHeight = 400;
    private const int SidebarCollapseThreshold = 720;

    public SettingsWindow()
    {
        InitializeComponent();

        SetupAcrylicBackdrop();
        SetupTitleBar();
        this.Activated += SettingsWindow_Activated;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Set window size and minimum size
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 960, Height = 640 });

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
        catch
        {
        }
    }

    private void UpdateTitleBarColors()
    {
        try
        {
            if (AppWindow == null || AppWindow.TitleBar == null)
                return;

            var titleBar = AppWindow.TitleBar;

            // Detect if we're in dark mode using UISettings
            bool isDarkMode = false;

            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var foreground = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
                // If foreground is light (close to white), we're in dark mode
                isDarkMode = ((foreground.R + foreground.G + foreground.B) / 3.0) > 128;
            }
            catch
            {
                // Fallback to checking element theme
                var rootElement = Content as FrameworkElement;
                if (rootElement != null)
                {
                    if (rootElement.ActualTheme == ElementTheme.Dark)
                    {
                        isDarkMode = true;
                    }
                    else if (rootElement.ActualTheme == ElementTheme.Default)
                    {
                        isDarkMode = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                    }
                }
            }

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
                SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                _configurationSource = new SystemBackdropConfiguration
                {
                    IsInputActive = true,
                };
            }
        }
        catch
        {
        }
    }

    private void SettingsWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_configurationSource != null)
        {
            _configurationSource.IsInputActive =
                args.WindowActivationState != WindowActivationState.Deactivated;
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
        if (SidebarPanel == null || HamburgerButton == null || CompactActionButtons == null)
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
        HamburgerButton.Visibility = Visibility.Visible;
        CompactActionButtons.Visibility = Visibility.Visible;
    }

    private void ExpandSidebar()
    {
        _isSidebarExpanded = true;
        SidebarPanel.Visibility = Visibility.Visible;
        HamburgerButton.Visibility = Visibility.Collapsed;
        CompactActionButtons.Visibility = Visibility.Collapsed;
    }

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle sidebar in compact mode
        if (SidebarPanel.Visibility == Visibility.Collapsed)
        {
            SidebarPanel.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void EditHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutPickerDialog(_settings.Hotkey)
        {
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _settings.Hotkey = dialog.Hotkey;
            UpdateHotkeyDisplay(dialog.Hotkey);
        }
    }

    private void MaxHistoryBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            _settings.MaxHistoryItems = (uint)args.NewValue;
            _lastValidMaxHistory = args.NewValue;
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
    }

    private void ShowPreviewToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.ShowPreviewPanel = ShowPreviewToggle.IsOn;
    }

    private void ShowNotificationsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.ShowNotifications = ShowNotificationsToggle.IsOn;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            _settings.Theme = item.Tag?.ToString() ?? "System";
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = new SettingsViewModel();
        LoadSettingsToUI();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Bridge?.SaveSettings(_settings);

            // Show success feedback
            var dialog = new ContentDialog
            {
                Title = "Success",
                Content = "Settings saved successfully",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // Show error dialog
            ShowErrorDialog($"Failed to save settings: {ex.Message}");
        }
    }

    private async void ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
