using System.Drawing;
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
    private bool _disposed;

    public TrayService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeTrayIcon();

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
        var contextMenu = new MenuFlyout();

        // Show/Hide item
        var showItem = new MenuFlyoutItem { Text = "Show Clipcy" };
        showItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Clear History item
        var clearItem = new MenuFlyoutItem { Text = "Clear History" };
        clearItem.Click += (s, e) => ClearHistory();
        contextMenu.Items.Add(clearItem);

        // Settings item
        var settingsItem = new MenuFlyoutItem { Text = "Settings" };
        settingsItem.Click += (s, e) => OpenSettings();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new MenuFlyoutSeparator());

        // Exit item
        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        // Create tray icon
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Clipcy - Clipboard Manager",
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = contextMenu,
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
