using System.Windows;
using System.Windows.Controls;
using TrayLight.Helpers;
using Xunit;

namespace TrayLight.Tests.Views;

/// <summary>
/// The tile title / shortcut label tooltip must only appear when the text is
/// actually cut off (#28). These tests lay out real TextBlocks and check the
/// trimming detection the attached behavior relies on.
/// </summary>
public class TrimmedTextToolTipTests
{
    private const string LongText = "Besturingssysteem versie informatie";

    private static TextBlock Layout(string text, double width, TextWrapping wrapping,
        double maxHeight = double.PositiveInfinity)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Width = width,
            MaxHeight = maxHeight,
            FontSize = 11,
            TextWrapping = wrapping,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        textBlock.Measure(new Size(width, double.PositiveInfinity));
        textBlock.Arrange(new Rect(textBlock.DesiredSize));
        return textBlock;
    }

    [StaFact]
    public void Short_single_line_text_is_not_trimmed()
    {
        var textBlock = Layout("Storage", 200, TextWrapping.NoWrap);
        Assert.False(TrimmedTextToolTip.IsTextTrimmed(textBlock));
    }

    [StaFact]
    public void Long_single_line_text_in_narrow_block_is_trimmed()
    {
        var textBlock = Layout(LongText, 60, TextWrapping.NoWrap);
        Assert.True(TrimmedTextToolTip.IsTextTrimmed(textBlock));
    }

    [StaFact]
    public void Wrapped_text_with_room_to_grow_is_not_trimmed()
    {
        // The tile title case: it wraps and the row grows, so nothing is cut off.
        var textBlock = Layout(LongText, 60, TextWrapping.Wrap);
        Assert.False(TrimmedTextToolTip.IsTextTrimmed(textBlock));
    }

    [StaFact]
    public void Wrapped_text_capped_in_height_is_trimmed()
    {
        var textBlock = Layout(LongText, 60, TextWrapping.Wrap, maxHeight: 16);
        Assert.True(TrimmedTextToolTip.IsTextTrimmed(textBlock));
    }

    [StaFact]
    public void Empty_text_is_not_trimmed()
    {
        var textBlock = Layout(string.Empty, 60, TextWrapping.NoWrap);
        Assert.False(TrimmedTextToolTip.IsTextTrimmed(textBlock));
    }

    [StaFact]
    public void Disabling_the_behavior_clears_the_tooltip()
    {
        var textBlock = Layout(LongText, 60, TextWrapping.NoWrap);
        TrimmedTextToolTip.SetIsEnabled(textBlock, true);
        textBlock.ToolTip = LongText;

        TrimmedTextToolTip.SetIsEnabled(textBlock, false);

        Assert.Null(textBlock.ToolTip);
    }
}
