using Microsoft.UI.Xaml;
using Clipcy.Services;

namespace Clipcy;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Singleton instance of the Rust clipboard engine
    /// </summary>
    public static RustBridgeService? Bridge { get; private set; }

    /// <summary>
    /// The main popup window (hidden by default, shown on hotkey)
    /// </summary>
    public static MainWindow? MainPopup { get; private set; }

    /// <summary>
    /// Tray service for system tray icon management
    /// </summary>
    public static TrayService? Tray { get; private set; }

    private Window? _window;

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

        // Create the main popup window (hidden initially)
        MainPopup = new MainWindow();
        MainPopup.Hide();

        // Initialize tray icon
        Tray = new TrayService(MainPopup);

        // The app runs in the background - tray icon is the primary UI
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
