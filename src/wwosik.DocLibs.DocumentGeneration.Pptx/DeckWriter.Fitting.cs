using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Keeps every slide within its content area. A slide is measured first; what does not fit is set smaller, step by
/// step within limits, and what still does not fit continues on further slides with the same title.
/// </summary>
internal sealed partial class DeckWriter
{
    // Text and table sizes go down 2 pt per step; flow slides by an eighth of their sizes per step.
    private const int SizeStep = 200;
    private const int MinTextSize = 1200;
    private const int MaxVisualStep = 3;
    private const int MaxFlowStep = 2;

    /// <summary>The slide as it is written, with the step it is shrunk by; or its parts, when it had to be split.</summary>
    private IEnumerable<(Slide Slide, int Step)> Fit(Slide slide) => slide switch
    {
        ContentSlide content => this.FitContent(content),
        VisualSlide visual => this.FitVisual(visual),
        FlowSlide flow => this.FitFlow(flow),
        _ => [(slide, 0)],
    };

    // The text size of text slides, from the template's body style, shrunk by `step`.
    private int BodySize(int step) => Math.Max(this.bodySize - (step * SizeStep), Math.Min(MinTextSize, this.bodySize));

    private int MaxContentStep => Math.Max((this.bodySize - MinTextSize) / SizeStep, 0);

    private IEnumerable<(Slide, int)> FitContent(ContentSlide slide)
    {
        var area = this.ContentArea(this.LayoutOf(slide, this.contentLayout));
        long Height(IReadOnlyList<TextParagraph> body, int step) => EstimateHeight(body, this.BodySize(step), area.W);
        int? Step(IReadOnlyList<TextParagraph> body) =>
            Enumerable.Range(0, this.MaxContentStep + 1).Cast<int?>().FirstOrDefault(s => Height(body, s!.Value) <= area.H);

        if (Step(slide.Body) is { } fits)
        {
            return [(slide, fits)];
        }

        var parts = Pack(ListGroups(slide.Body), body => Height(body, this.MaxContentStep) <= area.H);
        return parts.Select((body, i) => ((Slide)new ContentSlide(this.Continued(slide.Title, i), body) { Layout = slide.Layout }, Step(body) ?? this.MaxContentStep));
    }

    private IEnumerable<(Slide, int)> FitVisual(VisualSlide slide)
    {
        var area = this.ContentArea(this.LayoutOf(slide, this.titleOnlyLayout));
        int? Step(VisualSlide part) =>
            Enumerable.Range(0, MaxVisualStep + 1).Cast<int?>().FirstOrDefault(s => this.VisualHeight(part, s!.Value, area.W) <= area.H);

        if (Step(slide) is { } fits)
        {
            return [(slide, fits)];
        }

        if (slide.Table is not { } table || table.Rows.Count - (table.HasHeader ? 1 : 0) < 2)
        {
            return [(slide, MaxVisualStep)];
        }

        // Rows split across slides, the header row on each; text above on the first, below on the last.
        var first = table.HasHeader ? 1 : 0;
        var rows = Enumerable.Range(first, table.Rows.Count - first).Select(r => new List<int> { r }).ToList();
        VisualSlide Part(List<int> chosen, bool isFirst, bool isLast, int index) => new(
            this.Continued(slide.Title, index),
            isFirst ? slide.Above : [],
            Subset(table, chosen),
            null,
            isLast ? slide.Below : []) { Layout = slide.Layout };
        var parts = Pack(rows, chosen => this.VisualHeight(Part(chosen, chosen[0] == first, false, 0), MaxVisualStep, area.W) <= area.H);
        return parts.Select((chosen, i) =>
        {
            var part = Part(chosen, i == 0, i == parts.Count - 1, i);
            return ((Slide)part, Step(part) ?? MaxVisualStep);
        });
    }

    // Text above, the table, image or drawing (an image takes at least a quarter of the area), the caption below.
    private long VisualHeight(VisualSlide slide, int step, long width)
    {
        var height = slide.Above.Count > 0 ? EstimateHeight(slide.Above, VisualTextSize(step), width) + Gap : 0;
        height += slide.Table is { } table
            ? TableLayout(table, width, TableStartSize(table) - (step * SizeStep)).RowHeights.Sum()
            : this.ContentArea(this.titleOnlyLayout).H / 4;
        return height + (slide.Below.Count > 0 ? EstimateHeight(slide.Below, CaptionTextSize(step), width) + Gap : 0);
    }

    private static int VisualTextSize(int step) => Math.Max(TextSize - (step * SizeStep), MinTextSize);

    private static int CaptionTextSize(int step) => Math.Max(CaptionSize - (step * SizeStep), 1000);

    private IEnumerable<(Slide, int)> FitFlow(FlowSlide slide)
    {
        var area = this.ContentArea(this.FlowLayout(slide));
        long Height(IReadOnlyList<FlowBlock> blocks, int step) => this.Stack(null, blocks, area, this.FlowStyleOf(slide, step));
        int? Step(IReadOnlyList<FlowBlock> blocks) =>
            Enumerable.Range(0, MaxFlowStep + 1).Cast<int?>().FirstOrDefault(s => Height(blocks, s!.Value) <= area.H);

        if (Step(slide.Blocks) is { } fits)
        {
            return [(slide, fits)];
        }

        var parts = Pack(FlowUnits(slide.Blocks), pieces => Height(Combine(pieces), MaxFlowStep) <= area.H);
        return parts.Select((pieces, i) =>
        {
            var blocks = Combine(pieces);
            return ((Slide)new FlowSlide(this.Continued(slide.Title, i), slide.Kicker, slide.Dark, blocks) { Layout = slide.Layout }, Step(blocks) ?? MaxFlowStep);
        });
    }

