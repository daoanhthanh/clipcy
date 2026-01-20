using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Clipcy.Services;

namespace Clipcy;

/// <summary>
/// Settings window for configuring Clipcy
/// </summary>
public sealed partial class SettingsWindow : Window
{
  private SettingsDto _settings;
  private bool _isCapturingHotkey;
  private List<VirtualKey> _capturedKeys = new();

  public SettingsWindow()
  {
    this.InitializeComponent();

    // Set window size
    this.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 500, Height = 600 });

    // Load current settings
    _settings = App.Bridge?.GetSettings() ?? new SettingsDto();
    LoadSettingsToUI();
  }

  private void LoadSettingsToUI()
  {
    MaxHistoryBox.Value = _settings.MaxHistoryItems;
    HotkeyBox.Text = _settings.Hotkey;
    StartOnBootToggle.IsOn = _settings.StartOnBoot;
    ShowPreviewToggle.IsOn = _settings.ShowPreviewPanel;
    ShowNotificationsToggle.IsOn = _settings.ShowNotifications;

    // Set theme combo box
    var themeIndex = _settings.Theme switch
    {
      "Light" => 1,
      "Dark" => 2,
      _ => 0 // System
    };
    ThemeComboBox.SelectedIndex = themeIndex;
  }

  private void MaxHistoryBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
  {
    if (!double.IsNaN(args.NewValue))
    {
      _settings.MaxHistoryItems = (uint)args.NewValue;
    }
  }

  private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
  {
    _isCapturingHotkey = true;
    _capturedKeys.Clear();
    HotkeyBox.Text = "Press keys...";
  }

  private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
  {
    if (!_isCapturingHotkey) return;

    e.Handled = true;

    // Track modifier keys
    var key = e.Key;
    if (key == VirtualKey.Control || key == VirtualKey.Shift ||
        key == VirtualKey.Menu || key == VirtualKey.LeftWindows ||
        key == VirtualKey.RightWindows)
    {
      if (!_capturedKeys.Contains(key))
        _capturedKeys.Add(key);
      UpdateHotkeyDisplay();
      return;
    }

    // Regular key pressed - finalize hotkey
    _capturedKeys.Add(key);
    _isCapturingHotkey = false;
    UpdateHotkeyDisplay();

    // Convert to hotkey string
    _settings.Hotkey = BuildHotkeyString();
  }

  private void UpdateHotkeyDisplay()
  {
    HotkeyBox.Text = BuildHotkeyString();
  }

  private string BuildHotkeyString()
  {
    var parts = new List<string>();

    foreach (var key in _capturedKeys)
    {
      var keyName = key switch
      {
        VirtualKey.Control => "Ctrl",
        VirtualKey.Shift => "Shift",
        VirtualKey.Menu => "Alt",
        VirtualKey.LeftWindows or VirtualKey.RightWindows => "Win",
        _ => key.ToString()
      };
      parts.Add(keyName);
    }

    return string.Join("+", parts);
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
    _settings = new SettingsDto();
    LoadSettingsToUI();
  }

  private void SaveButton_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      App.Bridge?.SaveSettings(_settings);
      this.Close();
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
      XamlRoot = this.Content.XamlRoot
    };
    await dialog.ShowAsync();
  }
}
