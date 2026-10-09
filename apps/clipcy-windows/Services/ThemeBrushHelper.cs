using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Clipcy.Services
{
    /// <summary>
    /// Helper class for theme resource key constants.
    /// Most theme styling should be done via XAML ThemeResource bindings or
    /// SetResourceReference() in code-behind. This class provides key constants
    /// for programmatic access when needed.
    /// </summary>
    public static class ThemeBrushHelper
    {
        /// <summary>
        /// Common theme resource keys for brushes.
        /// Use these with SetResourceReference() or in XAML with {ThemeResource}.
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
        /// Gets a brush from theme resources.
        /// Prefer using SetResourceReference() for automatic theme updates.
        /// </summary>
        /// <param name="element">The framework element to get resources from</param>
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
    }
}