    private FlowStyle FlowStyleOf(FlowSlide slide, int step)
    {
        var scale = 1.0 - (step * 0.125);
        return new FlowStyle(slide.Dark || this.palette.DarkSlides, (int)(FlowTextSize * scale), scale);
    }

    private IReadOnlyList<TextRun> Continued(IReadOnlyList<TextRun> title, int index) =>
        index > 0 && this.options.ContinuationSuffix is { Length: > 0 } suffix ? [.. title, new TextRun(suffix, RunStyle.None)] : title;

    // Units packed greedily into parts that fit; a unit too big for a part of its own gets one anyway.
    private static List<List<T>> Pack<T>(IEnumerable<List<T>> units, Func<List<T>, bool> fits)
    {
        var parts = new List<List<T>>();
        var current = new List<T>();
        foreach (var unit in units)
        {
            if (current.Count > 0 && !fits([.. current, .. unit]))
            {
                parts.Add(current);
                current = [];
            }

            current.AddRange(unit);
        }

        if (current.Count > 0)
        {
            parts.Add(current);
        }

        return parts;
    }

    // Paragraphs in groups that stay together: a top-level paragraph with the deeper ones after it.
    private static IEnumerable<List<TextParagraph>> ListGroups(IReadOnlyList<TextParagraph> paragraphs)
    {
        var group = new List<TextParagraph>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Level == 0 && group.Count > 0)
            {
                yield return group;
                group = [];
            }

            group.Add(paragraph);
        }

        if (group.Count > 0)
        {
            yield return group;
        }
    }

    // The header row, if any, and the body rows chosen.
    private static SlideTable Subset(SlideTable table, IReadOnlyList<int> rows)
    {
        var chosen = (table.HasHeader ? [0] : Enumerable.Empty<int>()).Concat(rows).ToList();
        return table with
        {
            Rows = chosen.Select(r => table.Rows[r]).ToList(),
            Highlighted = chosen.Select(r => r < table.Highlighted.Count && table.Highlighted[r]).ToList(),
        };
    }

    // The blocks of a flow slide in the units a split may fall between: paragraph groups, table rows (each with the
    // header), the items of rows and labelled tags; every other block whole. `Origin` ties the pieces of one block.
    private static IEnumerable<List<Piece>> FlowUnits(IReadOnlyList<FlowBlock> blocks)
    {
        for (var origin = 0; origin < blocks.Count; origin++)
        {
            var o = origin;
            switch (blocks[origin])
            {
                case TextBlock text:
                    foreach (var group in ListGroups(text.Paragraphs))
                    {
                        yield return [new Piece(new TextBlock(group), o)];
                    }

                    break;
                case RowsBlock rows:
                    foreach (var row in rows.Rows)
                    {
                        yield return [new Piece(new RowsBlock([row]), o)];
                    }

                    break;
                case LabelledTagsBlock tags:
                    foreach (var item in tags.Items)
                    {
                        yield return [new Piece(new LabelledTagsBlock([item]), o)];
                    }

                    break;
                case TableBlock { Table: var table } when table.Rows.Count - (table.HasHeader ? 1 : 0) >= 2:
                    for (var r = table.HasHeader ? 1 : 0; r < table.Rows.Count; r++)
                    {
                        yield return [new Piece(new TableBlock(Subset(table, [r])), o)];
                    }

                    break;
                default:
                    yield return [new Piece(blocks[origin], o)];
                    break;
            }
        }
    }

    // Pieces back into blocks: neighbouring pieces of one block joined.
    private static List<FlowBlock> Combine(IReadOnlyList<Piece> pieces)
    {
        var blocks = new List<FlowBlock>();
        Piece? last = null;
        foreach (var piece in pieces)
        {
            if (last is { } previous && previous.Origin == piece.Origin)
            {
                blocks[^1] = (blocks[^1], piece.Block) switch
                {
                    (TextBlock a, TextBlock b) => new TextBlock([.. a.Paragraphs, .. b.Paragraphs]),
                    (RowsBlock a, RowsBlock b) => new RowsBlock([.. a.Rows, .. b.Rows]),
                    (LabelledTagsBlock a, LabelledTagsBlock b) => new LabelledTagsBlock([.. a.Items, .. b.Items]),
                    (TableBlock a, TableBlock b) => new TableBlock(a.Table with
                    {
                        Rows = [.. a.Table.Rows, .. b.Table.Rows.Skip(b.Table.HasHeader ? 1 : 0)],
                        Highlighted = [.. a.Table.Highlighted, .. b.Table.Highlighted.Skip(b.Table.HasHeader ? 1 : 0)],
                    }),
                    var (_, other) => other,
                };
            }
            else
            {
                blocks.Add(piece.Block);
            }

            last = piece;
        }

        return blocks;
    }

    // The size of level-1 text in the content layout's body: its own list style, else the master's body style.
    private static int ReadBodySize(SlideLayoutPart layout)
    {
        var own = Placeholders(layout).FirstOrDefault(IsBody)?.Parent?.Parent?.Parent?
            .Descendants<DocumentFormat.OpenXml.Drawing.Level1ParagraphProperties>().FirstOrDefault()?
            .GetFirstChild<DocumentFormat.OpenXml.Drawing.DefaultRunProperties>()?.FontSize?.Value;
        var master = layout.SlideMasterPart?.SlideMaster?.TextStyles?.BodyStyle?.Level1ParagraphProperties?
            .GetFirstChild<DocumentFormat.OpenXml.Drawing.DefaultRunProperties>()?.FontSize?.Value;
        return own ?? master ?? TextSize;
    }

    private sealed record Piece(FlowBlock Block, int Origin);
}
