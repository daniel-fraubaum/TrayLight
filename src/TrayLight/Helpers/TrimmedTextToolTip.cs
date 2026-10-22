using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrayLight.Helpers;

/// <summary>
/// Attached behavior that shows a TextBlock's own text as its tooltip only
/// while the text is actually cut off (ellipsis or clipped lines). When the
/// full text is visible the tooltip is cleared, so a tooltip set on a parent
/// (e.g. the tile's value tooltip) shows instead of a redundant repeat.
/// </summary>
public static class TrimmedTextToolTip
{
    // Sub-pixel slack so layout rounding never reports a fitting text as trimmed.
    private const double Tolerance = 1.0;

    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TrimmedTextToolTip),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock) return;

        textBlock.Loaded -= OnLoaded;
        textBlock.Unloaded -= OnUnloaded;
        textBlock.SizeChanged -= OnSizeChanged;
        TextDescriptor.RemoveValueChanged(textBlock, OnTextChanged);

        if ((bool)e.NewValue)
        {
            textBlock.Loaded += OnLoaded;
            textBlock.Unloaded += OnUnloaded;
            textBlock.SizeChanged += OnSizeChanged;
            if (textBlock.IsLoaded)
                OnLoaded(textBlock, new RoutedEventArgs());
        }
        else
        {
            textBlock.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var textBlock = (TextBlock)sender;
        // Remove first so a repeated Loaded (re-parenting) never double-subscribes.
        TextDescriptor.RemoveValueChanged(textBlock, OnTextChanged);
        TextDescriptor.AddValueChanged(textBlock, OnTextChanged);
        Update(textBlock);
    }

    // The descriptor holds a strong reference; drop it when the tile is torn down.
    private static void OnUnloaded(object sender, RoutedEventArgs e) =>
        TextDescriptor.RemoveValueChanged((TextBlock)sender, OnTextChanged);

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((TextBlock)sender);

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBlock textBlock) return;
        // A new text may keep the same size (no SizeChanged), so re-check once
        // the pending layout pass has run.
        textBlock.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Update(textBlock));
    }

    private static void Update(TextBlock textBlock)
    {
        try
        {
            if (IsTextTrimmed(textBlock))
                textBlock.ToolTip = textBlock.Text;
            else
                textBlock.ClearValue(FrameworkElement.ToolTipProperty);
        }
        catch
        {
            // Never let a tooltip check crash the popup; just show no tooltip.
            textBlock.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    /// <summary>
    /// True when the rendered text does not fit the TextBlock's arranged size,
    /// i.e. WPF has to trim (ellipsis) or clip it. Measures the full text with
    /// the same font and wrapping width and compares it to the actual size.
    /// </summary>
    internal static bool IsTextTrimmed(TextBlock textBlock)
    {
        var text = textBlock.Text;
        if (string.IsNullOrEmpty(text) || textBlock.ActualWidth <= 0) return false;

        var padding = textBlock.Padding;
        var width = textBlock.ActualWidth - padding.Left - padding.Right;
        var height = textBlock.ActualHeight - padding.Top - padding.Bottom;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            textBlock.FlowDirection,
            new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
            textBlock.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(textBlock).PixelsPerDip);

        if (textBlock.TextWrapping == TextWrapping.NoWrap)
            return formatted.Width > width + Tolerance;

        formatted.MaxTextWidth = Math.Max(1, width);
        if (formatted.Height > height + Tolerance)
            return true;

        // WrapWithOverflow never breaks inside a word, so a single word wider
        // than the line is clipped horizontally.
        return textBlock.TextWrapping == TextWrapping.WrapWithOverflow
            && formatted.MinWidth > width + Tolerance;
    }
}
