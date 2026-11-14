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

    /// <summary>
    /// The slide as it is written, with the step it is shrunk by; or its parts, when it had to be split. Each says
    /// whether it fits at that step, and the notes say what was done.
    /// </summary>
    private List<(Slide Slide, int Step)> Fit(Slide slide)
    {
        // Measured with room for all the slide's footnotes; each part then keeps those it references.
        this.footnotes = slide.Footnotes;
        var (parts, maxStep) = slide switch
        {
            ContentSlide content => (this.FitContent(content).ToList(), this.MaxContentStep),
            VisualSlide visual => (this.FitVisual(visual).ToList(), MaxVisualStep),
            FlowSlide flow => (this.FitFlow(flow).ToList(), MaxFlowStep),
            PlaceholderSlide placeholders => (this.FitPlaceholders(placeholders).ToList(), 0),
            KindSlide kind => (this.FitKind(kind), 0),
            _ => ([(slide, 0, true)], 0),
        };

        var title = Plain(slide.Title);
        if (parts.Count > 1)
        {
            this.notes.Add(
                NoteCodes.SlideSplit,
                slide.Line,
                $"The slide '{title}' did not fit and continues on {parts.Count - 1} more slide{(parts.Count > 2 ? "s" : string.Empty)}. Shorten it, or split it into slides with titles of their own.");
        }
        else if (maxStep > 0 && parts[0] is { Fits: true } only && only.Step == maxStep)
        {
            this.notes.Add(
                NoteCodes.SlideShrunk,
                slide.Line,
                $"The slide '{title}' was set at the smallest text size to fit. Shorten it.");
        }

        if (parts.Any(p => !p.Fits))
        {
            this.notes.Add(
                NoteCodes.SlideOverfull,
                slide.Line,
                $"The slide '{title}' runs past the content area even at the smallest size: one item does not fit on a slide of its own. Shorten it.");
        }

        // Every part keeps the slide's speaker notes and transition.
        return [.. parts.Select(p => (
            (p.Slide.Footnotes.Count == 0 && slide.Footnotes.Count > 0 ? WithFootnotes(p.Slide, slide.Footnotes) : p.Slide) with
            {
                SpeakerNotes = slide.SpeakerNotes,
                Transition = slide.Transition,
            },
            p.Step))];
    }

    private static Slide WithFootnotes(Slide part, IReadOnlyList<SlideFootnote> footnotes)
    {
        var references = part.References();
        return part with { Footnotes = [.. footnotes.Where(f => references.Contains(f.Number))] };
    }

    private static string Plain(IReadOnlyList<TextRun> runs) => string.Concat(runs.Select(r => r.IsBreak ? " " : r.Text)).Trim();

    // The text size of text slides, from the template's body style, shrunk by `step`.
    private int BodySize(int step) => Math.Max(this.bodySize - (step * SizeStep), Math.Min(MinTextSize, this.bodySize));

    private int MaxContentStep => Math.Max((this.bodySize - MinTextSize) / SizeStep, 0);

    private IEnumerable<(Slide Slide, int Step, bool Fits)> FitContent(ContentSlide slide)
    {
        var area = this.ContentArea(this.LayoutOf(slide, this.contentLayout));
        long Height(IReadOnlyList<TextParagraph> body, int step) => this.levelSizes is { } sizes
            ? body.Sum(p => EstimateHeight([p], sizes[MappedLevel(p, this.roles.Declared.TextLevels!)] * this.BodySize(step) / this.bodySize, area.W) - CellInsets) + CellInsets
            : EstimateHeight(body, this.BodySize(step), area.W);
        int? Step(IReadOnlyList<TextParagraph> body) =>
            Enumerable.Range(0, this.MaxContentStep + 1).Cast<int?>().FirstOrDefault(s => Height(body, s!.Value) <= area.H);

        if (Step(slide.Body) is { } fits)
        {
            return [(slide, fits, true)];
        }

        var parts = Pack(ListGroups(slide.Body), body => Height(body, this.MaxContentStep) <= area.H);
        return parts.Select((body, i) =>
        {
            var step = Step(body);
            return ((Slide)new ContentSlide(this.Continued(slide.Title, i), body) { Layout = slide.Layout, Line = slide.Line }, step ?? this.MaxContentStep, step is not null);
        });
    }

    private IEnumerable<(Slide Slide, int Step, bool Fits)> FitVisual(VisualSlide slide)
    {
        var area = this.ContentArea(this.LayoutOf(slide, this.titleOnlyLayout));
        int? Step(VisualSlide part) =>
            Enumerable.Range(0, MaxVisualStep + 1).Cast<int?>().FirstOrDefault(s => this.VisualHeight(part, s!.Value, area.W) <= area.H);

        if (Step(slide) is { } fits)
        {
            return [(slide, fits, true)];
        }

        if (slide.Table is not { } table || table.Rows.Count - (table.HasHeader ? 1 : 0) < 2)
        {
            return [(slide, MaxVisualStep, false)];
        }

        // Rows split across slides, the header row on each; text above on the first, below on the last.
        var first = table.HasHeader ? 1 : 0;
        var rows = Enumerable.Range(first, table.Rows.Count - first).Select(r => new List<int> { r }).ToList();
        VisualSlide Part(List<int> chosen, bool isFirst, bool isLast, int index) => new(
            this.Continued(slide.Title, index),
            isFirst ? slide.Above : [],
            Subset(table, chosen),
            null,
            isLast ? slide.Below : []) { Layout = slide.Layout, Line = slide.Line, VisualBuild = slide.VisualBuild };
        var parts = Pack(rows, chosen => this.VisualHeight(Part(chosen, chosen[0] == first, false, 0), MaxVisualStep, area.W) <= area.H);
        return parts.Select((chosen, i) =>
        {
            var part = Part(chosen, i == 0, i == parts.Count - 1, i);
            var step = Step(part);
            return ((Slide)part, step ?? MaxVisualStep, step is not null);
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

    private IEnumerable<(Slide Slide, int Step, bool Fits)> FitFlow(FlowSlide slide)
    {
        var area = this.ContentArea(this.FlowLayout(slide));
        long Height(IReadOnlyList<FlowBlock> blocks, int step) => this.Stack(null, blocks, area, this.FlowStyleOf(slide, step));
        int? Step(IReadOnlyList<FlowBlock> blocks) =>
            Enumerable.Range(0, MaxFlowStep + 1).Cast<int?>().FirstOrDefault(s => Height(blocks, s!.Value) <= area.H);

        if (Step(slide.Blocks) is { } fits)
        {
            return [(slide, fits, true)];
        }

        var parts = Pack(FlowUnits(slide.Blocks), pieces => Height(Combine(pieces), MaxFlowStep) <= area.H);
        return parts.Select((pieces, i) =>
        {
            var blocks = Combine(pieces);
            var step = Step(blocks);
            return ((Slide)new FlowSlide(this.Continued(slide.Title, i), slide.Kicker, slide.Dark, blocks) { Layout = slide.Layout, Line = slide.Line }, step ?? MaxFlowStep, step is not null);
        });
    }

    // Surplus parts and visuals continue on further slides on the same layout; each text part is shrunk in its own
    // placeholder, and never split, since its `---` is the writer's cut.
    private IEnumerable<(Slide Slide, int Step, bool Fits)> FitPlaceholders(PlaceholderSlide slide)
    {
        var layout = this.LayoutOf(slide, this.contentLayout);
        var slots = this.SlotsOf(layout);
        var parts = PlaceholderFill.Distribute(slide.Items, [.. slots.Select(s => s.Kind)])
            ?? throw new wwosik.DocLibs.DocumentConversions.DocumentConversionException(
                wwosik.DocLibs.DocumentConversions.DocumentFormat.Pptx,
                $"The layout '{LayoutName(layout)}' has no placeholder for {PlaceholderFill.Unplaceable(slide.Items, [.. slots.Select(s => s.Kind)])!.Kind} (the slide '{Plain(slide.Title)}'). "
                + "Choose a layout that has one, or move it to a slide of its own.");
        return parts.Select((part, i) => (
            (Slide)new PlaceholderSlide(this.Continued(slide.Title, i), [.. part.Select(p => p.Item)]) { Layout = slide.Layout, Line = slide.Line },
            0,
            part.All(p => p.Item.Text is not { } text || this.PlaceholderFit(layout, slots[p.Slot].Shape, text).Fits)));
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
    // header), the items of rows, labelled tags and icon lists; every other block whole. `Origin` ties the pieces of one block.
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
                        yield return [new Piece(text with { Paragraphs = group }, o)];
                    }

                    break;
                case RowsBlock rows:
                    foreach (var row in rows.Rows)
                    {
                        yield return [new Piece(rows with { Rows = [row] }, o)];
                    }

                    break;
                case LabelledTagsBlock tags:
                    foreach (var item in tags.Items)
                    {
                        yield return [new Piece(tags with { Items = [item] }, o)];
                    }

                    break;
                case IconListBlock icons:
                    foreach (var item in icons.Items)
                    {
                        yield return [new Piece(icons with { Items = [item] }, o)];
                    }

                    break;
                case TableBlock { Table: var table } whole when table.Rows.Count - (table.HasHeader ? 1 : 0) >= 2:
                    for (var r = table.HasHeader ? 1 : 0; r < table.Rows.Count; r++)
                    {
                        yield return [new Piece(whole with { Table = Subset(table, [r]) }, o)];
                    }

                    break;
                default:
                    yield return [new Piece(blocks[origin], o)];
                    break;
            }
        }
    }

    // Pieces back into blocks: neighbouring pieces of one block joined, with its build marker.
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
                    (TextBlock a, TextBlock b) => a with { Paragraphs = [.. a.Paragraphs, .. b.Paragraphs] },
                    (RowsBlock a, RowsBlock b) => a with { Rows = [.. a.Rows, .. b.Rows] },
                    (LabelledTagsBlock a, LabelledTagsBlock b) => a with { Items = [.. a.Items, .. b.Items] },
                    (IconListBlock a, IconListBlock b) => a with { Items = [.. a.Items, .. b.Items] },
                    (TableBlock a, TableBlock b) => a with
                    {
                        Table = a.Table with
                        {
                            Rows = [.. a.Table.Rows, .. b.Table.Rows.Skip(b.Table.HasHeader ? 1 : 0)],
                            Highlighted = [.. a.Table.Highlighted, .. b.Table.Highlighted.Skip(b.Table.HasHeader ? 1 : 0)],
                        },
                    },
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
    private int ReadBodySize(SlideLayoutPart layout) => this.LevelSize(layout, this.Bodies(layout).FirstOrDefault(), 1) ?? TextSize;

    // The sizes of the nine outline levels of the content layout's body; a level without one takes the level above's.
    private int[] ReadLevelSizes(SlideLayoutPart layout)
    {
        var sizes = new int[9];
        for (var level = 1; level <= 9; level++)
        {
            sizes[level - 1] = this.LevelSize(layout, this.Bodies(layout).FirstOrDefault(), level) ?? (level == 1 ? this.bodySize : sizes[level - 2]);
        }

        return sizes;
    }

    // The size of an outline level (1–9) in a placeholder of the layout: its own list style, else the master's body style.
    private int? LevelSize(SlideLayoutPart layout, DocumentFormat.OpenXml.Presentation.PlaceholderShape? placeholder, int level)
    {
        static int? Size(DocumentFormat.OpenXml.OpenXmlElement? styles, int level) =>
            styles?.ChildElements.FirstOrDefault(e => e.LocalName == $"lvl{level}pPr")?
                .GetFirstChild<DocumentFormat.OpenXml.Drawing.DefaultRunProperties>()?.FontSize?.Value;

        var own = placeholder?.Parent?.Parent?.Parent?.Descendants<DocumentFormat.OpenXml.Drawing.ListStyle>().FirstOrDefault();
        return Size(own, level) ?? Size(layout.SlideMasterPart?.SlideMaster?.TextStyles?.BodyStyle, level);
    }

    private sealed record Piece(FlowBlock Block, int Origin);
}
