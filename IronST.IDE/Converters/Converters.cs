using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace IronST.IDE.Converters;

/// <summary>
/// true  -> Visible
/// false -> Collapsed
/// int 0 -> Collapsed, int > 0 -> Visible
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        if (value is int i)
            return i > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Inverse: true/false flips. int 0 -> Visible, int > 0 -> Collapsed.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        if (value is int i)
            return i == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

/// <summary>
/// "Error" -> Red, "Warning" -> Yellow, "Info" -> Blue.
/// </summary>
public class SeverityToBrushConverter : IValueConverter
{
    private static readonly Brush ErrorBrush = Make(0xF3, 0x8B, 0xA8);
    private static readonly Brush WarningBrush = Make(0xF9, 0xE2, 0xAF);
    private static readonly Brush InfoBrush = Make(0x89, 0xB4, 0xFA);
    private static readonly Brush DefaultBrush = Make(0xA6, 0xAD, 0xC8);

    private static Brush Make(byte r, byte g, byte b)
    {
        var sb = new SolidColorBrush(Color.FromRgb(r, g, b));
        sb.Freeze();
        return sb;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "Error" => ErrorBrush,
            "Warning" => WarningBrush,
            "Info" => InfoBrush,
            _ => DefaultBrush,
        };

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
