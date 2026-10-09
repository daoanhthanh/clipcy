using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Clipcy;

public sealed partial class ShortcutPickerDialog : ContentDialog
{
    private const int VkOem1 = 0xBA;
    private const int VkOemPlus = 0xBB;
    private const int VkOemComma = 0xBC;
    private const int VkOemMinus = 0xBD;
    private const int VkOemPeriod = 0xBE;
    private const int VkOem2 = 0xBF;
    private const int VkOem3 = 0xC0;
    private const int VkOem4 = 0xDB;
    private const int VkOem5 = 0xDC;
    private const int VkOem6 = 0xDD;
    private const int VkOem7 = 0xDE;
    private List<VirtualKey> _capturedKeys = new();
    private string _originalHotkey = "";
    private static readonly TimeSpan ComboResetDelay = TimeSpan.FromSeconds(1);
    private DateTimeOffset _lastKeyTime = DateTimeOffset.MinValue;
    private bool _hasStartedCapture;

    public string Hotkey { get; private set; } = "";
    public bool IsSaved { get; private set; }

    public ShortcutPickerDialog(string currentHotkey)
    {
        InitializeComponent();
        _originalHotkey = currentHotkey;
        Hotkey = currentHotkey;
        IsSaved = false;
        _hasStartedCapture = false;

        ParseAndDisplayHotkey(currentHotkey);

        // Apply current theme to dialog content
        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = Services.ThemeManager.CurrentElementTheme;
        }

