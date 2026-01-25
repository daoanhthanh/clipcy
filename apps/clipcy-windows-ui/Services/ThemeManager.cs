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
    /// Centralized theme manager that uses WMI RegistryValueChangeEvent for instant theme detection.
    /// Windows and controls should subscribe to ThemeChanged and set their
    /// Content.RequestedTheme accordingly to leverage WinUI 3's built-in theming.
    /// </summary>
    public static class ThemeManager
    {
        private static AppTheme _currentTheme = AppTheme.System;
        private static bool _isInitialized;
        private static DispatcherQueue? _dispatcherQueue;
        private static ThemeListener? _themeListener;
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
        /// Initialize the theme manager and start listening for system theme changes via WMI.
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

                // Create WMI-based theme listener for instant notification
                _themeListener = new ThemeListener();
                _themeListener.ThemeChanged += OnSystemThemeChanged;

                System.Diagnostics.Debug.WriteLine($"[ThemeManager] Initialized with WMI listener. Initial isDark: {_themeListener.IsDarkTheme}");
            }
        }

        /// <summary>
        /// Called when WMI detects a theme change via RegistryValueChangeEvent.
        /// This is called from a WMI thread, so we must dispatch to UI thread.
        /// </summary>
        private static void OnSystemThemeChanged(bool isDark)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeManager] WMI ThemeChanged! isDark: {isDark}, currentTheme: {_currentTheme}");

            // Only process if we're following system theme
            if (_currentTheme != AppTheme.System)
            {
                System.Diagnostics.Debug.WriteLine("[ThemeManager] Not in System theme mode, ignoring");
                return;
            }

            // Dispatch to UI thread with high priority for instant update
            _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.High, () =>
            {
                System.Diagnostics.Debug.WriteLine("[ThemeManager] Firing ThemeChanged on UI thread...");
                FireThemeChanged();
            });
        }

        private static void FireThemeChanged()
        {
            try
            {
                var subscriberCount = ThemeChanged?.GetInvocationList().Length ?? 0;
                System.Diagnostics.Debug.WriteLine($"[ThemeManager] Notifying {subscriberCount} subscribers");
                ThemeChanged?.Invoke(_currentTheme);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeManager] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the app theme and notifies all subscribers.
        /// </summary>
        /// <param name="theme">The new theme setting</param>
        public static void SetTheme(AppTheme theme)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeManager] SetTheme: {theme}");

            if (!_isInitialized)
                Initialize();

            lock (_lock)
            {
                _currentTheme = theme;
            }

            if (_dispatcherQueue?.HasThreadAccess == true)
            {
                FireThemeChanged();
            }
            else
            {
                _dispatcherQueue?.TryEnqueue(DispatcherQueuePriority.High, FireThemeChanged);
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
        /// Returns true if the effective theme (considering System setting) is dark.
        /// </summary>
        public static bool IsDarkTheme()
        {
            return _currentTheme switch
            {
                AppTheme.Dark => true,
                AppTheme.Light => false,
                _ => _themeListener?.IsDarkTheme ?? false
            };
        }

        /// <summary>
        /// Cleanup resources when app exits.
        /// </summary>
        public static void Shutdown()
        {
            _themeListener?.Dispose();
            _themeListener = null;
        }
    }
}
