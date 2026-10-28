using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2019.Drawing.SVG;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using A = DocumentFormat.OpenXml.Drawing;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>Writes slides into an opened template whose own slides it removes first.</summary>
internal sealed partial class DeckWriter
{
    private const long EmuPerPoint = 12700;
    private const long EmuPerPixel = 9525;

    // The extension PowerPoint 2016 and later read an SVG picture from, beside its PNG.
    private const string SvgBlipUri = "{96DAC541-7B7A-43D3-8B79-37D633B846F1}";
    private const long BulletIndent = 285750;
    private const long Gap = 152400;
    private const long MinRowHeight = 370840;
    private const long CellInsets = 91440 * 2;
    private const int MinTableSize = 1000;
    private const double MaxDrawingUpscale = 1.5;
    private const int MaxImagesPerRow = 4;
    private const int TextSize = 1800;
    private const int CaptionSize = 1400;

    // Flow slides: sizes in hundredths of a point, distances in EMU.
    private const int FlowTextSize = 1600;
    private const int FlowSmallSize = 1400;
    private const int KickerSize = 1200;
    private const int CardTitleSize = 1600;
    private const int FigureSize = 3200;
    private const int StatementSize = 2600;
    private const int TagSize = 1200;
    private const long Pad = 137160;
    private const long RowPad = 64008;
    private const long RuleWidth = 12700;
    private const long BarWidth = 54864;
    private const long TextInsets = 91440;
    private const long ChipPadX = 64008;
    private const long ChipPadY = 27432;
    private const long ChipGap = 64008;
    private const int MaxCardsPerRow = 4;
    private const int CodeLineSpacing = 130000;
    private const string SectionListExtension = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";
    private const string TableGraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/table";
    private const string ChartGraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // Built into PowerPoint ("Medium Style 2 - Accent 1"), so it needs no definition; for templates without a default.
    private const string BuiltInTableStyleId = "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}";

    private readonly PresentationPart presentationPart;
    private readonly P.Presentation presentation;
    private readonly PptxOptions options;
    private readonly string? date;
    private readonly string? tableFont;
    private readonly string codeFont;
    private readonly long slideWidth;
    private readonly long slideHeight;
    private readonly string tableStyleId;
    private readonly SlideLayoutPart titleLayout;
    private readonly SlideLayoutPart contentLayout;
    private readonly SlideLayoutPart titleOnlyLayout;
    private readonly SlideLayoutPart sectionLayout;
    private readonly SlideLayoutPart? darkLayout;
    private readonly SlideLayoutPart closingLayout;
    private readonly List<SlideLayoutPart> layouts;
    private readonly string? footerText;
    private readonly bool builtIn;
    private readonly bool listBullets;
    private readonly SlidePalette palette;
    private readonly int bodySize;
    private readonly Dictionary<DocumentResource, ImagePart> images = [];
    private readonly Dictionary<DocumentResource, ImagePart> vectors = [];
    private readonly IReadOnlyDictionary<string, LabelCategory> labels;
    private uint nextSlideId = 256;

    /// <summary>Opens the deck for writing. Layouts named in <paramref name="names"/> win over the lookup by type.</summary>
    /// <remarks>
    /// <paramref name="date"/> is the text for the date placeholder, or <see langword="null"/> for no date;
    /// <paramref name="tableFont"/> the family of table text, or <see langword="null"/> for the theme font;
    /// <paramref name="codeFont"/> the family of code, by default <see cref="PptxOptions.CodeFont"/> or Consolas;
    /// <paramref name="palette"/> the colours of flow slides, by default the template's theme colours;
    /// <paramref name="labels"/> the categories of status labels, from front matter.
    /// </remarks>
    public DeckWriter(
        PresentationDocument package,
        PptxOptions options,
        PptxLayoutNames? names,
        string? date,
        string? tableFont = null,
        SlidePalette? palette = null,
        IReadOnlyDictionary<string, LabelCategory>? labels = null,
        string? codeFont = null,
        string? footerText = null,
        bool builtIn = false,
        bool listBullets = false)
    {
        this.footerText = footerText;
        this.builtIn = builtIn;
        this.listBullets = listBullets;
        this.labels = labels ?? new Dictionary<string, LabelCategory>();
        this.options = options;
        this.date = date;
        this.tableFont = tableFont;
        this.codeFont = codeFont ?? options.CodeFont ?? PptxOptions.DefaultCodeFont;
        this.palette = palette ?? SlidePalette.Scheme;
        this.presentationPart = package.PresentationPart ?? throw Damaged("it has no presentation part.");
        this.presentation = this.presentationPart.Presentation ?? throw Damaged("ppt/presentation.xml is empty.");
        this.RemoveSlides();

        var layouts = this.layouts = this.Layouts();
        if (layouts.Count == 0)
        {
            throw Damaged("it has no slide layouts.");
        }

        this.contentLayout = ByName(layouts, names?.Content, nameof(names.Content))
            ?? ByType(layouts, P.SlideLayoutValues.Object) ?? ByType(layouts, P.SlideLayoutValues.Text)
            ?? layouts.FirstOrDefault(l => Placeholders(l).Any(IsTitle) && Placeholders(l).Any(IsBody)) ?? layouts[0];
        this.titleLayout = ByName(layouts, names?.Title, nameof(names.Title))
            ?? ByType(layouts, P.SlideLayoutValues.Title)
            ?? layouts.FirstOrDefault(l => Placeholders(l).Any(p => p.Type?.Value == P.PlaceholderValues.CenteredTitle)) ?? this.contentLayout;
        this.titleOnlyLayout = ByName(layouts, names?.TitleOnly, nameof(names.TitleOnly))
            ?? ByType(layouts, P.SlideLayoutValues.TitleOnly)
            ?? layouts.FirstOrDefault(l => Placeholders(l).Any(IsTitle) && !Placeholders(l).Any(IsBody)) ?? this.contentLayout;
        this.sectionLayout = ByName(layouts, names?.SectionHeader, nameof(names.SectionHeader))
            ?? ByType(layouts, P.SlideLayoutValues.SectionHeader) ?? this.titleLayout;
        this.darkLayout = layouts.FirstOrDefault(l => string.Equals(LayoutName(l), TemplateBuilder.DarkLayoutName, StringComparison.Ordinal));
        this.bodySize = ReadBodySize(this.contentLayout);
        this.closingLayout = ByName(layouts, names?.Closing, nameof(names.Closing))
            ?? layouts.FirstOrDefault(l => LayoutName(l) is { } name && ClosingNamePattern().IsMatch(name))
            ?? (builtIn ? this.sectionLayout : this.titleLayout);

        this.slideWidth = this.presentation.SlideSize?.Cx?.Value ?? 12192000;
        this.slideHeight = this.presentation.SlideSize?.Cy?.Value ?? 6858000;
        this.tableStyleId = this.presentationPart.TableStylesPart?.TableStyleList?.Default?.Value is { Length: > 0 } style
            ? style
            : BuiltInTableStyleId;
    }

    public void Write(IReadOnlyList<Slide> slides)
    {
        var number = 0;
        foreach (var (slide, step) in slides.SelectMany(this.Fit))
        {
            var canvas = slide switch
            {
                TitleSlide title => this.WriteTitleSlide(title),
                ContentSlide content => this.WriteContentSlide(content, step),
                VisualSlide visual => this.WriteVisualSlide(visual, step),
                FlowSlide flow => this.WriteFlowSlide(flow, step),
                _ => throw new InvalidOperationException("Unknown kind of slide."),
            };

            number++;
            if (slide is not TitleSlide { IsSection: false } and not TitleSlide { Closing: true })
            {
                this.Footer(canvas, number, this.PaintsDark(slide) ? SlideColor.White : null);
            }
        }

        if (this.presentation.SlideIdList is { HasChildren: false } empty)
        {
            empty.Remove();
        }
    }

