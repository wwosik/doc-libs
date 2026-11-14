using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// Slide kinds (<c>{.agenda}</c>, <c>{.quote}</c>, <c>{.statement}</c>, <c>{.contact}</c>, <c>{.chart}</c>) on the
/// template's layout for each, or as flow slides where it has none; two columns of text on a declared two-content
/// layout; and the flow blocks <c>::: conclusion</c>, <c>::: comment</c> and a quote's byline.
/// </summary>
internal sealed partial class DeckWriter
{
    // 2.5 inches: the narrowest a comment's callout gets.
    private const long MinCommentWidth = 2286000;

    // A flow slide of nothing but two columns of text, on a template whose brand pack or options declare a two-content
    // layout: its columns in that layout's first two text placeholders. Anything else stays as it is.
    private Slide Resolve(Slide slide)
    {
        if (slide is not FlowSlide { Kicker: null, Dark: false, Layout: null, NoTitle: false, Blocks: [ColumnsBlock { Columns: [var left, var right] } columns] } flow
            || this.builtIn
            || this.roles.Declared.Layouts?.TwoContent is null
            || !left.Concat(right).All(b => b is TextBlock)
            || this.roles.Layout(LayoutRole.TwoContent) is not { } layout
            || this.SlotsOf(layout).Count(s => s.Kind is SlotKind.Text or SlotKind.Object) < 2)
        {
            return slide;
        }

        PlaceholderItem Column(IReadOnlyList<FlowBlock> blocks, int index) => new(
            [.. blocks.Cast<TextBlock>().SelectMany(t => t.Paragraphs)
                .Select(p => p.Build is null && columns.Build is { } mark ? p with { Build = mark with { Item = index } } : p)],
            null);
        return new PlaceholderSlide(flow.Title, [Column(left, 0), Column(right, 1)])
        {
            Layout = LayoutName(layout),
            Line = flow.Line,
            Footnotes = flow.Footnotes,
            SpeakerNotes = flow.SpeakerNotes,
            Transition = flow.Transition,
        };
    }

    // The layout of the slide's kind: the one its heading names, else the template's for the role (two charts for two);
    // null when the template has none.
    private SlideLayoutPart? KindLayout(KindSlide slide) =>
        slide.Layout is not null ? this.LayoutOf(slide, this.contentLayout) : this.roles.Layout(slide switch
        {
            AgendaSlide => LayoutRole.Agenda,
            QuoteSlide => LayoutRole.Quote,
            StatementSlide => LayoutRole.Statement,
            ContactSlide => LayoutRole.Contact,
            ChartSlide { Charts.Count: >= 2 } => LayoutRole.TwoCharts,
            _ => LayoutRole.Chart,
        });

    private static string KindName(KindSlide slide) => slide switch
    {
        AgendaSlide => "an agenda",
        QuoteSlide => "a quote",
        StatementSlide => "a statement",
        ContactSlide => "a contact",
        ChartSlide { Charts.Count: >= 2 } => "a two-chart",
        _ => "a chart",
    };

    // People five a slide (or as many as the layout has bodies), charts two a slide (one where the layout takes only
    // one); each part on its layout, or as a flow slide where the template lacks it, which a note says. Every part after
    // the first gets the continuation suffix.
    private List<(Slide Slide, int Step, bool Fits)> FitKind(KindSlide slide)
    {
        var parts = new List<(Slide Slide, int Step, bool Fits)>();
        var fallback = new List<string>();
        foreach (var piece in this.Pieces(slide))
        {
            if (this.KindLayout(piece) is { } layout)
            {
                parts.AddRange(this.FitOnLayout(piece, layout));
            }
            else
            {
                fallback.Add(KindName(piece));
                parts.AddRange(this.FitFlow(this.Fallback(piece)));
            }
        }

        foreach (var kind in fallback.Distinct())
        {
            this.notes.Add(
                NoteCodes.LayoutFallback,
                slide.Line,
                $"The slide '{Plain(slide.Title)}' asks for {kind} layout, which the template does not have; it is drawn as a flow slide.");
        }

        return [.. parts.Select((p, i) => (p.Slide with { Title = this.Continued(slide.Title, i) }, p.Step, p.Fits))];
    }

