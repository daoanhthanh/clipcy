using System;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace Clipcy.Services
{
    /// <summary>
    /// Theme listener using UISettings.ColorValuesChanged.
    /// This is the WinRT API specifically designed for instant theme change notification.
    /// </summary>
    public class ThemeListener : IDisposable
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";

        private readonly UISettings _uiSettings;
        private bool _disposed;
        private bool _lastIsDark;

        /// <summary>
        /// Gets whether the current theme is dark.
        /// </summary>
        public bool IsDarkTheme => _lastIsDark;

        /// <summary>
        /// Event fired when the theme changes. The bool parameter is true if dark theme.
        /// </summary>
        public event Action<bool>? ThemeChanged;

        public ThemeListener()
        {
            // Get initial theme from registry (most accurate)
            _lastIsDark = GetCurrentThemeFromRegistry();

            // Use UISettings for instant notification
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += OnColorValuesChanged;

            System.Diagnostics.Debug.WriteLine(
                $"[ThemeListener] Started UISettings listener. Initial isDark: {_lastIsDark}"
            );
        }

        private void OnColorValuesChanged(UISettings sender, object args)
        {
            // Read current theme from registry (more reliable than color calculation)
            var newIsDark = GetCurrentThemeFromRegistry();

            if (newIsDark != _lastIsDark)
            {
                _lastIsDark = newIsDark;
                ThemeChanged?.Invoke(newIsDark);
            }
        }

        /// <summary>
        /// Read current theme directly from registry.
        /// </summary>
        private static bool GetCurrentThemeFromRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key != null)
                {
                    var value = key.GetValue(AppsUseLightTheme);
                    if (value is int intValue)
                    {
                        return intValue == 0; // 0 = Dark, 1 = Light
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeListener] Registry error: {ex.Message}");
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
            GC.SuppressFinalize(this);
        }
    }
}
