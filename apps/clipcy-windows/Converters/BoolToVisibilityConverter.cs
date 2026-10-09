using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Clipcy.Converters;

/// <summary>
/// Converts boolean values to Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolValue = value is bool b && b;

        if (parameter is string paramString && paramString.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}