    private IEnumerable<KindSlide> Pieces(KindSlide slide)
    {
        switch (slide)
        {
            case ContactSlide contact when this.KindLayout(contact) is { } layout:
                var perSlide = Math.Clamp(this.roles.Placeholders(layout).Bodies.Count, 1, SlideKinds.MaxPeople);
                foreach (var people in contact.People.Chunk(perSlide))
                {
                    yield return contact with { People = people };
                }

                break;
            case ChartSlide chart:
                foreach (var (index, charts) in chart.Charts.Chunk(SlideKinds.MaxCharts).Index())
                {
                    var piece = chart with { Charts = charts, Text = index == 0 ? chart.Text : [] };
                    if (charts.Length > 1 && this.KindLayout(piece) is { } layout
                        && this.SlotsOf(layout).Count(s => s.Kind is SlotKind.Chart or SlotKind.Object) < charts.Length)
                    {
                        yield return piece with { Charts = [charts[0]] };
                        yield return piece with { Charts = [charts[1]], Text = [] };
                    }
                    else
                    {
                        yield return piece;
                    }
                }

                break;
            default:
                yield return slide;
                break;
        }
    }

    // Each text in its placeholder is shrunk on its own when written; an agenda too long for its body continues between
    // its items, numbered on.
    private IEnumerable<(Slide Slide, int Step, bool Fits)> FitOnLayout(KindSlide slide, SlideLayoutPart layout)
    {
        var found = this.roles.Placeholders(layout);
        var body = found.Bodies.FirstOrDefault();
        bool Fits(P.PlaceholderShape? placeholder, IReadOnlyList<TextParagraph> text) =>
            placeholder is null || text.Count == 0 || this.PlaceholderFit(layout, placeholder, text).Fits;

        switch (slide)
        {
            case AgendaSlide agenda when body is not null && !Fits(body, agenda.Items):
                var parts = Pack(ListGroups(agenda.Items), part => Fits(body, part));
                var done = 0;
                var start = agenda.Items.FirstOrDefault(p => p is { Level: 0, Kind: ParagraphKind.Numbered })?.NumberStart ?? 1;
                foreach (var (index, part) in parts.Index())
                {
                    var numbered = index == 0 ? part
                        : [.. part.Select(p => p is { Level: 0, Kind: ParagraphKind.Numbered } ? p with { NumberStart = start + done } : p)];
                    done += part.Count(p => p is { Level: 0, Kind: ParagraphKind.Numbered });
                    yield return (agenda with { Items = numbered }, 0, Fits(body, numbered));
                }

                yield break;
            case QuoteSlide quote:
                yield return (quote, 0, Fits(body, found.Byline is null ? this.QuoteText(quote) : quote.Quote));
                yield break;
            case StatementSlide statement:
                yield return (statement, 0, Fits(body, statement.Text));
                yield break;
            case ContactSlide contact:
                yield return (contact, 0, contact.People.Select((p, i) => Fits(found.Bodies.ElementAtOrDefault(i), PersonText(p))).All(f => f));
                yield break;
            default:
                yield return (slide, 0, true);
                yield break;
        }
    }

    // The quote, with its byline as a last paragraph when the layout has no place of its own for it.
    private List<TextParagraph> QuoteText(QuoteSlide quote) =>
        quote.Byline is { } byline ? [.. quote.Quote, new TextParagraph(byline)] : [.. quote.Quote];

    // The name in bold, the person's other lines one level down (the built-in contact layout's smaller text).
    private static List<TextParagraph> PersonText(Person person) =>
        [new TextParagraph(Bold(person.Name)), .. person.Lines.Select(l => l with { Level = 1 })];