    // A cover or section layout without a title placeholder (such as some corporate dividers) takes the title in its
    // first body placeholder and the subtitle in the next one.
    // The closing slide writes only into the placeholders its layout has: a closing layout often has none.
    private Canvas WriteTitleSlide(TitleSlide slide)
    {
        var layout = slide.Closing ? this.closingLayout : this.LayoutOf(slide, slide.IsSection ? this.sectionLayout : this.titleLayout);
        var canvas = this.NewSlide(layout);
        var bodies = Placeholders(layout).Where(IsBody).ToList();
        var titleInBody = FindPlaceholder(layout, IsTitle) is null && bodies.Count > 0;
        if (slide.Closing && FindPlaceholder(layout, IsTitle) is null && bodies.Count == 0)
        {
            return canvas;
        }

        this.Title(canvas, slide.Title, titleInBody ? bodies[0] : null);
        if (slide.Subtitle.Count == 0)
        {
            return canvas;
        }

        var subtitle = FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.SubTitle) ?? bodies.Skip(titleInBody ? 1 : 0).FirstOrDefault();
        if (subtitle is not null)
        {
            canvas.Add(Placeholder(canvas, subtitle, "Subtitle", slide.Subtitle.Select(p => this.Paragraph(canvas, p, explicitBullets: false, null))));
        }
        else if (!slide.Closing)
        {
            var title = (titleInBody ? Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, p => ReferenceEquals(p, bodies[0])) : null)
                ?? this.TitleArea(layout);
            this.TextBox(canvas, new Rect(title.X, title.Bottom + Gap, title.W, EstimateHeight(slide.Subtitle, TextSize, title.W)), slide.Subtitle, TextSize, center: false);
        }

        return canvas;
    }

    // Shrunk by `step`: the placeholder scales its text (normAutofit), so PowerPoint keeps it fitted when edited.
    // With `listBullets`, Markdown's bullets are written on every level, also where the template's level 1 has none.
    private Canvas WriteContentSlide(ContentSlide slide, int step)
    {
        var layout = this.LayoutOf(slide, this.contentLayout);
        var canvas = this.NewSlide(layout);
        this.Title(canvas, slide.Title);
        if (slide.Body.Count == 0)
        {
            return canvas;
        }

        if (FindPlaceholder(layout, IsBody) is { } body)
        {
            var properties = step == 0 ? null : new A.BodyProperties(new A.NormalAutoFit { FontScale = (int)(this.BodySize(step) * 100000L / this.bodySize) });
            canvas.Add(Placeholder(canvas, body, "Content", slide.Body.Select(p => this.Paragraph(canvas, p, explicitBullets: this.listBullets, null)), bodyProperties: properties));
        }
        else
        {
            this.TextBox(canvas, this.ContentArea(layout), slide.Body, this.BodySize(step), center: false);
        }

        return canvas;
    }

    private Canvas WriteVisualSlide(VisualSlide slide, int step)
    {
        var layout = this.LayoutOf(slide, this.titleOnlyLayout);
        var canvas = this.NewSlide(layout);
        this.Title(canvas, slide.Title);
        var area = this.ContentArea(layout);
        var (textSize, captionSize) = (VisualTextSize(step), CaptionTextSize(step));
        var y = area.Y;
        if (slide.Above.Count > 0)
        {
            var height = EstimateHeight(slide.Above, textSize, area.W);
            this.TextBox(canvas, new Rect(area.X, y, area.W, height), slide.Above, textSize, center: false);
            y += height + Gap;
        }

        var captionHeight = slide.Below.Count > 0 ? EstimateHeight(slide.Below, captionSize, area.W) : 0;
        var placed = new Rect(area.X, y, area.W, 0);
        if (slide.Table is { } table)
        {
            placed = this.Table(canvas, table, new Rect(area.X, y, area.W, 0), TableStartSize(table) - (step * SizeStep));
        }
        else if (slide.Images is not null || slide.Drawing is not null || slide.Chart is not null)
        {
            var room = area.Bottom - y - (captionHeight > 0 ? captionHeight + Gap : 0);
            var box = new Rect(area.X, y, area.W, Math.Max(room, area.H / 4));
            placed = slide.Images is { } images ? box with { H = this.Images(canvas, images, box) }
                : slide.Chart is { } chart ? this.Chart(canvas, chart, box)
                : this.Drawing(canvas, slide.Drawing!, box);
        }

        if (slide.Below.Count > 0)
        {
            this.TextBox(canvas, new Rect(area.X, placed.Bottom + Gap, area.W, captionHeight), slide.Below, captionSize, center: slide.Table is null);
        }

        return canvas;
    }

    // A dark slide uses the dark layout where the template has one, else the title-only layout with a dark background
    // and white text on everything written here.
    private Canvas WriteFlowSlide(FlowSlide slide, int step)
    {
        var layout = this.FlowLayout(slide);
        var canvas = this.NewSlide(layout);
        if (this.PaintsDark(slide))
        {
            canvas.Part.Slide!.CommonSlideData!.PrependChild(new P.Background(new P.BackgroundProperties(this.palette.Dark.Fill(), new A.EffectList())));
        }

        var titleArea = this.TitleArea(layout);
        Rect? titleBox = null;
        if (slide.Kicker is { } kicker)
        {
            var height = LineHeight(KickerSize) + TextInsets;
            var run = this.Run(canvas, new TextRun(kicker, RunStyle.Bold), KickerSize, color: slide.Dark && !this.palette.DarkSlides ? SlideColor.White : this.palette.Accent);
            run.RunProperties!.Capital = A.TextCapsValues.All;
            run.RunProperties.Spacing = 100;
            var paragraph = new A.Paragraph(new A.ParagraphProperties(new A.NoBullet()) { LeftMargin = 0, Indent = 0 }, run, new A.EndParagraphRunProperties { FontSize = KickerSize });
            canvas.Add(TextBoxShape(canvas, titleArea with { H = height }, [paragraph]));
            titleBox = titleArea with { Y = titleArea.Y + height, H = Math.Max(titleArea.H - height, height) };
        }

        this.Title(canvas, slide.Title, null, titleBox, this.PaintsDark(slide) ? SlideColor.White : null);
        var area = this.ContentArea(layout);
        this.Stack(canvas, slide.Blocks, area, this.FlowStyleOf(slide, step));
        return canvas;
    }

    // On a template whose slides are all dark, a dark slide keeps the template's background.
    private bool PaintsDark(Slide slide) => slide is FlowSlide { Dark: true } && this.darkLayout is null && !this.palette.DarkSlides;

    // Slide number and date, where the layout has placeholders for them; their position and style come from there.
    private void Footer(Canvas canvas, int number, SlideColor? color = null)
    {
        var layout = canvas.Part.SlideLayoutPart!;
        A.RunProperties Properties() => color is { } fill ? new A.RunProperties(fill.Fill()) : new A.RunProperties();
        if (FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.SlideNumber) is { } slideNumber)
        {
            var field = new A.Field(Properties(), new A.Text(number.ToString(CultureInfo.InvariantCulture)))
            {
                Id = TemplateBuilder.SlideNumberFieldId,
                Type = "slidenum",
            };
            canvas.Add(Placeholder(canvas, slideNumber, "Slide Number", [new A.Paragraph(field, new A.EndParagraphRunProperties())]));
        }

        if (this.date is { } text && FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.DateAndTime) is { } date)
        {
            canvas.Add(Placeholder(canvas, date, "Date", [new A.Paragraph(new A.Run(Properties(), new A.Text(text)), new A.EndParagraphRunProperties())]));
        }

        if (this.footerText is { } footer && FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.Footer) is { } placeholder)
        {
            canvas.Add(Placeholder(canvas, placeholder, "Footer", [new A.Paragraph(new A.Run(Properties(), new A.Text(XmlSafe(footer))), new A.EndParagraphRunProperties())]));
        }
    }

    // Blocks top to bottom with a gap between them; returns the height they take. Without a canvas it only measures.
    // Images and drawings share the height the other blocks leave, each at least a quarter of the slide.
    private long Stack(Canvas? canvas, IReadOnlyList<FlowBlock> blocks, Rect box, FlowStyle style)
    {
        var visuals = blocks.Count(IsVisual);
        long? visualRoom = null;
        if (visuals > 0 && box.H > 0)
        {
            var others = blocks.Where(b => !IsVisual(b)).Sum(b => this.Place(null, b, box with { H = 0 }, style)) + (style.BlockGap * (blocks.Count - 1));
            visualRoom = Math.Max((box.H - others) / visuals, 0);
        }

        var y = box.Y;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                y += style.BlockGap;
            }

            var room = IsVisual(blocks[i]) && visualRoom is { } shared ? shared : Math.Max(box.Bottom - y, 0);
            y += this.Place(canvas, blocks[i], new Rect(box.X, y, box.W, room), style);
        }

        return y - box.Y;
    }

    private static bool IsVisual(FlowBlock block) => block switch
    {
        ImageBlock or ImageRowBlock or DrawingBlock or ChartBlock => true,
        ColumnsBlock columns => columns.Columns.Any(c => c.Any(IsVisual)),
        _ => false,
    };

    private long Place(Canvas? canvas, FlowBlock block, Rect box, FlowStyle style) => block switch
    {
        TextBlock text => this.FlowText(canvas, text.Paragraphs, box, style.Size, style.Text),
        TableBlock table => this.Table(canvas, table.Table, box).H,
        ImageBlock image => this.Images(canvas, [image.Image], this.VisualBox(box)),
        ImageRowBlock row => this.Images(canvas, row.Images, this.VisualBox(box)),
        ColumnsBlock columns => this.Columns(canvas, columns, box, style),
        ChartBlock chart => this.Chart(canvas, chart.Chart, this.VisualBox(box)).H,
        DrawingBlock drawing => this.Drawing(canvas, drawing.Drawing, this.VisualBox(box)).H,
        CardsBlock cards => this.Cards(canvas, cards, box, style),
        RowsBlock rows => this.Rows(canvas, rows, box, style),
        PanelBlock panel => this.Panel(canvas, panel, box, style),
        CalloutBlock callout => this.Callout(canvas, callout, box, style),
        StatementBlock statement => this.FlowText(canvas, [new TextParagraph(Bold(statement.Text))], box, style.Scaled(StatementSize), this.Heading(style)),
        TagsBlock tags => this.Tags(canvas, tags, box, style),
        CodeLinesBlock code => this.Code(canvas, code, box, style),
        LabelledTagsBlock labelled => this.LabelledTags(canvas, labelled, box, style),
        _ => throw new InvalidOperationException("Unknown kind of block."),
    };

    // An image or drawing gets the room left, but at least a quarter of the slide's height.
    private Rect VisualBox(Rect box) => box with { H = Math.Max(box.H, this.slideHeight / 4) };

    private long FlowText(Canvas? canvas, IReadOnlyList<TextParagraph> paragraphs, Rect box, int size, SlideColor? color)
    {
        var height = FlowHeight(paragraphs, size, box.W);
        canvas?.Add(TextBoxShape(canvas, box with { H = height }, paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, size, color: color))));
        return height;
    }

    // Images side by side in cells of equal width, each fitted into its cell and centred in it, its alt text under it
    // when it asks for a caption; more than four wrap into further rows that share the height. Returns the height.
    private long Images(Canvas? canvas, IReadOnlyList<SlideImage> images, Rect box)
    {
        if (images.Count > MaxImagesPerRow)
        {
            var rows = images.Chunk(MaxImagesPerRow).ToList();
            var rowHeight = Math.Max((box.H - ((rows.Count - 1) * Gap)) / rows.Count, 0);
            var y = box.Y;
            foreach (var row in rows)
            {
                y += this.Images(canvas, row, box with { Y = y, H = rowHeight }) + Gap;
            }

            return y - Gap - box.Y;
        }

        var count = images.Count;
        var width = (box.W - ((count - 1) * Gap)) / count;
        var captions = images.Select(i => i.Caption && i.AltText.Length > 0 ? new TextParagraph([new TextRun(i.AltText, RunStyle.None)]) : null).ToList();
        var captionHeight = captions.Any(c => c is not null)
            ? captions.OfType<TextParagraph>().Max(c => EstimateHeight([c], CaptionSize, width)) + Gap
            : 0;
        var cells = images.Select((image, i) => this.Picture(null, image, new Rect(box.X + (i * (width + Gap)), box.Y, width, Math.Max(box.H - captionHeight, box.H / 2)))).ToList();
        var top = cells.Max(c => c.H);
        if (canvas is not null)
        {
            for (var i = 0; i < count; i++)
            {
                var cell = new Rect(box.X + (i * (width + Gap)), box.Y, width, Math.Max(box.H - captionHeight, box.H / 2));
                var placed = this.Picture(canvas, images[i], cell);
                if (captions[i] is { } caption)
                {
                    this.TextBox(canvas, new Rect(cell.X, placed.Bottom + Gap, width, captionHeight - Gap), [caption], CaptionSize, center: true);
                }
            }
        }

        return top + captionHeight;
    }

    // The columns of `::: columns` side by side, in the ratio of their widths; as tall as the tallest.
    private long Columns(Canvas? canvas, ColumnsBlock block, Rect box, FlowStyle style)
    {
        var total = block.Widths.Sum();
        var room = box.W - ((block.Columns.Count - 1) * Gap);
        var x = box.X;
        long height = 0;
        for (var i = 0; i < block.Columns.Count; i++)
        {
            var width = i == block.Columns.Count - 1 ? box.Right - x : room * block.Widths[i] / total;
            height = Math.Max(height, this.Stack(canvas, block.Columns[i], new Rect(x, box.Y, width, box.H), style));
            x += width + Gap;
        }

        return height;
    }

    // Cards side by side, at most four to a row, each row as tall as its tallest card: outlined, without fill.
    private long Cards(Canvas? canvas, CardsBlock block, Rect box, FlowStyle style)
    {
        var columns = Math.Min(block.Cards.Count, MaxCardsPerRow);
        var width = (box.W - ((columns - 1) * Gap)) / columns;
        var inner = style with { Size = style.Scaled(FlowSmallSize) };
        var y = box.Y;
        foreach (var row in block.Cards.Chunk(columns))
        {
            var height = row.Max(card => this.Card(null, card, new Rect(0, 0, width, 0), inner));
            if (canvas is not null)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    var card = new Rect(box.X + (i * (width + Gap)), y, width, height);
                    canvas.Add(Shape(canvas, card, A.ShapeTypeValues.RoundRectangle, null, style.Dark ? this.palette.DarkLine : this.palette.Line));
                    this.Card(canvas, row[i], card, inner);
                }
            }

            y += height + Gap;
        }

        return y - Gap - box.Y;
    }

    private long Card(Canvas? canvas, Card card, Rect box, FlowStyle style)
    {
        var inner = new Rect(box.X + Pad, box.Y + Pad, box.W - (2 * Pad), Math.Max(box.H - (2 * Pad), 0));
        var y = inner.Y + this.FlowText(canvas, [new TextParagraph(Bold(card.Title))], inner, style.Scaled(CardTitleSize), this.Heading(style));
        if (card.Figure is { } figure)
        {
            y += this.FlowText(canvas, [new TextParagraph(Bold(figure))], inner with { Y = y }, style.Scaled(FigureSize), style.Dark && !this.palette.DarkSlides ? SlideColor.White : this.palette.Accent);
        }

        if (card.Blocks.Count > 0)
        {
            y += this.Stack(canvas, card.Blocks, inner with { Y = y, H = Math.Max(inner.Bottom - y, 0) }, style);
        }

        return y - inner.Y + (2 * Pad);
    }

    // A rule above every row and below the last; the term in bold in the left third.
    private long Rows(Canvas? canvas, RowsBlock block, Rect box, FlowStyle style)
    {
        var termWidth = box.W * 32 / 100;
        var description = new Rect(box.X + termWidth + Gap, 0, box.W - termWidth - Gap, 0);
        var line = style.Dark ? this.palette.DarkLine : this.palette.Line;
        var y = box.Y;
        foreach (var row in block.Rows)
        {
            var term = new TextParagraph(Bold(row.Term));
            var height = Math.Max(FlowHeight([term], style.Size, termWidth), FlowHeight(row.Description, style.Size, description.W)) + (2 * RowPad);
            if (canvas is not null)
            {
                canvas.Add(Shape(canvas, new Rect(box.X, y, box.W, RuleWidth), A.ShapeTypeValues.Rectangle, line, null));
                this.FlowText(canvas, [term], new Rect(box.X, y + RowPad, termWidth, 0), style.Size, this.Heading(style));
                this.FlowText(canvas, row.Description, description with { Y = y + RowPad }, style.Size, style.Text);
            }

            y += height;
        }

        if (canvas is not null)
        {
            canvas.Add(Shape(canvas, new Rect(box.X, y, box.W, RuleWidth), A.ShapeTypeValues.Rectangle, line, null));
        }

        return y + RuleWidth - box.Y;
    }

    private long Panel(Canvas? canvas, PanelBlock panel, Rect box, FlowStyle style)
    {
        var inner = style with { Dark = style.Dark || panel.Dark };
        var content = new Rect(box.X + Pad, box.Y + Pad, box.W - (2 * Pad), Math.Max(box.H - (2 * Pad), 0));
        var height = this.Stack(null, panel.Blocks, content, inner) + (2 * Pad);
        if (canvas is not null)
        {
            var fill = panel.Dark && this.palette.DarkSlides ? this.palette.Dark
                : style.Dark ? this.palette.DarkSurface
                : panel.Dark ? this.palette.Dark
                : this.palette.Surface;
            canvas.Add(Shape(canvas, box with { H = height }, A.ShapeTypeValues.RoundRectangle, fill, null));
            this.Stack(canvas, panel.Blocks, content, inner);
        }

        return height;
    }

    // A band with a bar on its left: the primary colour for notes and tips, the highlight for the others.
    private long Callout(Canvas? canvas, CalloutBlock callout, Rect box, FlowStyle style)
    {
        var content = new Rect(box.X + BarWidth + Pad, box.Y + RowPad, box.W - BarWidth - (2 * Pad), Math.Max(box.H - (2 * RowPad), 0));
        var height = this.Stack(null, callout.Blocks, content, style) + (2 * RowPad);
        if (canvas is not null)
        {
            canvas.Add(Shape(canvas, box with { H = height }, A.ShapeTypeValues.Rectangle, this.Surface(style), null));
            canvas.Add(Shape(canvas, box with { W = BarWidth, H = height }, A.ShapeTypeValues.Rectangle, callout.Important ? this.palette.ImportantBar : this.palette.NoteBar, null));
            this.Stack(canvas, callout.Blocks, content, style);
        }

        return height;
    }

    // Code on a box of its own, in the code font with more room between the lines.
    private long Code(Canvas? canvas, CodeLinesBlock code, Rect box, FlowStyle style)
    {
        var inner = box.W - (2 * Pad);
        var lines = code.Lines.Sum(line => EstimateLines([new TextRun(line, RunStyle.Code)], style.Scaled(FlowSmallSize), inner));
        var height = (long)(lines * CodeLineSpacing / 100000.0) + TextInsets + (2 * RowPad);
        if (canvas is not null)
        {
            canvas.Add(Shape(canvas, box with { H = height }, A.ShapeTypeValues.RoundRectangle, style.Dark ? this.palette.DarkSurface : this.palette.Code, null));
            var paragraphs = code.Lines.Select(line =>
            {
                var paragraph = this.Paragraph(canvas, new TextParagraph(line.Length > 0 ? [new TextRun(line, RunStyle.Code)] : []), explicitBullets: true, style.Scaled(FlowSmallSize), color: style.Text);
                paragraph.ParagraphProperties!.PrependChild(new A.LineSpacing(new A.SpacingPercent { Val = CodeLineSpacing }));
                return paragraph;
            });
            canvas.Add(TextBoxShape(canvas, new Rect(box.X + Pad, box.Y + RowPad, inner, height - (2 * RowPad)), paragraphs));
        }

        return height;
    }

    // Chips left to right, wrapping at the right edge.
    private long Tags(Canvas? canvas, TagsBlock block, Rect box, FlowStyle style)
    {
        var height = ChipHeight(style.Scaled(TagSize));
        var (x, y) = (box.X, box.Y);
        foreach (var tag in block.Tags)
        {
            var width = Math.Min(ChipWidth(tag, style.Scaled(TagSize)), box.W);
            if (x > box.X && x + width > box.Right)
            {
                (x, y) = (box.X, y + height + ChipGap);
            }

            if (canvas is not null)
            {
                canvas.Add(this.Chip(canvas, new Rect(x, y, width, height), new TextRun(tag, RunStyle.Code), style));
            }

            x += width + ChipGap;
        }

        return y + height - box.Y;
    }

    // The labels in a column as wide as the widest, each item's text beside its label.
    private long LabelledTags(Canvas? canvas, LabelledTagsBlock block, Rect box, FlowStyle style)
    {
        var labelWidth = Math.Min(block.Items.Max(i => ChipWidth(i.Label, style.Scaled(TagSize))), box.W / 2);
        var text = new Rect(box.X + labelWidth + Gap, 0, box.W - labelWidth - Gap, 0);
        var chipHeight = ChipHeight(style.Scaled(TagSize));
        var y = box.Y;
        for (var i = 0; i < block.Items.Count; i++)
        {
            if (i > 0)
            {
                y += ChipGap;
            }

            var item = block.Items[i];
            var paragraph = new TextParagraph(item.Text);
            var height = Math.Max(chipHeight, FlowHeight([paragraph], style.Size, text.W));
            if (canvas is not null)
            {
                // Lowered so that its text sits on the first line of the item's text, which has the larger inset.
                canvas.Add(this.Chip(canvas, new Rect(box.X, y + (TextInsets / 2) - ChipPadY, labelWidth, chipHeight), new TextRun(item.Label, RunStyle.Bold), style));
                this.FlowText(canvas, [paragraph], text with { Y = y }, style.Size, style.Text);
            }

            y += height;
        }

        return y - box.Y;
    }

    // Outlined on the page colour, so that chips stand out on cards and panels too.
    private P.Shape Chip(Canvas canvas, Rect box, TextRun text, FlowStyle style)
    {
        var paragraph = this.Paragraph(canvas, new TextParagraph([text]), explicitBullets: true, style.Scaled(TagSize), CellAlignment.Center, color: this.Heading(style));
        return style.Dark
            ? Shape(canvas, box, A.ShapeTypeValues.RoundRectangle, this.palette.DarkSurface, this.palette.DarkLine, paragraph)
            : Shape(canvas, box, A.ShapeTypeValues.RoundRectangle, this.palette.Paper, this.palette.Line, paragraph);
    }

    private SlideColor Surface(FlowStyle style) => style.Dark ? this.palette.DarkSurface : this.palette.Surface;

    // Titles of cards and rows, statements and chips: white on dark, else the primary colour.
    private SlideColor Heading(FlowStyle style) => style.Dark ? SlideColor.White : this.palette.Primary;

    // A shape, filled or not, optionally outlined, optionally with one line of centred text (a chip).
    private static P.Shape Shape(Canvas canvas, Rect box, A.ShapeTypeValues preset, SlideColor? fill, SlideColor? line, A.Paragraph? text = null)
    {
        var id = canvas.NextId();
        var geometry = new A.PresetGeometry(new A.AdjustValueList()) { Preset = preset };
        if (preset == A.ShapeTypeValues.RoundRectangle && text is null)
        {
            geometry.AdjustValueList!.Append(new A.ShapeGuide { Name = "adj", Formula = "val 6000" });
        }

        var outline = line is { } stroke ? new A.Outline(stroke.Fill()) { Width = 9525 } : new A.Outline(new A.NoFill());
        var shape = new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = (text is null ? "Box " : "Chip ") + id },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(box.Transform(), geometry, fill is { } color ? color.Fill() : new A.NoFill(), outline));
        if (text is not null)
        {
            shape.Append(new P.TextBody(
                new A.BodyProperties(new A.NoAutoFit())
                {
                    Wrap = A.TextWrappingValues.None,
                    LeftInset = (int)ChipPadX,
                    RightInset = (int)ChipPadX,
                    TopInset = (int)ChipPadY,
                    BottomInset = (int)ChipPadY,
                    Anchor = A.TextAnchoringTypeValues.Center,
                },
                new A.ListStyle(),
                text));
        }

        return shape;
    }

    private static List<TextRun> Bold(IEnumerable<TextRun> runs) => runs.Select(r => r.IsBreak ? r : r with { Style = r.Style | RunStyle.Bold }).ToList();

    private static long FlowHeight(IEnumerable<TextParagraph> paragraphs, int size, long width) =>
        paragraphs.Sum(p => EstimateLines(p.Runs, size, width - ((p.Level + (p.Kind == ParagraphKind.Plain ? 0 : 1)) * BulletIndent))
            + (long)(size / 100.0 * 0.2 * EmuPerPoint)) + TextInsets;

    private static long LineHeight(int size) => (long)(size / 100.0 * 1.2 * EmuPerPoint);

    private static long ChipHeight(int size) => LineHeight(size) + (2 * ChipPadY);

    private static long ChipWidth(string text, int size) => (long)(text.Length * size / 100.0 * 0.62 * EmuPerPoint) + (2 * ChipPadX);

    // The fill of a status label's cell: a light tint of its category's colour; labels no category names are neutral.
    private SlideColor LabelFill(string text) => this.palette.LabelFill(this.labels.GetValueOrDefault(text, LabelCategory.Neutral));

    private Canvas NewSlide(SlideLayoutPart layout)
    {
        var part = this.presentationPart.AddNewPart<SlidePart>();
        part.AddPart(layout);
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties());
        part.Slide = new P.Slide(new P.CommonSlideData(tree), new P.ColorMapOverride(new A.MasterColorMapping()));
        this.presentation.SlideIdList ??= new P.SlideIdList();
        this.presentation.SlideIdList.Append(new P.SlideId { Id = this.nextSlideId++, RelationshipId = this.presentationPart.GetIdOfPart(part) });
        return new Canvas(part, tree);
    }

    // `box` moves the title from where the layout has it; `color` overrides the layout's.
    private void Title(Canvas canvas, IReadOnlyList<TextRun> title, P.PlaceholderShape? fallback = null, Rect? box = null, SlideColor? color = null)
    {
        var layout = canvas.Part.SlideLayoutPart!;
        var paragraph = new A.Paragraph();
        foreach (var run in title)
        {
            paragraph.Append(run.IsBreak ? new A.Break() : this.Run(canvas, run, null, color: color));
        }

        paragraph.Append(new A.EndParagraphRunProperties());
        if ((FindPlaceholder(layout, IsTitle) ?? fallback) is { } placeholder)
        {
            canvas.Add(Placeholder(canvas, placeholder, "Title", [paragraph], box));
        }
        else
        {
            canvas.Add(TextBoxShape(canvas, box ?? this.TitleArea(layout), [paragraph]));
        }
    }

    private void TextBox(Canvas canvas, Rect box, IEnumerable<TextParagraph> paragraphs, int size, bool center) =>
        canvas.Add(TextBoxShape(canvas, box, paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, size, center ? CellAlignment.Center : CellAlignment.Default, color: this.palette.Text))));

    private static P.Shape TextBoxShape(Canvas canvas, Rect box, IEnumerable<A.Paragraph> paragraphs)
    {
        var id = canvas.NextId();
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "TextBox " + id },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                box.Transform(),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            new P.TextBody(new OpenXmlElement[]
            {
                new A.BodyProperties(new A.ShapeAutoFit()) { Wrap = A.TextWrappingValues.Square, LeftInset = 0, RightInset = 0 },
                new A.ListStyle(),
            }.Concat(paragraphs)));
    }

    private static P.Shape Placeholder(Canvas canvas, P.PlaceholderShape layoutPlaceholder, string name, IEnumerable<A.Paragraph> paragraphs, Rect? box = null, A.BodyProperties? bodyProperties = null)
    {
        var id = canvas.NextId();
        var placeholder = new P.PlaceholderShape();
        if (layoutPlaceholder.Type is { } type)
        {
            placeholder.Type = type.Value;
        }

        if (layoutPlaceholder.Index is { } index)
        {
            placeholder.Index = index.Value;
        }

        var body = new P.TextBody(new OpenXmlElement[] { bodyProperties ?? new A.BodyProperties(), new A.ListStyle() }.Concat(paragraphs));
        if (!body.Elements<A.Paragraph>().Any())
        {
            body.Append(new A.Paragraph(new A.EndParagraphRunProperties()));
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name + " " + id },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(placeholder)),
            box is { } position ? new P.ShapeProperties(position.Transform()) : new P.ShapeProperties(),
            body);
    }

    // In placeholders the template's levels supply indents and bullets; in text boxes and cells they are explicit.
    // `font` is the family of all but code runs; null leaves them in the theme font. `color` is that of every run
    // without a colour of its own; null leaves them in the theme's.
    private A.Paragraph Paragraph(Canvas canvas, TextParagraph paragraph, bool explicitBullets, int? size, CellAlignment alignment = CellAlignment.Default, string? font = null, SlideColor? color = null)
    {
        var level = Math.Min(paragraph.Level, 8);
        var properties = new A.ParagraphProperties();
        if (level > 0)
        {
            properties.Level = level;
        }

        switch (paragraph.Kind)
        {
            case ParagraphKind.Plain:
                if (explicitBullets || level == 0)
                {
                    properties.LeftMargin = (int)(level * BulletIndent);
                }

                properties.Indent = 0;
                properties.Append(new A.NoBullet());
                break;
            case ParagraphKind.Bullet when explicitBullets:
                properties.LeftMargin = (int)((level + 1) * BulletIndent);
                properties.Indent = (int)-BulletIndent;
                properties.Append(new A.BulletFont { Typeface = "Arial" }, new A.CharacterBullet { Char = level % 2 == 0 ? "\u2022" : "\u2013" });
                break;
            case ParagraphKind.Numbered:
                if (explicitBullets)
                {
                    properties.LeftMargin = (int)((level + 1) * BulletIndent);
                    properties.Indent = (int)-BulletIndent;
                }

                var numbering = new A.AutoNumberedBullet { Type = A.TextAutoNumberSchemeValues.ArabicPeriod };
                if (paragraph.NumberStart != 1)
                {
                    numbering.StartAt = paragraph.NumberStart;
                }

                properties.Append(new A.BulletFont { Typeface = "+mj-lt" }, numbering);
                break;
        }

        if (alignment != CellAlignment.Default)
        {
            properties.Alignment = alignment switch
            {
                CellAlignment.Center => A.TextAlignmentTypeValues.Center,
                CellAlignment.Right => A.TextAlignmentTypeValues.Right,
                _ => A.TextAlignmentTypeValues.Left,
            };
        }

        var result = new A.Paragraph(properties);
        foreach (var run in paragraph.Runs)
        {
            result.Append(run.IsBreak ? new A.Break() : this.Run(canvas, run, size, font, color));
        }

        var end = new A.EndParagraphRunProperties();
        if (size is { } endSize)
        {
            end.FontSize = endSize;
        }

        result.Append(end);
        return result;
    }

    // Labels and marked text are bold; marked text gets the light highlight behind it, like a highlighter pen.
    private A.Run Run(Canvas canvas, TextRun run, int? size, string? font = null, SlideColor? color = null)
    {
        var properties = new A.RunProperties();
        if (run.Style.HasFlag(RunStyle.Bold) || run.Style.HasFlag(RunStyle.Label))
        {
            properties.Bold = true;
        }

        if (run.Style.HasFlag(RunStyle.Italic))
        {
            properties.Italic = true;
        }

        if (run.Style.HasFlag(RunStyle.Strike))
        {
            properties.Strike = A.TextStrikeValues.SingleStrike;
        }

        if (size is { } fontSize)
        {
            properties.FontSize = fontSize;
        }

        if (run.Style.HasFlag(RunStyle.Mark))
        {
            properties.Bold = true;
        }

        if ((run.Colour is { } category ? this.palette.WordColour(category)
            : run.Style.HasFlag(RunStyle.Mark) ? this.palette.MarkText ?? color
            : color) is { } runColor)
        {
            properties.Append(runColor.Fill());
        }

        if (run.Style.HasFlag(RunStyle.Mark) && this.palette.MarkFill is { } markFill)
        {
            properties.Append(new A.Highlight(markFill.Element()));
        }

        if (run.Style.HasFlag(RunStyle.Code))
        {
            properties.Append(new A.LatinFont { Typeface = this.codeFont });
        }
        else if (font is not null)
        {
            properties.Append(new A.LatinFont { Typeface = font });
        }

        if (run.Link is { } link && canvas.Hyperlink(link) is { } relationshipId)
        {
            properties.Append(new A.HyperlinkOnClick { Id = relationshipId });
        }

        return new A.Run(properties, new A.Text(XmlSafe(run.Text)));
    }

    // Without a canvas, only where it would go.
    private Rect Picture(Canvas? canvas, SlideImage image, Rect box)
    {
        var info = ImageInfo.Read(image.Resource.Content.Span)
            ?? throw new DocumentConversionException(Format.Pptx, $"The image '{image.Resource.Path}' is not a PNG, JPEG or GIF.");
        var (naturalWidth, naturalHeight) = image.Natural ?? (info.Width, info.Height);
        var width = naturalWidth * EmuPerPixel;
        var height = naturalHeight * EmuPerPixel;
        var fit = Math.Min(image.Scale ?? this.options.MaxImageUpscale, Math.Min(box.W / width, box.H / height));
        var placed = new Rect(box.X + (long)((box.W - (width * fit)) / 2), box.Y, (long)(width * fit), (long)(height * fit));
        if (canvas is null)
        {
            return placed;
        }

        if (!this.images.TryGetValue(image.Resource, out var part))
        {
            part = canvas.Part.AddImagePart(info.Format switch
            {
                ImageFormat.Png => ImagePartType.Png,
                ImageFormat.Gif => ImagePartType.Gif,
                _ => ImagePartType.Jpeg,
            });
            using var content = new MemoryStream(image.Resource.Content.ToArray());
            part.FeedData(content);
            this.images[image.Resource] = part;
        }

        var relationshipId = canvas.Part.Parts.Where(p => p.OpenXmlPart == part).Select(p => p.RelationshipId).FirstOrDefault()
            ?? canvas.Part.CreateRelationshipToPart(part);
        var id = canvas.NextId();
        canvas.Add(new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Picture " + id, Description = XmlSafe(image.AltText) },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.BlipFill(this.Blip(canvas, image, relationshipId), new A.Stretch(new A.FillRectangle())),
            new P.ShapeProperties(placed.Transform(), new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })));
        return placed;
    }

    // The picture's PNG, JPEG or GIF; for a converted SVG also the SVG, which PowerPoint 2016 and later draw instead.
    private A.Blip Blip(Canvas canvas, SlideImage image, string relationshipId)
    {
        var blip = new A.Blip { Embed = relationshipId };
        if (image.Vector is not { } vector)
        {
            return blip;
        }

        if (!this.vectors.TryGetValue(vector, out var part))
        {
            part = canvas.Part.AddImagePart(ImagePartType.Svg);
            using var content = new MemoryStream(vector.Content.ToArray());
            part.FeedData(content);
            this.vectors[vector] = part;
        }

        var svgId = canvas.Part.Parts.Where(p => p.OpenXmlPart == part).Select(p => p.RelationshipId).FirstOrDefault()
            ?? canvas.Part.CreateRelationshipToPart(part);
        blip.Append(new A.BlipExtensionList(new A.BlipExtension(new SVGBlip { Embed = svgId }) { Uri = SvgBlipUri }));
        return blip;
    }

    // A native chart filling the box's width, at most about 16:10 tall, so that it neither stretches nor squashes.
    private Rect Chart(Canvas? canvas, ChartSpec chart, Rect box)
    {
        var placed = box with { H = Math.Min(box.H, box.W * 5 / 8) };
        if (canvas is null)
        {
            return placed;
        }

        var relationshipId = ChartWriter.Add(canvas.Part, chart, this.palette.Text, this.palette.DarkSlides ? SlideColor.White.Translucent(30) : new SlideColor("D9D9D9"), this.tableFont);
        var id = canvas.NextId();
        canvas.Add(new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Chart " + id },
                new P.NonVisualGraphicFrameDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = placed.X, Y = placed.Y }, new A.Extents { Cx = placed.W, Cy = placed.H }),
            new A.Graphic(new A.GraphicData(new DocumentFormat.OpenXml.Drawing.Charts.ChartReference { Id = relationshipId }) { Uri = ChartGraphicUri })));
        return placed;
    }

    // Fitted into the box like a picture: centred, enlarged at most by half, so that its text keeps a sensible size.
    private Rect Drawing(Canvas? canvas, Drawing drawing, Rect box)
    {
        var fit = Math.Min(MaxDrawingUpscale, Math.Min(box.W / (drawing.Width * EmuPerPixel), box.H / (drawing.Height * EmuPerPixel)));
        var width = (long)(drawing.Width * EmuPerPixel * fit);
        var placed = new Rect(box.X + ((box.W - width) / 2), box.Y, width, (long)(drawing.Height * EmuPerPixel * fit));
        canvas?.Add(DrawingWriter.Group(drawing, placed.X, placed.Y, EmuPerPixel * fit, canvas.NextId, this.tableFont, this.codeFont));
        return placed;
    }

    // Without a canvas, only how tall it would be. Highlighted rows and status labels get a fill over the table style's.
    private Rect Table(Canvas? canvas, SlideTable table, Rect box, int? startSize = null)
    {
        var columns = table.Alignments.Count;
        if (columns == 0 || table.Rows.Count == 0)
        {
            return box with { H = 0 };
        }

        var (size, widths, rowHeights) = TableLayout(table, box.W, startSize ?? TableStartSize(table));
        var grid = new A.TableGrid(widths.Select(w => new A.GridColumn { Width = w }));
        var element = new A.Table(new A.TableProperties(new A.TableStyleId(this.tableStyleId)) { FirstRow = table.HasHeader, BandRow = true }, grid);
        long height = 0;
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var rowHeight = rowHeights[r];
            height += rowHeight;
            if (canvas is null)
            {
                continue;
            }

            var highlighted = r < table.Highlighted.Count && table.Highlighted[r];
            var tableRow = new A.TableRow { Height = rowHeight };
            var dark = this.palette.DarkTable;
            var rowFill = dark is not { } colours ? (SlideColor?)null
                : table.HasHeader && r == 0 ? colours.Header
                : (r - (table.HasHeader ? 1 : 0)) % 2 == 0 ? colours.Band
                : null;
            for (var c = 0; c < columns; c++)
            {
                var paragraph = this.Paragraph(canvas, new TextParagraph(row[c]), explicitBullets: true, size, table.Alignments[c], this.tableFont, dark?.Text);
                var fill = row[c] is [{ Style: RunStyle.Label } label] ? this.LabelFill(label.Text) : highlighted ? this.palette.HighlightSurface : rowFill;
                // On dark slides every cell has a fill of its own, transparent where none is due: the table style's would be light.
                var properties = fill is { } cellFill ? new A.TableCellProperties(cellFill.Fill())
                    : dark is not null ? new A.TableCellProperties(new A.NoFill())
                    : new A.TableCellProperties();
                tableRow.Append(new A.TableCell(new A.TextBody(new A.BodyProperties(), new A.ListStyle(), paragraph), properties));
            }

            element.Append(tableRow);
        }

        if (canvas is null)
        {
            return box with { H = height };
        }

        var id = canvas.NextId();
        canvas.Add(new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Table " + id },
                new P.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.Transform(new A.Offset { X = box.X, Y = box.Y }, new A.Extents { Cx = box.W, Cy = height }),
            new A.Graphic(new A.GraphicData(element) { Uri = TableGraphicUri })));
        return box with { H = height };
    }

    // The font size, column widths and row heights of a table `width` wide. Every column is at least as wide as its
    // longest word; the rest of the width goes to the columns by the length of their longest text. When even the words
    // do not fit, the font steps down to 10 pt, and only then do the columns shrink below their words.
    private static int TableStartSize(SlideTable table) => table.Rows.Count switch { <= 6 => 1600, <= 10 => 1400, _ => 1200 };

    private static (int Size, long[] Widths, long[] RowHeights) TableLayout(SlideTable table, long width, int startSize)
    {
        var columns = table.Alignments.Count;
        bool Header(int row) => table.HasHeader && row == 0;
        var size = Math.Max(startSize, MinTableSize);
        double[] minimums;
        while (true)
        {
            var atSize = size;
            minimums = Enumerable.Range(0, columns)
                .Select(c => table.Rows.Select((row, r) => TextMetrics.LongestWord(row[c], atSize, Header(r))).Max() + CellInsets)
                .ToArray();
            if (minimums.Sum() <= width || size <= MinTableSize)
            {
                break;
            }

            size -= 200;
        }

        var spare = width - minimums.Sum();
        if (spare < 0)
        {
            minimums = minimums.Select(m => m * width / minimums.Sum()).ToArray();
            spare = 0;
        }

        var weights = Enumerable.Range(0, columns)
            .Select(c => (double)Math.Clamp(table.Rows.Max(r => Segments(r[c]).DefaultIfEmpty(0).Max()), 3, 60))
            .ToList();
        var widths = Enumerable.Range(0, columns).Select(c => (long)(minimums[c] + (spare * weights[c] / weights.Sum()))).ToArray();
        widths[^1] = width - widths[..^1].Sum();

        var lineHeight = size / 100.0 * 1.2 * EmuPerPoint;
        var heights = table.Rows
            .Select((row, r) => Math.Max(
                MinRowHeight,
                (long)(Enumerable.Range(0, columns).Max(c => TextMetrics.Lines(row[c], size, Header(r), widths[c] - CellInsets)) * lineHeight) + 91440))
            .ToArray();
        return (size, widths, heights);
    }

    private Rect TitleArea(SlideLayoutPart layout) =>
        Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, IsTitle)
        ?? Bounds(layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, IsTitle)
        ?? new Rect(this.slideWidth / 20, this.slideHeight / 25, this.slideWidth * 9 / 10, this.slideHeight * 3 / 20);

    private Rect ContentArea(SlideLayoutPart layout)
    {
        if (Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, IsBody) is { } own)
        {
            return own;
        }

        if (Bounds(layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, p => p.Type?.Value == P.PlaceholderValues.Body) is { } master)
        {
            return master;
        }

        var top = this.TitleArea(layout).Bottom + (this.slideHeight / 50);
        return new Rect(this.slideWidth / 20, top, this.slideWidth * 9 / 10, Math.Max((this.slideHeight * 23 / 25) - top, this.slideHeight / 4));
    }

    private static Rect? Bounds(P.ShapeTree? tree, Func<P.PlaceholderShape, bool> match)
    {
        var transform = tree?.Descendants<P.Shape>()
            .FirstOrDefault(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape is { } p && match(p))
            ?.ShapeProperties?.Transform2D;
        return transform?.Offset is { X: { } x, Y: { } y } && transform.Extents is { Cx: { } cx, Cy: { } cy }
            ? new Rect(x.Value, y.Value, cx.Value, cy.Value)
            : null;
    }

    private void RemoveSlides()
    {
        if (this.presentation.SlideIdList is { } list)
        {
            foreach (var slideId in list.Elements<P.SlideId>().ToList())
            {
                if (slideId.RelationshipId?.Value is { } relationshipId && this.presentationPart.TryGetPartById(relationshipId, out var part))
                {
                    this.presentationPart.DeletePart(part);
                }

                slideId.Remove();
            }
        }

        this.presentation.CustomShowList?.Remove();
        foreach (var extension in this.presentation.PresentationExtensionList?.Elements<P.PresentationExtension>().Where(e => e.Uri == SectionListExtension).ToList() ?? [])
        {
            extension.Remove();
        }

        foreach (var slideList in this.presentationPart.ViewPropertiesPart?.ViewProperties?.Descendants<P.OutlineViewSlideList>().ToList() ?? [])
        {
            slideList.Remove();
        }
    }

    private List<SlideLayoutPart> Layouts()
    {
        var masters = this.presentation.SlideMasterIdList?.Elements<P.SlideMasterId>()
            .Select(id => id.RelationshipId?.Value is { } r && this.presentationPart.TryGetPartById(r, out var part) ? part as SlideMasterPart : null)
            .OfType<SlideMasterPart>()
            .ToList() is { Count: > 0 } listed ? listed : this.presentationPart.SlideMasterParts.ToList();
        return masters
            .SelectMany(master => master.SlideMaster?.SlideLayoutIdList?.Elements<P.SlideLayoutId>()
                .Select(id => id.RelationshipId?.Value is { } r && master.TryGetPartById(r, out var part) ? part as SlideLayoutPart : null)
                .OfType<SlideLayoutPart>() ?? master.SlideLayoutParts)
            .ToList();
    }

    // The layout the slide's heading asked for (`layout="…"`), found by name, case-insensitively; else `fallback`.
    private SlideLayoutPart LayoutOf(Slide slide, SlideLayoutPart fallback)
    {
        if (slide.Layout is not { } name)
        {
            return fallback;
        }

        if (this.builtIn)
        {
            throw new DocumentConversionException(
                Format.Pptx,
                $"The heading attribute layout=\"{name}\" works on a brand's own template or a caller's template; the built-in design chooses its layouts itself.");
        }

        return this.layouts.FirstOrDefault(l => string.Equals(LayoutName(l), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new DocumentConversionException(
                Format.Pptx,
                $"The template has no layout named '{name}' (asked for by layout=\"{name}\"). Its layouts: {string.Join(", ", this.layouts.Select(l => $"'{LayoutName(l)}'"))}.");
    }

    private SlideLayoutPart FlowLayout(FlowSlide slide) =>
        this.LayoutOf(slide, slide.Dark && this.darkLayout is not null ? this.darkLayout : this.titleOnlyLayout);

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(closing|end|thank)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ClosingNamePattern();

    private static SlideLayoutPart? ByName(List<SlideLayoutPart> layouts, string? name, string kind)
    {
        if (name is null)
        {
            return null;
        }

        return layouts.FirstOrDefault(l => string.Equals(LayoutName(l), name, StringComparison.Ordinal))
            ?? throw new DocumentConversionException(
                Format.Pptx,
                $"The template has no layout named '{name}' (asked for as the {kind} layout). "
                + $"Its layouts: {string.Join(", ", layouts.Select(l => $"'{LayoutName(l)}'"))}.");
    }

    private static string? LayoutName(SlideLayoutPart layout) => layout.SlideLayout?.CommonSlideData?.Name?.Value;

    private static SlideLayoutPart? ByType(List<SlideLayoutPart> layouts, P.SlideLayoutValues type) =>
        layouts.FirstOrDefault(l => l.SlideLayout?.Type?.Value == type);

    private static IEnumerable<P.PlaceholderShape> Placeholders(SlideLayoutPart layout) =>
        layout.SlideLayout?.CommonSlideData?.ShapeTree?.Descendants<P.PlaceholderShape>() ?? [];

    private static P.PlaceholderShape? FindPlaceholder(SlideLayoutPart layout, Func<P.PlaceholderShape, bool> match) =>
        Placeholders(layout).FirstOrDefault(match);

    private static bool IsTitle(P.PlaceholderShape placeholder) =>
        placeholder.Type?.Value == P.PlaceholderValues.Title || placeholder.Type?.Value == P.PlaceholderValues.CenteredTitle;

    private static bool IsBody(P.PlaceholderShape placeholder) =>
        placeholder.Type is null || placeholder.Type.Value == P.PlaceholderValues.Body || placeholder.Type.Value == P.PlaceholderValues.Object;

    private static long EstimateHeight(IEnumerable<TextParagraph> paragraphs, int size, long width) =>
        paragraphs.Sum(p => EstimateLines(p.Runs, size, width - ((p.Level + (p.Kind == ParagraphKind.Plain ? 0 : 1)) * BulletIndent))
            + (long)(size / 100.0 * 0.3 * EmuPerPoint)) + CellInsets;

    // Height of the lines a text needs at the given width, wrapping between words as measured by TextMetrics.
    private static long EstimateLines(IReadOnlyList<TextRun> runs, int size, long width) =>
        (long)(TextMetrics.Lines(runs, size, false, width) * size / 100.0 * 1.2 * EmuPerPoint);

    private static IEnumerable<int> Segments(IReadOnlyList<TextRun> runs)
    {
        var length = 0;
        foreach (var run in runs)
        {
            if (run.IsBreak)
            {
                yield return length;
                length = 0;
            }
            else
            {
                length += run.Text.Length;
            }
        }

        yield return length;
    }

    internal static string XmlSafe(string text) =>
        text.Any(c => char.IsControl(c) && c != '\t') ? new string(text.Where(c => !char.IsControl(c) || c == '\t').ToArray()) : text;

    private static DocumentConversionException Damaged(string reason) =>
        new(Format.Pptx, "The template is not usable: " + reason);

    /// <summary>
    /// On a flow slide: whether the blocks sit on a dark background, their text size, and the scale every other size
    /// is set at (below 1 when the slide was shrunk to fit).
    /// </summary>
    private readonly record struct FlowStyle(bool Dark, int Size, double Scale = 1.0)
    {
        public SlideColor? Text => this.Dark ? SlideColor.White : null;

        /// <summary>The gap between blocks: half of it on a shrunk slide.</summary>
        public long BlockGap => this.Scale < 1 ? Gap / 2 : Gap;

        public int Scaled(int size) => (int)(Math.Round(size * this.Scale / 50.0) * 50);
    }

    private readonly record struct Rect(long X, long Y, long W, long H)
    {
        public long Bottom => this.Y + this.H;

        public long Right => this.X + this.W;

        public A.Transform2D Transform() => new(new A.Offset { X = this.X, Y = this.Y }, new A.Extents { Cx = this.W, Cy = this.H });
    }

    /// <summary>The slide being written: its part, its shape tree, and the ids used on it.</summary>
    private sealed class Canvas(SlidePart part, P.ShapeTree tree)
    {
        private readonly Dictionary<string, string?> hyperlinks = new(StringComparer.Ordinal);
        private uint nextId = 2;

        public SlidePart Part => part;

        public uint NextId() => this.nextId++;

        public void Add(OpenXmlElement shape) => tree.Append(shape);

        public string? Hyperlink(string url)
        {
            if (!this.hyperlinks.TryGetValue(url, out var id))
            {
                id = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? part.AddHyperlinkRelationship(uri, isExternal: true).Id : null;
                this.hyperlinks[url] = id;
            }

            return id;
        }
    }
}
