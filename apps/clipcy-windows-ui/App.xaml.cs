using System;
using System.IO;
using System.Runtime.InteropServices;
using Clipcy.Services;
using Microsoft.UI.Xaml;

namespace Clipcy;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    /// <summary>
    /// Singleton instance of the Rust clipboard engine
    /// </summary>
    internal static RustBridgeService? Bridge { get; private set; }

    /// <summary>
    /// The main popup window (hidden by default, shown on hotkey)
    /// </summary>
    public static MainWindow? MainPopup { get; private set; }

    /// <summary>
    /// Tray service for system tray icon management
    /// </summary>
    public static TrayService? Tray { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InitializeConsole();

        // Initialize the Rust bridge
        try
        {
            Bridge = new RustBridgeService();
            Bridge.Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize Rust bridge: {ex.Message}");
            // Show error dialog and exit
            Exit();
            return;
        }

        // Initialize theme manager and set theme from saved settings
        Services.ThemeManager.Initialize();
        string? themeTag = null;
        try
        {
            var settings = Bridge?.GetSettings();
            themeTag = settings?.Theme;
        }
        catch { }
        Services.ThemeManager.SetTheme(Services.ThemeManager.ParseTheme(themeTag));

        // Create the main popup window (hidden initially)
        MainPopup = new MainWindow();

        Tray = new TrayService(MainPopup);

        Console.WriteLine("Running...");
    }

    private static void InitializeConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
        {
            return;
        }

        try
        {
            var stdout = Console.OpenStandardOutput();
            var writer = new StreamWriter(stdout) { AutoFlush = true };
            Console.SetOut(writer);

            var stderr = Console.OpenStandardError();
            var errorWriter = new StreamWriter(stderr) { AutoFlush = true };
            Console.SetError(errorWriter);
        }
        catch { }
    }

    /// <summary>
    /// Clean up when application exits
    /// </summary>
    public new void Exit()
    {
        Tray?.Dispose();
        Bridge?.Dispose();
        base.Exit();
    }
}
