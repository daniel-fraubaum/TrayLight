using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrayLight.Views;

public partial class ShortcutButton : UserControl
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(
            nameof(AccentBrush),
            typeof(Brush),
            typeof(ShortcutButton),
            new PropertyMetadata(Brushes.DodgerBlue, OnAccentBrushChanged));

    public ShortcutButton()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyAccent();
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private static void OnAccentBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ShortcutButton)d).ApplyAccent();

    private void ApplyAccent()
    {
        if (IconBackground is not null)
        {
            IconBackground.Fill = AccentBrush;
        }
    }
}