        if (this.Content is UIElement inputRootElement)
        {
            inputRootElement.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Dialog_KeyDown), true);
        }
        this.KeyDown += Dialog_KeyDown;
        this.Loaded += (s, e) => this.Focus(FocusState.Programmatic);
    }

    private void ParseAndDisplayHotkey(string hotkey)
    {
        _capturedKeys.Clear();
        _lastKeyTime = DateTimeOffset.MinValue;
        _hasStartedCapture = false;

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
                _ => ParseMainKey(trimmed),
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
        var normalized = keyName.Trim();

        var mapped = normalized switch
        {
            "Backspace" => VirtualKey.Back,
            "Tab" => VirtualKey.Tab,
            "Enter" => VirtualKey.Enter,
            "Esc" or "Escape" => VirtualKey.Escape,
            "Space" => VirtualKey.Space,
            "Page Up" => VirtualKey.PageUp,
            "Page Down" => VirtualKey.PageDown,
            "Home" => VirtualKey.Home,
            "End" => VirtualKey.End,
            "Insert" => VirtualKey.Insert,
            "Delete" => VirtualKey.Delete,
            "PrtSc" or "PrintScreen" => VirtualKey.Snapshot,
            "Left" => VirtualKey.Left,
            "Right" => VirtualKey.Right,
            "Up" => VirtualKey.Up,
            "Down" => VirtualKey.Down,
            "0" => VirtualKey.Number0,
            "1" => VirtualKey.Number1,
            "2" => VirtualKey.Number2,
            "3" => VirtualKey.Number3,
            "4" => VirtualKey.Number4,
            "5" => VirtualKey.Number5,
            "6" => VirtualKey.Number6,
            "7" => VirtualKey.Number7,
            "8" => VirtualKey.Number8,
            "9" => VirtualKey.Number9,
            "Num 0" => VirtualKey.NumberPad0,
            "Num 1" => VirtualKey.NumberPad1,
            "Num 2" => VirtualKey.NumberPad2,
            "Num 3" => VirtualKey.NumberPad3,
            "Num 4" => VirtualKey.NumberPad4,
            "Num 5" => VirtualKey.NumberPad5,
            "Num 6" => VirtualKey.NumberPad6,
            "Num 7" => VirtualKey.NumberPad7,
            "Num 8" => VirtualKey.NumberPad8,
            "Num 9" => VirtualKey.NumberPad9,
            ";" => (VirtualKey)VkOem1,
            "=" => (VirtualKey)VkOemPlus,
            "," => (VirtualKey)VkOemComma,
            "-" => (VirtualKey)VkOemMinus,
            "." => (VirtualKey)VkOemPeriod,
            "/" => (VirtualKey)VkOem2,
            "`" => (VirtualKey)VkOem3,
            "[" => (VirtualKey)VkOem4,
            "\\" => (VirtualKey)VkOem5,
            "]" => (VirtualKey)VkOem6,
            "'" => (VirtualKey)VkOem7,
            _ => VirtualKey.None,
        };

        if (mapped != VirtualKey.None)
        {
            return mapped;
        }

        if (Enum.TryParse<VirtualKey>(normalized, true, out var key))
        {
            return key;
        }
        return VirtualKey.None;
    }

    private void Dialog_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        if (e.OriginalKey != VirtualKey.None && e.OriginalKey != e.Key)
        {
            key = e.OriginalKey;
        }

        e.Handled = true;

        if (key == VirtualKey.None)
        {
            return;
        }

        if (!_hasStartedCapture)
        {
            _capturedKeys.Clear();
            _hasStartedCapture = true;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastKeyTime != DateTimeOffset.MinValue && now - _lastKeyTime > ComboResetDelay)
        {
            _capturedKeys.Clear();
        }
        _lastKeyTime = now;

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

        SyncModifiersFromState();
        _capturedKeys.RemoveAll(existing => !IsModifierKey(existing));
        if (!_capturedKeys.Contains(key))
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

        foreach (var key in GetOrderedKeys())
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

        foreach (var key in GetOrderedKeys())
        {
            parts.Add(GetKeyDisplayName(key));
        }

        return string.Join("+", parts);
    }

    private IEnumerable<VirtualKey> GetOrderedKeys()
    {
        var ordered = new List<VirtualKey>();

        if (_capturedKeys.Contains(VirtualKey.LeftWindows) || _capturedKeys.Contains(VirtualKey.RightWindows))
        {
            ordered.Add(VirtualKey.LeftWindows);
        }
        if (_capturedKeys.Contains(VirtualKey.Control))
        {
            ordered.Add(VirtualKey.Control);
        }
        if (_capturedKeys.Contains(VirtualKey.Menu))
        {
            ordered.Add(VirtualKey.Menu);
        }
        if (_capturedKeys.Contains(VirtualKey.Shift))
        {
            ordered.Add(VirtualKey.Shift);
        }

        foreach (var key in _capturedKeys)
        {
            if (!IsModifierKey(key))
            {
                ordered.Add(key);
            }
        }

        return ordered;
    }

    private static bool IsModifierKey(VirtualKey key) =>
        key switch
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

    private static VirtualKey NormalizeModifier(VirtualKey key) =>
        key switch
        {
            VirtualKey.LeftControl or VirtualKey.RightControl => VirtualKey.Control,
            VirtualKey.LeftShift or VirtualKey.RightShift => VirtualKey.Shift,
            VirtualKey.LeftMenu or VirtualKey.RightMenu => VirtualKey.Menu,
            VirtualKey.LeftWindows or VirtualKey.RightWindows => VirtualKey.LeftWindows,
            _ => key,
        };

    private void SyncModifiersFromState()
    {
        if (IsKeyDown(VirtualKey.Control))
        {
            AddModifier(VirtualKey.Control);
        }
        if (IsKeyDown(VirtualKey.Shift))
        {
            AddModifier(VirtualKey.Shift);
        }
        if (IsKeyDown(VirtualKey.Menu))
        {
            AddModifier(VirtualKey.Menu);
        }
        if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
        {
            AddModifier(VirtualKey.LeftWindows);
        }
    }

    private void AddModifier(VirtualKey key)
    {
        if (!_capturedKeys.Contains(key))
        {
            _capturedKeys.Add(key);
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

    private static string GetKeyDisplayName(VirtualKey key) =>
        key switch
        {
            VirtualKey.Control => "Ctrl",
            VirtualKey.Shift => "Shift",
            VirtualKey.Menu => "Alt",
            VirtualKey.LeftWindows or VirtualKey.RightWindows => "Win",
            VirtualKey.Back => "Backspace",
            VirtualKey.Tab => "Tab",
            VirtualKey.Enter => "Enter",
            VirtualKey.Escape => "Esc",
            VirtualKey.Space => "Space",
            VirtualKey.PageUp => "Page Up",
            VirtualKey.PageDown => "Page Down",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Snapshot => "PrtSc",
            VirtualKey.Left => "Left",
            VirtualKey.Right => "Right",
            VirtualKey.Up => "Up",
            VirtualKey.Down => "Down",
            VirtualKey.Number0 => "0",
            VirtualKey.Number1 => "1",
            VirtualKey.Number2 => "2",
            VirtualKey.Number3 => "3",
            VirtualKey.Number4 => "4",
            VirtualKey.Number5 => "5",
            VirtualKey.Number6 => "6",
            VirtualKey.Number7 => "7",
            VirtualKey.Number8 => "8",
            VirtualKey.Number9 => "9",
            VirtualKey.NumberPad0 => "Num 0",
            VirtualKey.NumberPad1 => "Num 1",
            VirtualKey.NumberPad2 => "Num 2",
            VirtualKey.NumberPad3 => "Num 3",
            VirtualKey.NumberPad4 => "Num 4",
            VirtualKey.NumberPad5 => "Num 5",
            VirtualKey.NumberPad6 => "Num 6",
            VirtualKey.NumberPad7 => "Num 7",
            VirtualKey.NumberPad8 => "Num 8",
            VirtualKey.NumberPad9 => "Num 9",
            _ => GetOemDisplayName(key) ?? key.ToString(),
        };

    private static string? GetOemDisplayName(VirtualKey key) =>
        (int)key switch
        {
            VkOem1 => ";",
            VkOemPlus => "=",
            VkOemComma => ",",
            VkOemMinus => "-",
            VkOemPeriod => ".",
            VkOem2 => "/",
            VkOem3 => "`",
            VkOem4 => "[",
            VkOem5 => "\\",
            VkOem6 => "]",
            VkOem7 => "'",
            _ => null,
        };

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        IsSaved = false;
        ParseAndDisplayHotkey(_originalHotkey);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        IsSaved = false;
        _capturedKeys.Clear();
        _lastKeyTime = DateTimeOffset.MinValue;
        UpdateDisplay();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        IsSaved = true;
        Hotkey = BuildHotkeyString();
        Hide();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsSaved = false;
        Hotkey = _originalHotkey;
        Hide();
    }
}
