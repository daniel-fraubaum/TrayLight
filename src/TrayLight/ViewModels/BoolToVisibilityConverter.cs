using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrayLight.ViewModels;

public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance        = new(invert: false);
    public static readonly BoolToVisibilityConverter InverseInstance = new(invert: true);

    private readonly bool _invert;

    public BoolToVisibilityConverter() : this(invert: false) { }
    private BoolToVisibilityConverter(bool invert) => _invert = invert;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (_invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
