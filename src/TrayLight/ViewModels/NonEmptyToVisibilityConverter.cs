using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrayLight.ViewModels;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound string is non-empty
/// and <see cref="Visibility.Collapsed"/> otherwise.
/// </summary>
public class NonEmptyToVisibilityConverter : IValueConverter
{
    public static readonly NonEmptyToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
