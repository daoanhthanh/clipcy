using System.Drawing;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipcy.Services;

/// <summary>
/// Manages the system tray icon and native Win32 context menu.
/// </summary>
public class TrayService : IDisposable
{
    private readonly MainWindow _mainWindow;
    private TaskbarIcon? _trayIcon;
    private MenuFlyout? _contextMenu;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // Undocumented uxtheme ordinals used by Windows shell apps for dark/light classic menus.
    [DllImport("uxtheme.dll", EntryPoint = "#135", ExactSpelling = true)]
    private static extern int SetPreferredAppMode(PreferredAppMode preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", ExactSpelling = true)]
    private static extern void FlushMenuThemes();

    private enum PreferredAppMode
    {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3,
    }

    public TrayService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeTrayIcon();
        LocalizationService.LanguageChanged += OnLanguageChanged;

        // Keep native menu theme in sync with app/system theme
        ThemeManager.ThemeChanged += OnThemeChanged;
        ApplyNativeMenuTheme(ThemeManager.CurrentTheme);

        // Subscribe to bridge events
        if (App.Bridge != null)
        {
            App.Bridge.OnHotkeyTriggered += OnHotkeyTriggered;
            App.Bridge.OnHistoryUpdated += OnHistoryUpdated;
            App.Bridge.OnNotification += OnNotification;
        }
    }

    private void InitializeTrayIcon()
    {
        // MenuFlyout is the source definition; ContextMenuMode.PopupMenu converts it to a
        // native Win32 HMENU that follows the Windows menu chrome + theme.
        // PopupMenu mode only invokes MenuFlyoutItem.Command (Click handlers are ignored).
        _contextMenu = new MenuFlyout();

        _contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.Get("tray.show"),
            Command = new RelayCommand(ShowMainWindow),
        });

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        _contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.Get("tray.clear_history"),
            Command = new RelayCommand(ClearHistory),
        });

        _contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.Get("common.settings"),
            Command = new RelayCommand(OpenSettings),
        });

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        _contextMenu.Items.Add(new MenuFlyoutItem
        {
            Text = LocalizationService.Get("tray.exit"),
            Command = new RelayCommand(ExitApplication),
        });

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = LocalizationService.Get("tray.tooltip"),
            // Native Win32 popup menu (default); SecondWindow was a WinUI flyout chrome.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = _contextMenu,
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            _trayIcon.Icon = new Icon(iconPath);
        }

        _trayIcon.LeftClickCommand = new RelayCommand(ShowMainWindow);

        // Force create to show the icon immediately
        _trayIcon.ForceCreate();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_trayIcon == null || _contextMenu == null)
            return;

        ((MenuFlyoutItem)_contextMenu.Items[0]).Text = LocalizationService.Get("tray.show");
        ((MenuFlyoutItem)_contextMenu.Items[2]).Text = LocalizationService.Get("tray.clear_history");
        ((MenuFlyoutItem)_contextMenu.Items[3]).Text = LocalizationService.Get("common.settings");
        ((MenuFlyoutItem)_contextMenu.Items[5]).Text = LocalizationService.Get("tray.exit");
        _trayIcon.ToolTipText = LocalizationService.Get("tray.tooltip");
    }

    private void OnThemeChanged(AppTheme theme)
    {
        ApplyNativeMenuTheme(theme);
    }

    /// <summary>
    /// Configures process-wide preferred app mode so TrackPopupMenu uses light/dark menu chrome.
    /// </summary>
    private static void ApplyNativeMenuTheme(AppTheme theme)
    {
        try
        {
            var mode = theme switch
            {
                AppTheme.Light => PreferredAppMode.ForceLight,
                AppTheme.Dark => PreferredAppMode.ForceDark,
                // System: let Windows pick based on AppsUseLightTheme
                _ => PreferredAppMode.AllowDark,
            };

            _ = SetPreferredAppMode(mode);
            FlushMenuThemes();
        }
        catch (DllNotFoundException)
        {
            // Pre-Win10 builds without the ordinal — classic menu only.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private void OnHotkeyTriggered()
    {
        if (ShouldIgnoreHotkeyTrigger())
        {
            return;
        }

        ShowMainWindow();
    }

    private static bool ShouldIgnoreHotkeyTrigger()
    {
        var settings = App.Bridge?.GetSettings();
        if (settings?.ExcludedApps == null || settings.ExcludedApps.Count == 0)
        {
            return false;
        }

        var excludedApps = NormalizeExcludedApps(settings.ExcludedApps);
        if (excludedApps.Count == 0)
        {
            return false;
        }

        var foregroundIdentifiers = GetForegroundAppIdentifiers();
        if (foregroundIdentifiers.Count == 0)
        {
            return false;
        }

        if (!excludedApps.Overlaps(foregroundIdentifiers))
        {
            return false;
        }

        System.Diagnostics.Debug.WriteLine("Hotkey trigger ignored for excluded foreground app.");
        return true;
    }

    private static HashSet<string> NormalizeExcludedApps(IEnumerable<string> values)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var entries = value.Split(
                new[] { '\r', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            foreach (var entry in entries)
            {
                AddIdentifier(normalized, entry);
            }
        }

        return normalized;
    }

    private static HashSet<string> GetForegroundAppIdentifiers()
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return identifiers;
            }

            GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
            {
                return identifiers;
            }

            using var process = System.Diagnostics.Process.GetProcessById((int)processId);

            AddIdentifier(identifiers, process.ProcessName);
            AddIdentifier(identifiers, $"{process.ProcessName}.exe");

            string? executablePath = null;
            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch { }

            AddIdentifier(identifiers, executablePath);
            AddIdentifier(identifiers, Path.GetFileName(executablePath));
            AddIdentifier(identifiers, Path.GetFileNameWithoutExtension(executablePath));

            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                try
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablePath);
                    AddIdentifier(identifiers, versionInfo.FileDescription);
                    AddIdentifier(identifiers, versionInfo.ProductName);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to resolve foreground app: {ex.Message}");
        }

        return identifiers;
    }

    private static void AddIdentifier(HashSet<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        target.Add(trimmed);

        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 4)
        {
            target.Add(trimmed[..^4]);
        }
    }

    private void OnHistoryUpdated()
    {
        // Could update tray tooltip with item count, etc.
    }

    private void OnNotification(string message)
    {
        _trayIcon?.ShowNotification(
            "Clipcy",
            LocalizationService.Get(message),
            NotificationIcon.Info,
            customIconHandle: null,
            largeIcon: false,
            sound: true,
            respectQuietTime: true,
            realtime: true,
            timeout: null
        );
    }

    private void ShowMainWindow()
    {
        _mainWindow.ShowAtCursor();
    }

    private void ClearHistory()
    {
        try
        {
            App.Bridge?.ClearHistory();
            _mainWindow.ViewModel.RefreshHistory();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clear history error: {ex.Message}");
        }
    }

    private void OpenSettings()
    {
        SettingsWindow.ShowOrActivate();
    }

    private void ExitApplication()
    {
        Dispose();
        Application.Current.Exit();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Unsubscribe from theme changes
        ThemeManager.ThemeChanged -= OnThemeChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;

        if (App.Bridge != null)
        {
            App.Bridge.OnHotkeyTriggered -= OnHotkeyTriggered;
            App.Bridge.OnHistoryUpdated -= OnHistoryUpdated;
            App.Bridge.OnNotification -= OnNotification;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

        GC.SuppressFinalize(this);
    }
}
