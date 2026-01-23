using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Clipcy;

public sealed partial class ShortcutPickerDialog : ContentDialog
{
    private List<VirtualKey> _capturedKeys = new();
    private string _originalHotkey = "";

    public string Hotkey { get; private set; } = "";

    public ShortcutPickerDialog(string currentHotkey)
    {
        InitializeComponent();
        _originalHotkey = currentHotkey;
        Hotkey = currentHotkey;

        ParseAndDisplayHotkey(currentHotkey);

        this.KeyDown += Dialog_KeyDown;
        this.Loaded += (s, e) => this.Focus(FocusState.Programmatic);
    }

    private void ParseAndDisplayHotkey(string hotkey)
    {
        _capturedKeys.Clear();

        if (string.IsNullOrEmpty(hotkey))
        {
            UpdateDisplay();
            return;
        }

        var parts = hotkey.Split('+');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var key = trimmed switch
            {
                "Ctrl" or "Control" => VirtualKey.Control,
                "Shift" => VirtualKey.Shift,
                "Alt" => VirtualKey.Menu,
                "Win" or "Windows" => VirtualKey.LeftWindows,
                _ => ParseMainKey(trimmed)
            };

            if (key != VirtualKey.None)
            {
                _capturedKeys.Add(key);
            }
        }

        UpdateDisplay();
    }

    private VirtualKey ParseMainKey(string keyName)
    {
        if (Enum.TryParse<VirtualKey>(keyName, true, out var key))
        {
            return key;
        }
        return VirtualKey.None;
    }

    private void Dialog_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;

        var key = e.Key;

        if (IsModifierKey(key))
        {
            var normalized = NormalizeModifier(key);
            if (!_capturedKeys.Contains(normalized))
            {
                _capturedKeys.Add(normalized);
            }
            UpdateDisplay();
            return;
        }

        if (key != VirtualKey.Escape)
        {
            _capturedKeys.Add(key);
        }

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        WinKeyButton.Visibility = Visibility.Collapsed;
        CtrlKeyButton.Visibility = Visibility.Collapsed;
        AltKeyButton.Visibility = Visibility.Collapsed;
        ShiftKeyButton.Visibility = Visibility.Collapsed;
        MainKeyButton.Visibility = Visibility.Collapsed;

        foreach (var key in _capturedKeys)
        {
            switch (key)
            {
                case VirtualKey.LeftWindows:
                case VirtualKey.RightWindows:
                    WinKeyButton.Visibility = Visibility.Visible;
                    break;
                case VirtualKey.Control:
                    CtrlKeyButton.Visibility = Visibility.Visible;
                    break;
                case VirtualKey.Menu:
                    AltKeyButton.Visibility = Visibility.Visible;
                    break;
                case VirtualKey.Shift:
                    ShiftKeyButton.Visibility = Visibility.Visible;
                    break;
                default:
                    MainKeyButton.Content = GetKeyDisplayName(key);
                    MainKeyButton.Visibility = Visibility.Visible;
                    break;
            }
        }
    }

    private string BuildHotkeyString()
    {
        var parts = new List<string>();

        foreach (var key in _capturedKeys)
        {
            parts.Add(GetKeyDisplayName(key));
        }

        return string.Join("+", parts);
    }

    private static bool IsModifierKey(VirtualKey key) => key switch
    {
        VirtualKey.Control
        or VirtualKey.LeftControl
        or VirtualKey.RightControl
        or VirtualKey.Shift
        or VirtualKey.LeftShift
        or VirtualKey.RightShift
        or VirtualKey.Menu
        or VirtualKey.LeftMenu
        or VirtualKey.RightMenu
        or VirtualKey.LeftWindows
        or VirtualKey.RightWindows => true,
        _ => false,
    };

    private static VirtualKey NormalizeModifier(VirtualKey key) => key switch
    {
        VirtualKey.LeftControl or VirtualKey.RightControl => VirtualKey.Control,
        VirtualKey.LeftShift or VirtualKey.RightShift => VirtualKey.Shift,
        VirtualKey.LeftMenu or VirtualKey.RightMenu => VirtualKey.Menu,
        VirtualKey.LeftWindows or VirtualKey.RightWindows => VirtualKey.LeftWindows,
        _ => key,
    };

    private static string GetKeyDisplayName(VirtualKey key) => key switch
    {
        VirtualKey.Control => "Ctrl",
        VirtualKey.Shift => "Shift",
        VirtualKey.Menu => "Alt",
        VirtualKey.LeftWindows or VirtualKey.RightWindows => "Win",
        _ => key.ToString(),
    };

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        ParseAndDisplayHotkey(_originalHotkey);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _capturedKeys.Clear();
        UpdateDisplay();
    }

    private void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hotkey = BuildHotkeyString();
    }

    private void CloseButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hotkey = _originalHotkey;
    }
}
