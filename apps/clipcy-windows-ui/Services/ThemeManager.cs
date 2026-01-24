using Microsoft.UI.Xaml;

namespace Clipcy.Services
{
    public enum AppTheme
    {
        System,
        Light,
        Dark
    }

    public static class ThemeManager
    {
        private static AppTheme _currentTheme = AppTheme.System;
        private static readonly List<FrameworkElement> _registeredElements = new();

        public static event Action<AppTheme>? ThemeChanged;

        public static AppTheme CurrentTheme => _currentTheme;

        public static void RegisterElement(FrameworkElement element)
        {
            if (!_registeredElements.Contains(element))
            {
                _registeredElements.Add(element);
                ApplyThemeToElement(element, _currentTheme);
            }
        }

        public static void UnregisterElement(FrameworkElement element)
        {
            _registeredElements.Remove(element);
        }

        public static void SetTheme(AppTheme theme)
        {
            _currentTheme = theme;
            foreach (var element in _registeredElements)
            {
                ApplyThemeToElement(element, theme);
            }
            ThemeChanged?.Invoke(theme);
        }

        public static void ApplyThemeToElement(FrameworkElement element, AppTheme theme)
        {
            switch (theme)
            {
                case AppTheme.Light:
                    element.RequestedTheme = ElementTheme.Light;
                    break;
                case AppTheme.Dark:
                    element.RequestedTheme = ElementTheme.Dark;
                    break;
                default:
                    element.RequestedTheme = ElementTheme.Default;
                    break;
            }
        }

        public static AppTheme ParseTheme(string? themeTag)
        {
            return themeTag switch
            {
                "Light" => AppTheme.Light,
                "Dark" => AppTheme.Dark,
                _ => AppTheme.System
            };
        }
    }
}
