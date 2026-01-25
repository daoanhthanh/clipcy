using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Clipcy.Services
{
    public enum AppTheme
    {
        System,
        Light,
        Dark,
    }

    /// <summary>
    /// Centralized theme manager that uses the Rust bridge for theme detection.
    /// This provides zero-polling, instant theme change detection via registry notifications.
    /// Windows and controls should subscribe to ThemeChanged and set their
    /// Content.RequestedTheme accordingly to leverage WinUI 3's built-in theming.
    /// </summary>
    public static class ThemeManager
    {
        private static AppTheme _currentTheme = AppTheme.System;
        private static bool _isInitialized;
        private static bool _lastResolvedIsDark;
        private static DispatcherQueue? _dispatcherQueue;
        private static readonly object _lock = new object();

        /// <summary>
        /// Fired when the theme changes. Subscribers should update their RequestedTheme.
        /// This event is always fired on the UI thread.
        /// </summary>
        public static event Action<AppTheme>? ThemeChanged;

        /// <summary>
        /// Gets the current app theme setting.
        /// </summary>
        public static AppTheme CurrentTheme => _currentTheme;

        /// <summary>
        /// Initialize the theme manager and start listening for system theme changes via Rust bridge.
        /// Call this early during app startup on the UI thread.
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized)
                    return;

                _isInitialized = true;
                _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

                // Get initial system theme state from Rust bridge
                _lastResolvedIsDark = GetSystemThemeFromBridge();

                // Subscribe to theme changes from Rust bridge
                if (App.Bridge != null)
                {
                    App.Bridge.OnThemeChanged += OnBridgeThemeChanged;
                }
            }
        }

        /// <summary>
        /// Called when Rust bridge detects a theme change via registry notification.
        /// This is already on the UI thread thanks to RustBridgeService.DispatchEvent.
        /// </summary>
        private static void OnBridgeThemeChanged(bool isDark)
        {
            // Only process if we're following system theme
            if (_currentTheme != AppTheme.System)
                return;

            lock (_lock)
            {
                if (isDark == _lastResolvedIsDark)
                    return;

                _lastResolvedIsDark = isDark;
            }

            // Already on UI thread from bridge dispatch, fire immediately
            try
            {
                ThemeChanged?.Invoke(_currentTheme);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ThemeChanged event error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the app theme and notifies all subscribers.
        /// </summary>
        /// <param name="theme">The new theme setting</param>
        public static void SetTheme(AppTheme theme)
        {
            // Initialize on first use if not already done
            if (!_isInitialized)
                Initialize();

            lock (_lock)
            {
                _currentTheme = theme;
                _lastResolvedIsDark = IsDarkThemeInternal();
            }

            // Fire immediately on current thread if on UI thread, otherwise marshal
            if (_dispatcherQueue?.HasThreadAccess == true)
            {
                ThemeChanged?.Invoke(theme);
            }
            else
            {
                _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.High, () =>
                {
                    ThemeChanged?.Invoke(theme);
                });
            }
        }

        /// <summary>
        /// Converts AppTheme to WinUI ElementTheme for use with RequestedTheme.
        /// </summary>
        public static ElementTheme GetElementTheme(AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default, // System = follow OS
            };
        }

        /// <summary>
        /// Gets the current ElementTheme based on current app theme.
        /// </summary>
        public static ElementTheme CurrentElementTheme => GetElementTheme(_currentTheme);

        /// <summary>
        /// Parses a theme string (from settings) to AppTheme enum.
        /// </summary>
        public static AppTheme ParseTheme(string? themeTag)
        {
            return themeTag switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.System,
            };
        }

        /// <summary>
        /// Gets system theme from Rust bridge (fastest, uses registry directly).
        /// </summary>
        private static bool GetSystemThemeFromBridge()
        {
            try
            {
                return App.Bridge?.IsDarkTheme() ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the effective theme (considering System setting) is dark.
        /// Uses Rust bridge for system theme detection.
        /// </summary>
        public static bool IsDarkTheme() => IsDarkThemeInternal();

        private static bool IsDarkThemeInternal()
        {
            return _currentTheme switch
            {
                AppTheme.Dark => true,
                AppTheme.Light => false,
                _ => GetSystemThemeFromBridge()
            };
        }
    }
}
