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

    public SettingsWindow()
    {
        InitializeComponent();

        SetupAcrylicBackdrop();
        this.Activated += SettingsWindow_Activated;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 600, Height = 700 });

        // Load current settings
        _settings = App.Bridge?.GetSettings() ?? new SettingsViewModel();
        LoadSettingsToUI();
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
                    MainKeyDisplayButton.Content = trimmed;
                    MainKeyDisplayButton.Visibility = Visibility.Visible;
                    break;
            }
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
