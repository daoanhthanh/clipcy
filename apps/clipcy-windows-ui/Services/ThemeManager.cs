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
    /// Centralized theme manager that broadcasts theme changes.
    /// Windows and controls should subscribe to ThemeChanged and set their
    /// Content.RequestedTheme accordingly to leverage WinUI 3's built-in theming.
    /// </summary>
    public static class ThemeManager
    {
        private static AppTheme _currentTheme = AppTheme.System;

        /// <summary>
        /// Fired when the theme changes. Subscribers should update their RequestedTheme.
        /// </summary>
        public static event Action<AppTheme>? ThemeChanged;

        /// <summary>
        /// Gets the current app theme setting.
        /// </summary>
        public static AppTheme CurrentTheme => _currentTheme;

        /// <summary>
        /// Sets the app theme and notifies all subscribers.
        /// </summary>
        /// <param name="theme">The new theme setting</param>
        public static void SetTheme(AppTheme theme)
        {
            _currentTheme = theme;
            ThemeChanged?.Invoke(theme);
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
    }
}
