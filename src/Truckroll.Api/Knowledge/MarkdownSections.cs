using System.Text;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// Cuts markdown into passages along its headings, then packs or splits sections to a size a
/// citation can show and an embedding can represent well.
/// </summary>
/// <remarks>
/// Headings are the author's own idea of where one topic ends and the next begins, which beats any
/// fixed-size window: "Burners light then shut off" stays one passage, with its heading path as
/// context. Only sections longer than <see cref="MaxCharacters"/> are split, at blank lines, and
/// a table is never cut mid-row: a long one is split between rows with its header repeated.
/// </remarks>
public static class MarkdownSections
{
    /// <summary>About 300 tokens: room for a whole troubleshooting step, short enough to cite.</summary>
    public const int MaxCharacters = 1200;

    public sealed record Section(string Title, string Text);

    /// <summary>
    /// Splits a document into sections titled by their heading path. The level-1 heading is folded
    /// into <paramref name="documentTitle"/>; sections with no text of their own (a heading whose
    /// content is all in sub-headings) are skipped.
    /// </summary>
    public static IEnumerable<Section> Split(string documentTitle, string markdown)
    {
        var headings = new List<(int Level, string Text)>();
        var body = new StringBuilder();
        var inFence = false;

        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (!inFence && HeadingLevel(line) is { } level)
            {
                foreach (var section in Flush(documentTitle, headings, body))
                {
                    yield return section;
                }

                headings.RemoveAll(h => h.Level >= level);
                headings.Add((level, line[(level + 1)..].Trim()));
                continue;
            }

            // '\n', not AppendLine: on Windows that writes '\r\n', and the blank-line split below
            // would then never find a paragraph break.
            body.Append(line).Append('\n');
        }

        foreach (var section in Flush(documentTitle, headings, body))
        {
            yield return section;
        }
    }

    /// <summary>
    /// Packs blocks (paragraphs, tables, notes) into pieces of at most <see cref="MaxCharacters"/>,
    /// keeping each block whole when it fits. Used for documents and for jobs with many notes.
    /// </summary>
    public static IEnumerable<string> Pack(IEnumerable<string> blocks)
    {
        var current = new StringBuilder();
        foreach (var block in blocks.SelectMany(SplitOversized))
        {
            if (current.Length > 0 && current.Length + block.Length + 2 > MaxCharacters)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }

            current.Append(block).Append("\n\n");
        }

        if (current.Length > 0 && !string.IsNullOrWhiteSpace(current.ToString()))
        {
            yield return current.ToString().Trim();
        }
    }

    private static IEnumerable<Section> Flush(string documentTitle, List<(int Level, string Text)> headings, StringBuilder body)
    {
        var text = body.ToString().Trim();
        body.Clear();
        if (text.Length == 0)
        {
            yield break;
        }

        var path = headings.Where(h => h.Level > 1).Select(h => h.Text).Prepend(documentTitle);
        var title = string.Join(" > ", path);
        foreach (var piece in Pack(Blocks(text)))
        {
            yield return new Section(title, piece);
        }
    }

    /// <summary>Paragraphs and tables, split at blank lines.</summary>
    private static IEnumerable<string> Blocks(string text) =>
        text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A block over the limit: a table splits between rows (header repeated), prose between lines.</summary>
    private static IEnumerable<string> SplitOversized(string block)
    {
        if (block.Length <= MaxCharacters)
        {
            yield return block;
            yield break;
        }

        var lines = block.Split('\n');
        var isTable = lines.All(l => l.TrimStart().StartsWith('|'));
        var header = isTable && lines.Length > 2 ? lines[0] + "\n" + lines[1] + "\n" : "";
        var rows = isTable && lines.Length > 2 ? lines[2..] : lines;

        var current = new StringBuilder(header);
        foreach (var row in rows)
        {
            if (current.Length > header.Length && current.Length + row.Length + 1 > MaxCharacters)
            {
                yield return current.ToString().TrimEnd();
                current.Clear().Append(header);
            }

            current.Append(row).Append('\n');
        }

        yield return current.ToString().TrimEnd();
    }

    private static int? HeadingLevel(string line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        return level is >= 1 and <= 6 && line.Length > level && line[level] == ' ' ? level : null;
    }
}
