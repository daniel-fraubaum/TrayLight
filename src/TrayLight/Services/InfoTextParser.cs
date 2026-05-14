using System.Collections.Generic;
using System.Text;

namespace TrayLight.Services;

/// <summary>
/// Single text run produced by <see cref="InfoTextParser"/>. <see cref="IsBold"/>
/// drives the <c>FontWeight</c> on the rendered <c>Run</c>.
/// </summary>
public sealed record InfoTextRun(string Text, bool IsBold);

/// <summary>
/// One line of parsed info text. <see cref="IsBlank"/> means a deliberate
/// empty separator line (rendered as vertical spacing in the popup).
/// </summary>
public sealed record InfoTextLine(IReadOnlyList<InfoTextRun> Runs, bool IsBlank);

/// <summary>
/// Parser for the simple markup used by the popup info-text section:
///   <list type="bullet">
///     <item><c>|</c>  → line break</item>
///     <item><c>||</c> → blank line between blocks</item>
///     <item><c>*…*</c> → bold run (asterisks must come in pairs)</item>
///   </list>
/// Unmatched / odd asterisks are emitted as literal characters so admin
/// typos never produce broken-looking output.
/// </summary>
public static class InfoTextParser
{
    public static IReadOnlyList<InfoTextLine> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return System.Array.Empty<InfoTextLine>();

        // Tokenise by '|'. Two consecutive pipes ("||") produce an empty
        // segment between them, which we map to a blank line.
        var segments = raw.Split('|');
        var lines = new List<InfoTextLine>(segments.Length);

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                lines.Add(new InfoTextLine(System.Array.Empty<InfoTextRun>(), IsBlank: true));
                continue;
            }
            lines.Add(new InfoTextLine(ParseRuns(trimmed), IsBlank: false));
        }

        // Trim trailing blank lines so a config ending in "||" doesn't add
        // an awkward gap at the bottom of the section.
        while (lines.Count > 0 && lines[^1].IsBlank)
            lines.RemoveAt(lines.Count - 1);

        return lines;
    }

    private static IReadOnlyList<InfoTextRun> ParseRuns(string line)
    {
        // Find positions of all asterisks. If the count is odd, the last
        // one is unpaired and gets emitted as a literal '*' instead of
        // toggling bold (so an admin typo doesn't bold half the popup).
        var asteriskPositions = new List<int>();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '*') asteriskPositions.Add(i);
        }
        int lastUnpaired = (asteriskPositions.Count % 2 == 1)
            ? asteriskPositions[^1]
            : -1;

        var runs = new List<InfoTextRun>();
        var buf  = new StringBuilder();
        bool bold = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '*' && i != lastUnpaired)
            {
                if (buf.Length > 0)
                {
                    runs.Add(new InfoTextRun(buf.ToString(), bold));
                    buf.Clear();
                }
                bold = !bold;
                continue;
            }
            buf.Append(c);
        }

        if (buf.Length > 0)
            runs.Add(new InfoTextRun(buf.ToString(), bold));

        return runs;
    }
}