    // The same content as a flow slide, on the title-only layout.
    private FlowSlide Fallback(KindSlide slide)
    {
        FlowSlide Flow(IReadOnlyList<FlowBlock> blocks, bool noTitle = false) => new(slide.Title, slide.Kicker, false, blocks)
        {
            Line = slide.Line,
            Footnotes = slide.Footnotes,
            SpeakerNotes = slide.SpeakerNotes,
            Transition = slide.Transition,
            NoTitle = noTitle,
        };

        return slide switch
        {
            AgendaSlide agenda => Flow(agenda.Items.Count == 0 ? [] : [new TextBlock(agenda.Items)]),
            QuoteSlide quote => Flow(
                [new StatementBlock([.. quote.Quote.SelectMany((p, i) => i == 0 ? p.Runs : [TextRun.Break, .. p.Runs])]), .. quote.Byline is { } byline ? [new BylineBlock(byline)] : Array.Empty<FlowBlock>()],
                noTitle: quote.Title.Count == 0),
            StatementSlide statement => Flow(
                [new StatementBlock(statement.Title), .. statement.Text.Count > 0 ? [new TextBlock(statement.Text)] : Array.Empty<FlowBlock>()],
                noTitle: true),
            ContactSlide contact => Flow([new CardsBlock([.. contact.People.Select(p => new Card(p.Name, null, p.Lines.Count > 0 ? [new TextBlock(p.Lines)] : []) { Photo = p.Photo })])
            {
                Build = contact.People.FirstOrDefault()?.Build is { } mark ? mark with { Item = 0 } : null,
            }]),
            ChartSlide chart => Flow([
                .. chart.Text.Count > 0 ? [new TextBlock(chart.Text)] : Array.Empty<FlowBlock>(),
                chart.Charts.Count == 1 ? chart.Charts[0] : new ColumnsBlock([1, 1], [[chart.Charts[0]], [chart.Charts[1]]]),
            ]),
            _ => throw new InvalidOperationException("Unknown kind of slide."),
        };
    }

    // The slide on its kind's layout, its content in the layout's placeholders as the table in docs/ says.
    private Canvas WriteKindSlide(KindSlide slide)
    {
        var layout = this.KindLayout(slide)!;
        var canvas = this.NewSlide(layout);
        var found = this.roles.Placeholders(layout);
        var body = found.Bodies.FirstOrDefault();
        var titleBox = slide.Kicker is { } kicker ? this.Kicker(canvas, layout, kicker, white: false) : (Rect?)null;

        // A quote's heading only where the layout has a title placeholder; every other kind always has its title.
        if (slide is not QuoteSlide || (slide.Title.Count > 0 && FindPlaceholder(layout, IsTitle) is not null))
        {
            this.Title(canvas, slide.Title, null, titleBox);
        }

        switch (slide)
        {
            case AgendaSlide agenda:
                this.Fill(canvas, layout, body, agenda.Items, "Agenda");
                break;
            case QuoteSlide quote when found.Byline is { } byline && quote.Byline is { } text:
                this.Fill(canvas, layout, body, quote.Quote, "Quote");
                this.Fill(canvas, layout, byline, [new TextParagraph(text)], "Byline");
                break;
            case QuoteSlide quote:
                this.Fill(canvas, layout, body, this.QuoteText(quote), "Quote");
                break;
            case StatementSlide statement:
                this.Fill(canvas, layout, body, statement.Text, "Text");
                break;
            case ContactSlide contact:
                foreach (var (index, person) in contact.People.Index())
                {
                    canvas.Building(person.Build, () =>
                    {
                        if (person.Photo is { } photo && found.Pictures.ElementAtOrDefault(index) is { } picture)
                        {
                            this.PicturePlaceholder(canvas, photo, picture, this.PlaceholderBox(layout, picture), LayoutName(layout));
                        }

                        this.Fill(canvas, layout, found.Bodies.ElementAtOrDefault(index), PersonText(person), "Person");
                        return 0;
                    });
                }

                break;
            case ChartSlide chart:
                this.KindCharts(canvas, layout, found, chart);
                break;
        }

        return canvas;
    }

