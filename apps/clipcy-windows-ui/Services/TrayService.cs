using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
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

        var elementTheme = ThemeManager.GetElementTheme(theme);

        // Apply theme to each menu item - MenuFlyoutItems are FrameworkElements
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
        ShowMainWindow();
    }

    private void OnHistoryUpdated()
    {
        // Could update tray tooltip with item count, etc.
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
        var settingsWindow = new SettingsWindow();
        settingsWindow.Activate();
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
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

        GC.SuppressFinalize(this);
    }
}
