using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Clipcy.Services
{
    /// <summary>
    /// Helper class for applying theme-aware brushes to UI elements.
    /// Brushes automatically update when the theme changes.
    /// </summary>
    public static class ThemeBrushHelper
    {
        /// <summary>
        /// Common theme resource keys for brushes.
        /// </summary>
        public static class BrushKeys
        {
            public const string TextPrimary = "TextFillColorPrimaryBrush";
            public const string TextSecondary = "TextFillColorSecondaryBrush";
            public const string TextTertiary = "TextFillColorTertiaryBrush";
            public const string TextDisabled = "TextFillColorDisabledBrush";

            public const string AccentPrimary = "AccentTextFillColorPrimaryBrush";

            public const string LayerDefault = "LayerFillColorDefaultBrush";
            public const string LayerAlt = "LayerFillColorAltBrush";

            public const string SurfaceStroke = "SurfaceStrokeColorDefaultBrush";
            public const string ControlStroke = "ControlStrokeColorDefaultBrush";

            public const string ControlFillDefault = "ControlFillColorDefaultBrush";
            public const string SubtleFill = "SubtleFillColorSecondaryBrush";
        }

        /// <summary>
        /// Gets a brush from theme resources. This brush is tied to the element's theme context.
        /// When used with elements that have RequestedTheme set, it will resolve correctly.
        /// </summary>
        /// <param name="element">The framework element to get resources from (uses its theme context)</param>
        /// <param name="resourceKey">The theme resource key</param>
        /// <returns>The brush, or transparent if not found</returns>
        public static Brush GetBrush(FrameworkElement element, string resourceKey)
        {
            // Try element's resources first (respects RequestedTheme)
            if (element.Resources.TryGetValue(resourceKey, out var elementVal) && elementVal is Brush elementBrush)
            {
                return elementBrush;
            }

            // Fall back to application resources
            if (Application.Current.Resources.TryGetValue(resourceKey, out var val) && val is Brush b)
            {
                return b;
            }

            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        /// <summary>
        /// Applies theme-aware foreground to a TextBlock. The brush will update when theme changes.
        /// </summary>
        /// <param name="textBlock">The TextBlock to style</param>
        /// <param name="brushKey">The brush resource key (use BrushKeys constants)</param>
        /// <param name="themeRoot">Optional: the root element with RequestedTheme set. If null, uses app resources.</param>
        public static void SetForeground(TextBlock textBlock, string brushKey, FrameworkElement? themeRoot = null)
        {
            // Subscribe to theme changes to update the brush
            void UpdateBrush(AppTheme _)
            {
                textBlock.DispatcherQueue.TryEnqueue(() =>
                {
                    var root = themeRoot ?? textBlock;
                    textBlock.Foreground = GetBrush(root, brushKey);
                });
            }

            // Set initial value
            var initialRoot = themeRoot ?? textBlock;
            textBlock.Foreground = GetBrush(initialRoot, brushKey);

            // Subscribe to theme changes
            ThemeManager.ThemeChanged += UpdateBrush;

            // Unsubscribe when unloaded to prevent memory leaks
            textBlock.Unloaded += (s, e) =>
            {
                ThemeManager.ThemeChanged -= UpdateBrush;
            };
        }

        /// <summary>
        /// Applies theme-aware background to a Border. The brush will update when theme changes.
        /// </summary>
        /// <param name="border">The Border to style</param>
        /// <param name="brushKey">The brush resource key (use BrushKeys constants)</param>
        /// <param name="themeRoot">Optional: the root element with RequestedTheme set.</param>
        public static void SetBackground(Border border, string brushKey, FrameworkElement? themeRoot = null)
        {
            void UpdateBrush(AppTheme _)
            {
                border.DispatcherQueue.TryEnqueue(() =>
                {
                    var root = themeRoot ?? border;
                    border.Background = GetBrush(root, brushKey);
                });
            }

            var initialRoot = themeRoot ?? border;
            border.Background = GetBrush(initialRoot, brushKey);

            ThemeManager.ThemeChanged += UpdateBrush;
            border.Unloaded += (s, e) =>
            {
                ThemeManager.ThemeChanged -= UpdateBrush;
            };
        }

        /// <summary>
        /// Applies theme-aware border brush to a Border. The brush will update when theme changes.
        /// </summary>
        /// <param name="border">The Border to style</param>
        /// <param name="brushKey">The brush resource key (use BrushKeys constants)</param>
        /// <param name="themeRoot">Optional: the root element with RequestedTheme set.</param>
        public static void SetBorderBrush(Border border, string brushKey, FrameworkElement? themeRoot = null)
        {
            void UpdateBrush(AppTheme _)
            {
                border.DispatcherQueue.TryEnqueue(() =>
                {
                    var root = themeRoot ?? border;
                    border.BorderBrush = GetBrush(root, brushKey);
                });
            }

            var initialRoot = themeRoot ?? border;
            border.BorderBrush = GetBrush(initialRoot, brushKey);

            ThemeManager.ThemeChanged += UpdateBrush;
            border.Unloaded += (s, e) =>
            {
                ThemeManager.ThemeChanged -= UpdateBrush;
            };
        }

        /// <summary>
        /// Creates an AcrylicBrush that automatically updates its tint color based on theme.
        /// </summary>
        /// <param name="element">The element this brush will be used on (for theme context)</param>
        /// <param name="tintOpacity">Tint opacity (0.0 - 1.0)</param>
        /// <param name="lightTintColor">Tint color for light theme</param>
        /// <param name="darkTintColor">Tint color for dark theme</param>
        /// <returns>An AcrylicBrush</returns>
        public static AcrylicBrush CreateThemedAcrylicBrush(
            FrameworkElement element,
            double tintOpacity = 0.85,
            Windows.UI.Color? lightTintColor = null,
            Windows.UI.Color? darkTintColor = null
        )
        {
            var lightColor = lightTintColor ?? Microsoft.UI.ColorHelper.FromArgb(255, 243, 243, 243);
            var darkColor = darkTintColor ?? Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32);

            var brush = new AcrylicBrush { TintOpacity = tintOpacity, TintLuminosityOpacity = tintOpacity };

            void UpdateBrush(AppTheme theme)
            {
                element.DispatcherQueue.TryEnqueue(() =>
                {
                    var isDark = theme == AppTheme.Dark || (theme == AppTheme.System && IsSystemDarkTheme());

                    brush.TintColor = isDark ? darkColor : lightColor;
                    brush.FallbackColor = isDark ? darkColor : lightColor;
                });
            }

            // Set initial colors
            var initialIsDark =
                ThemeManager.CurrentTheme == AppTheme.Dark
                || (ThemeManager.CurrentTheme == AppTheme.System && IsSystemDarkTheme());
            brush.TintColor = initialIsDark ? darkColor : lightColor;
            brush.FallbackColor = initialIsDark ? darkColor : lightColor;

            // Subscribe to theme changes
            ThemeManager.ThemeChanged += UpdateBrush;
            element.Unloaded += (s, e) =>
            {
                ThemeManager.ThemeChanged -= UpdateBrush;
            };

            return brush;
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var fg = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
                // If foreground is light (close to white), system is in dark mode
                return fg.R > 128 && fg.G > 128 && fg.B > 128;
            }
            catch
            {
                return false;
            }
        }
    }
}