    // Text into a placeholder, shrunk to fit like a placeholder slide's; without one, a text box in the content area.
    private void Fill(Canvas canvas, SlideLayoutPart layout, P.PlaceholderShape? placeholder, IReadOnlyList<TextParagraph> paragraphs, string name)
    {
        if (paragraphs.Count == 0)
        {
            return;
        }

        if (placeholder is null)
        {
            var area = this.ContentArea(layout);
            this.TextBox(canvas, area with { H = EstimateHeight(paragraphs, TextSize, area.W) }, paragraphs, TextSize, center: false);
            return;
        }

        var (step, scale, _) = this.PlaceholderFit(layout, placeholder, paragraphs);
        var properties = step == 0 ? null : new A.BodyProperties(new A.NormalAutoFit { FontScale = scale });
        canvas.AddText(Placeholder(canvas, placeholder, name, paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: this.listBullets, null)), null, properties), paragraphs);
    }

    // One chart's title and unit go into the layout's chart-title and unit placeholders, and the sources of all its charts
    // into its source placeholder, joined by "; ", instead of into the charts. The charts fill chart placeholders (else
    // `obj` bodies), the text a text placeholder left over, else a text box above the charts, which move down for it.
    private void KindCharts(Canvas canvas, SlideLayoutPart layout, LayoutPlaceholders found, ChartSlide slide)
    {
        var charts = slide.Charts.Select(c => c.Chart).ToList();
        void Note(P.PlaceholderShape placeholder, string text, string name) =>
            this.Fill(canvas, layout, placeholder, [new TextParagraph([new TextRun(text, RunStyle.None)])], name);

        if (charts.Count == 1 && found.ChartTitle is { } titlePlaceholder && charts[0].Title is { } title)
        {
            Note(titlePlaceholder, title, "Chart Title");
            charts[0] = charts[0] with { Title = null };
        }

        if (charts.Count == 1 && found.ChartUnit is { } unitPlaceholder && charts[0].Unit is { } unit)
        {
            Note(unitPlaceholder, unit, "Chart Unit");
            charts[0] = charts[0] with { Unit = null };
        }

        if (found.ChartSource is { } sourcePlaceholder && charts.Select(c => c.Source).OfType<string>().ToList() is { Count: > 0 } sources)
        {
            Note(sourcePlaceholder, $"{this.texts.Source}: {string.Join("; ", sources)}", "Chart Source");
            charts = [.. charts.Select(c => c with { Source = null })];
        }

        var slots = this.SlotsOf(layout);
        var items = charts.Select((c, i) => new PlaceholderItem(null, new PlaceholderVisual(null, null, null, c) { Build = slide.Charts[i].Build })).ToList();
        var placed = PlaceholderFill.Distribute(items, [.. slots.Select(s => s.Kind)])?.FirstOrDefault() ?? [];
        var textSlot = Enumerable.Range(0, slots.Count).Cast<int?>()
            .FirstOrDefault(i => slots[i!.Value].Kind is SlotKind.Text or SlotKind.Object && !placed.Any(p => p.Slot == i));
        long shift = 0;
        if (textSlot is { } slot)
        {
            this.Fill(canvas, layout, slots[slot].Shape, slide.Text, "Text");
        }
        else if (slide.Text.Count > 0)
        {
            var boxes = placed.Select(a => this.PlaceholderBox(layout, slots[a.Slot].Shape)).DefaultIfEmpty(this.ContentArea(layout)).ToList();
            var (left, right) = (boxes.Min(b => b.X), boxes.Max(b => b.Right));
            var height = EstimateHeight(slide.Text, FlowTextSize, right - left);
            this.TextBox(canvas, new Rect(left, boxes.Min(b => b.Y), right - left, height), slide.Text, FlowTextSize, center: false);
            shift = height + Gap;
        }

        foreach (var (item, index) in placed)
        {
            var (shape, kind) = slots[index];
            var box = this.PlaceholderBox(layout, shape);
            box = box with { Y = box.Y + shift, H = Math.Max(box.H - shift, box.H / 2) };
            var chart = item.Visual!.Chart!;
            canvas.Building(item.Visual.Build, () => kind == SlotKind.Chart ? this.Chart(canvas, chart, box, shape) : this.Chart(canvas, chart, box));
        }
    }

    // A band across the box: the surface colour with a rule on top in the primary colour (the highlight on dark), its
    // text bold in the heading colour.
    private long Conclusion(Canvas? canvas, ConclusionBlock block, Rect box, FlowStyle style)
    {
        var rule = 2 * RuleWidth;
        var paragraphs = block.Paragraphs.Select(p => p with { Runs = Bold(p.Runs) }).ToList();
        var inner = new Rect(box.X + Pad, box.Y + rule + RowPad, box.W - (2 * Pad), 0);
        var height = FlowHeight(paragraphs, style.Size, inner.W) + rule + (2 * RowPad);
        if (canvas is not null)
        {
            canvas.Add(Shape(canvas, box with { H = height }, A.ShapeTypeValues.Rectangle, this.Surface(style), null));
            canvas.Add(Shape(canvas, box with { H = rule }, A.ShapeTypeValues.Rectangle, style.Dark ? this.palette.Highlight : this.palette.Primary, null));
            this.FlowText(canvas, paragraphs, inner, style.Size, this.Heading(style));
        }

        return height;
    }

    // The block, then its comment: a rounded callout a third of the box wide (at least 2.5 inches), at the block's top
    // right and raised by a third of its own height, pointing towards the block's centre; inside the content area, and
    // taking no height of its own.
    private long Comment(Canvas? canvas, CommentBlock block, Rect box, FlowStyle style)
    {
        var height = this.Place(canvas, block.Target, box, style);
        if (canvas is null)
        {
            return height;
        }

        var size = style.Scaled(FlowSmallSize);
        var width = Math.Min(Math.Max(box.W / 3, MinCommentWidth), box.W);
        var calloutHeight = FlowHeight(block.Paragraphs, size, width - (2 * Pad)) + (2 * RowPad);
        var area = this.flowArea ?? box;
        var x = Math.Max(box.Right - width, area.X);
        var y = Math.Max(Math.Min(box.Y - (calloutHeight / 3), area.Bottom - calloutHeight), area.Y);

        // The tip towards the block's centre, at most half the callout's width plus its height from the callout's centre.
        var (cx, cy) = (x + (width / 2.0), y + (calloutHeight / 2.0));
        var (dx, dy) = (box.X + (box.W / 2.0) - cx, box.Y + (height / 2.0) - cy);
        var reach = (width / 2.0) + calloutHeight;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length > reach)
        {
            (dx, dy) = (dx * reach / length, dy * reach / length);
        }

        var fill = style.Dark && !this.palette.DarkSlides ? this.palette.Highlight.Translucent(25) : this.palette.HighlightSurface;
        var paragraphs = block.Paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, size, color: style.Text)).ToList();
        canvas.Building(block.Build ?? block.Target.Build, () =>
        {
            var id = canvas.NextId();
            var geometry = new A.PresetGeometry(new A.AdjustValueList(
                new A.ShapeGuide { Name = "adj1", Formula = $"val {(long)(dx * 100000 / width)}" },
                new A.ShapeGuide { Name = "adj2", Formula = $"val {(long)(dy * 100000 / calloutHeight)}" },
                new A.ShapeGuide { Name = "adj3", Formula = "val 16667" })) { Preset = A.ShapeTypeValues.WedgeRoundRectangleCallout };
            var shape = new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = id, Name = "Comment " + id },
                    new P.NonVisualShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(new Rect(x, y, width, calloutHeight).Transform(), geometry, fill.Fill(), new A.Outline(this.palette.Highlight.Fill()) { Width = 9525 }),
                new P.TextBody(
                    new DocumentFormat.OpenXml.OpenXmlElement[]
                    {
                        new A.BodyProperties(new A.NoAutoFit())
                        {
                            Wrap = A.TextWrappingValues.Square,
                            LeftInset = (int)Pad,
                            RightInset = (int)Pad,
                            TopInset = (int)RowPad,
                            BottomInset = (int)RowPad,
                            Anchor = A.TextAnchoringTypeValues.Center,
                        },
                        new A.ListStyle(),
                    }.Concat(paragraphs)));
            canvas.AddText(shape, block.Paragraphs);
            return 0;
        });

        return height;
    }

    // A quote's byline under it, right-aligned.
    private long Byline(Canvas? canvas, BylineBlock block, Rect box, FlowStyle style)
    {
        var paragraph = new TextParagraph(block.Text);
        var height = FlowHeight([paragraph], style.Size, box.W);
        canvas?.AddText(TextBoxShape(canvas, box with { H = height }, [this.Paragraph(canvas, paragraph, explicitBullets: true, style.Size, CellAlignment.Right, color: style.Text)]), [paragraph]);
        return height;
    }
}
