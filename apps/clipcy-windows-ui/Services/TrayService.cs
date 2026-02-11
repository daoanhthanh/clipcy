using System.Drawing;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipcy.Services;

/// <summary>
/// Manages the system tray icon and context menu
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

    public TrayService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeTrayIcon();

        // Subscribe to theme changes
        ThemeManager.ThemeChanged += OnThemeChanged;

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
        // Create context menu
        _contextMenu = new MenuFlyout();

        // Show/Hide item
        var showItem = new MenuFlyoutItem { Text = "Show Clipcy" };
        showItem.Click += (s, e) => ShowMainWindow();
        _contextMenu.Items.Add(showItem);

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Clear History item
        var clearItem = new MenuFlyoutItem { Text = "Clear History" };
        clearItem.Click += (s, e) => ClearHistory();
        _contextMenu.Items.Add(clearItem);

        // Settings item
        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        settingsItem.Click += (s, e) => OpenSettings();
        _contextMenu.Items.Add(settingsItem);

        _contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Exit item
        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        _contextMenu.Items.Add(exitItem);

        // Apply initial theme to context menu
        ApplyThemeToContextMenu(ThemeManager.CurrentTheme);

        // Create tray icon
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Clipcy - Clipboard Manager",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = _contextMenu,
        };

        // Set the icon from the application directory
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(iconPath))
        {
            _trayIcon.Icon = new Icon(iconPath);
        }

        // Handle left click to show the main window
        _trayIcon.LeftClickCommand = new RelayCommand(ShowMainWindow);

        // Force create to show the icon immediately
        _trayIcon.ForceCreate();
    }

    private void OnThemeChanged(AppTheme theme)
    {
        ApplyThemeToContextMenu(theme);
    }

    private void ApplyThemeToContextMenu(AppTheme theme)
    {
        if (_contextMenu == null)
            return;

        // MenuFlyout doesn't have RequestedTheme, so apply to items directly
        // This is still fast since we only have a few menu items
        var elementTheme = ThemeManager.GetElementTheme(theme);
        foreach (var item in _contextMenu.Items)
        {
            if (item is FrameworkElement fe)
            {
                fe.RequestedTheme = elementTheme;
            }
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
            message,
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
