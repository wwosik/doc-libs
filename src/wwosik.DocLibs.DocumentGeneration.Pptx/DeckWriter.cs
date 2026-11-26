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
    private const long BulletIndent = 285750;
    private const long Gap = 152400;

    // How far the classification label hangs from the top edge of the slide.
    private const long ClassificationTop = 91440;
    private const long MinRowHeight = 370840;
    private const long CellInsets = 91440 * 2;
    private const int MinTableSize = 1000;

    // Above this, an enlarged image is reported as possibly blurred.
    private const double MaxSharpUpscale = 2.0;
    private const int MaxImagesPerRow = 4;
    private const int TextSize = 1800;
    private const int CaptionSize = 1400;

    // Footnotes: the smallest table size, at the foot of the content area.
    private const int FootnoteSize = MinTableSize;

    // Wingdings, which ships with Office: an empty and a ticked box.
    private const string TaskFont = "Wingdings";
    private const string EmptyBox = "\uF0A8";
    private const string TickedBox = "\uF0FE";

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
    private readonly TemplateRoles roles;
    private readonly string? footerPattern;
    private readonly List<int> slideLines = [];
    private string? deckTitle;

    // Whether the code being written is on a dark code box (a `.dark` flow slide), for the colours of highlighted runs.
    private bool codeOnDark;
    private readonly string? author;
    private readonly string templateName;

    // The sizes of the nine outline levels of the content layout's body, when the template declares text levels.
    private readonly int[]? levelSizes;
    private readonly Dictionary<DocumentResource, ImagePart> images = [];
    private readonly Dictionary<DocumentResource, ImagePart> vectors = [];
    private readonly IReadOnlyDictionary<string, LabelCategory> labels;
    private readonly GenerationNotes notes;
    private readonly string language;
    private readonly GeneratedTexts texts;
    private Slide? current;

    // Where the footnotes of a slide that fills its layout's placeholders go: below the lowest of them.
    private long? footnoteTop;

    // The footnotes of the slide being fitted or written; the content area leaves room for them at its foot.
    private IReadOnlyList<SlideFootnote> footnotes = [];
    private uint nextSlideId = 256;

    // The title of the latest section divider, for the footer pattern's {chapter}.
    private string? chapter;

    // The content area of the flow slide being written, which a comment's callout stays inside.
    private Rect? flowArea;

    /// <summary>Opens the deck for writing. Layouts named in <paramref name="names"/> win over the lookup by type.</summary>
    /// <remarks>
    /// <paramref name="date"/> is the text for the date placeholder, or <see langword="null"/> for no date;
    /// <paramref name="tableFont"/> the family of table text, or <see langword="null"/> for the theme font;
    /// <paramref name="codeFont"/> the family of code, by default <see cref="PptxOptions.CodeFont"/> or Consolas;
    /// <paramref name="palette"/> the colours of flow slides, by default the template's theme colours;
    /// <paramref name="labels"/> the categories of status labels, from front matter; <paramref name="texts"/> the texts of
    /// the document language, such as the word before a chart's source; <paramref name="roles"/> what the template's
    /// layouts and placeholders are for (the built-in master declares its own); <paramref name="footerPattern"/> the text of
    /// the footer placeholder instead of <paramref name="footerText"/>, with the fields of the deck's title (given to
    /// <see cref="Write"/>) and <paramref name="author"/>; <paramref name="templateName"/> how errors name the template.
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
        bool listBullets = false,
        GenerationNotes? notes = null,
        string language = "en",
        GeneratedTexts? texts = null,
        PptxTemplateRoles? roles = null,
        string? footerPattern = null,
        string? author = null,
        string templateName = "the template")
    {
        this.footerPattern = footerPattern;
        this.author = author;
        this.templateName = templateName;
        this.notes = notes ?? new GenerationNotes();
        this.language = language;
        this.texts = texts ?? GeneratedTexts.English;
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
        this.roles = TemplateRoles.Resolve(layouts, builtIn ? TemplateBuilder.Roles : roles);
        this.darkLayout = this.roles.Layout(LayoutRole.Dark);
        this.bodySize = this.ReadBodySize(this.contentLayout);
        this.levelSizes = this.roles.Declared.TextLevels is null ? null : this.ReadLevelSizes(this.contentLayout);
        if (this.roles.Declared.ContentArea == PptxContentArea.Guides)
        {
            foreach (var master in layouts.Select(l => l.SlideMasterPart).OfType<SlideMasterPart>().Distinct())
            {
                this.Guides(master);
            }
        }

        this.closingLayout = ByName(layouts, names?.Closing, nameof(names.Closing))
            ?? layouts.FirstOrDefault(l => LayoutName(l) is { } name && ClosingNamePattern().IsMatch(name))
            ?? (builtIn ? this.sectionLayout : this.titleLayout);

        this.slideWidth = this.presentation.SlideSize?.Cx?.Value ?? 12192000;
        this.slideHeight = this.presentation.SlideSize?.Cy?.Value ?? 6858000;
        this.tableStyleId = this.presentationPart.TableStylesPart?.TableStyleList?.Default?.Value is { Length: > 0 } style
            ? style
            : BuiltInTableStyleId;
    }

    /// <summary>Whether the template numbers its chapters, unless front matter says otherwise.</summary>
    public bool NumbersChapters => this.roles.Declared.NumberChapters;

    /// <summary>
    /// The placeholders the layout named <paramref name="name"/> (case-insensitive, as <c>layout="…"</c> finds it) offers
    /// a slide that fills them, in the order they are filled; <see langword="null"/> on the built-in master or for a
    /// layout the template lacks, which the writer reports.
    /// </summary>
    public IReadOnlyList<SlotKind>? Slots(string name) =>
        this.builtIn || this.layouts.FirstOrDefault(l => string.Equals(LayoutName(l), name, StringComparison.OrdinalIgnoreCase)) is not { } layout
            ? null
            : [.. this.SlotsOf(layout).Select(s => s.Kind)];

    /// <summary>Where <see cref="Write"/> adds each slide it writes, split parts included, for a check; <see langword="null"/>: nowhere.</summary>
    public List<Slide>? Written { get; init; }

    /// <summary>The Markdown line each slide the last <see cref="Write"/> wrote starts at, split parts included; 0 if unknown.</summary>
    public IReadOnlyList<int> SlideLines => this.slideLines;

    /// <summary>The classification marking ("Confidential · Draft") in the top right corner of every slide; <see langword="null"/>: none.</summary>
    public string? Marking { get; init; }

    /// <summary>Whether <see cref="Marking"/> is confidential, and so in the danger colour.</summary>
    public bool Confidential { get; init; }

    /// <summary>
    /// Writes the slides, fitted, with their speaker notes, transitions and builds; returns how many were written.
    /// <paramref name="deckTitle"/> is the deck's title, for the footer pattern.
    /// </summary>
    public int Write(IReadOnlyList<Slide> slides, string? deckTitle = null)
    {
        this.deckTitle = deckTitle;
        this.slideLines.Clear();
        var number = 0;
        var speakerNotes = new List<(SlidePart Slide, IReadOnlyList<TextParagraph> Notes)>();
        foreach (var (slide, step) in slides.Select(this.Resolve).SelectMany(this.Fit))
        {
            this.current = slide;
            this.Written?.Add(slide);
            this.footnotes = slide.Footnotes;
            if (slide is TitleSlide { IsSection: true, Closing: false })
            {
                this.chapter = Plain(slide.Title);
            }

            var canvas = slide switch
            {
                TitleSlide title => this.WriteTitleSlide(title),
                ContentSlide content => this.WriteContentSlide(content, step),
                VisualSlide visual => this.WriteVisualSlide(visual, step),
                FlowSlide flow => this.WriteFlowSlide(flow, step),
                PlaceholderSlide placeholders => this.WritePlaceholderSlide(placeholders),
                KindSlide kind => this.WriteKindSlide(kind),
                SceneSlide scene => this.WriteSceneSlide(scene),
                _ => throw new InvalidOperationException("Unknown kind of slide."),
            };

            this.Footnotes(canvas, slide);
            number++;
            if (slide is not TitleSlide { IsSection: false } and not TitleSlide { Closing: true })
            {
                this.Footer(canvas, number, this.PaintsDark(slide) ? SlideColor.White : null);
            }

            this.Classification(canvas, slide);
            this.Tag(canvas);
            Transitions.Write(canvas.Part.Slide!, slide.Transition);
            TimingWriter.Write(canvas.Part.Slide!, canvas.Targets);
            SourceLines.Write(canvas.Part.Slide!, slide.Line);
            this.slideLines.Add(slide.Line);
            if (slide.SpeakerNotes.Count > 0)
            {
                speakerNotes.Add((canvas.Part, slide.SpeakerNotes));
            }
        }

        this.footnotes = [];

        // The notes master only for a deck with speaker notes, after the slides: a deck without them keeps its parts and ids.
        if (speakerNotes.Count > 0)
        {
            var master = this.NotesMaster();
            foreach (var (part, paragraphs) in speakerNotes)
            {
                this.WriteSpeakerNotes(part, master, paragraphs);
            }
        }

        if (this.presentation.SlideIdList is { HasChildren: false } empty)
        {
            empty.Remove();
        }

        return number;
    }

    // A cover or section layout without a title placeholder (such as some corporate dividers) takes the title in its
    // first body placeholder and the subtitle in the next one.
    // The closing slide writes only into the placeholders its layout has: a closing layout often has none.
    private Canvas WriteTitleSlide(TitleSlide slide)
    {
        var layout = slide.Closing ? this.closingLayout : this.LayoutOf(slide, slide.IsSection ? this.sectionLayout : this.titleLayout);
        var canvas = this.NewSlide(layout);
        var bodies = this.Bodies(layout).ToList();
        var titleInBody = FindPlaceholder(layout, IsTitle) is null && bodies.Count > 0;
        if (slide.Closing && FindPlaceholder(layout, IsTitle) is null && bodies.Count == 0)
        {
            return canvas;
        }

        this.Title(canvas, slide.Title, titleInBody ? bodies[0] : null);
        if (slide.Number is { } number)
        {
            this.ChapterNumber(canvas, layout, number);
        }

        if (slide.Subtitle.Count == 0)
        {
            return canvas;
        }

        var subtitle = FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.SubTitle && !this.roles.IsExcluded(layout, p)) ?? bodies.Skip(titleInBody ? 1 : 0).FirstOrDefault();
        if (subtitle is not null)
        {
            canvas.AddText(Placeholder(canvas, subtitle, "Subtitle", slide.Subtitle.Select(p => this.Paragraph(canvas, p, explicitBullets: false, null))), slide.Subtitle);
        }
        else if (!slide.Closing)
        {
            var title = (titleInBody ? Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, p => ReferenceEquals(p, bodies[0])) : null)
                ?? this.TitleArea(layout);
            this.TextBox(canvas, new Rect(title.X, title.Bottom + Gap, title.W, EstimateHeight(slide.Subtitle, TextSize, title.W)), slide.Subtitle, TextSize, center: false);
        }

        return canvas;
    }

    // "01", "02", …: in the layout's chapter-number placeholder, else in a text box above the title, in the heading font
    // at the subtitle's size and in the accent colour.
    private void ChapterNumber(Canvas canvas, SlideLayoutPart layout, int number)
    {
        var text = number.ToString("00", CultureInfo.InvariantCulture);
        if (this.roles.Placeholders(layout).ChapterNumber is { } placeholder)
        {
            canvas.Add(Placeholder(canvas, placeholder, "Chapter Number", [new A.Paragraph(new A.Run(new A.RunProperties(), new A.Text(text)), new A.EndParagraphRunProperties())]));
            return;
        }

        var subtitle = FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.SubTitle && !this.roles.IsExcluded(layout, p)) ?? this.Bodies(layout).FirstOrDefault();
        var size = this.LevelSize(layout, subtitle, 1) ?? TextSize;
        var title = this.TitleArea(layout);
        var height = LineHeight(size) + TextInsets;
        var paragraph = new A.Paragraph(
            new A.ParagraphProperties(new A.NoBullet()) { LeftMargin = 0, Indent = 0 },
            this.Run(canvas, new TextRun(text, RunStyle.None), size, font: "+mj-lt", color: this.palette.Accent),
            new A.EndParagraphRunProperties { FontSize = size });
        canvas.Add(TextBoxShape(canvas, new Rect(title.X, Math.Max(title.Y - height, 0), title.W, height), [paragraph]));
    }

    // The placeholders of a layout a placeholder slide fills, in order: the text ones (the subtitle first, then the
    // bodies in their order, `obj` and untyped ones among them, which also take visuals), then pictures, then charts.
    private List<(P.PlaceholderShape Shape, SlotKind Kind)> SlotsOf(SlideLayoutPart layout)
    {
        var found = this.roles.Placeholders(layout);
        var slots = new List<(P.PlaceholderShape Shape, SlotKind Kind)>();
        if (found.Subtitle is { } subtitle && !found.Bodies.Contains(subtitle))
        {
            slots.Add((subtitle, SlotKind.Text));
        }

        slots.AddRange(found.Bodies.Select(b => (b, b.Type is null || b.Type.Value == P.PlaceholderValues.Object ? SlotKind.Object : SlotKind.Text)));
        slots.AddRange(found.Pictures.Select(p => (p, SlotKind.Picture)));
        slots.AddRange(found.Charts.Select(c => (c, SlotKind.Chart)));
        return slots;
    }

    // Each text part in its placeholder, shrunk on its own; images cropped into picture placeholders, charts in chart
    // placeholders, anything in an `obj` body fitted into its box. Placeholders left over are not written.
    private Canvas WritePlaceholderSlide(PlaceholderSlide slide)
    {
        var layout = this.LayoutOf(slide, this.contentLayout);
        var canvas = this.NewSlide(layout);
        this.Title(canvas, slide.Title);
        var slots = this.SlotsOf(layout);
        var levels = this.roles.Declared.TextLevels;
        long bottom = 0;
        foreach (var (item, index) in PlaceholderFill.Distribute(slide.Items, [.. slots.Select(s => s.Kind)])?.FirstOrDefault() ?? [])
        {
            var (placeholder, kind) = slots[index];
            var box = this.PlaceholderBox(layout, placeholder);
            bottom = Math.Max(bottom, box.Bottom);
            if (item.Text is { } text)
            {
                var (step, scale, _) = this.PlaceholderFit(layout, placeholder, text);
                var properties = step == 0 ? null : new A.BodyProperties(new A.NormalAutoFit { FontScale = scale });
                canvas.AddText(Placeholder(canvas, placeholder, "Text", text.Select(p => this.Paragraph(canvas, p, explicitBullets: this.listBullets, null, levels: levels)), null, properties), text);
                continue;
            }

            var visual = item.Visual!;
            canvas.Building(visual.Build, () => kind switch
            {
                SlotKind.Picture => this.PicturePlaceholder(canvas, visual.Image!, placeholder, box, LayoutName(layout)),
                SlotKind.Chart => this.Chart(canvas, visual.Chart!, box, placeholder),
                _ => visual.Table is { } table ? this.Table(canvas, table, box with { H = 0 })
                    : visual.Image is { } image ? box with { H = this.Images(canvas, [image], box) }
                    : visual.Chart is { } chart ? this.Chart(canvas, chart, box)
                    : this.Drawing(canvas, visual.Drawing!, box, visual.Placement),
            });
        }

        this.footnoteTop = bottom > 0 ? bottom : null;
        return canvas;
    }

    // The box of a layout's placeholder: its own, else that of the master's placeholder it inherits from, else the content area.
    private Rect PlaceholderBox(SlideLayoutPart layout, P.PlaceholderShape placeholder) =>
        Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, p => ReferenceEquals(p, placeholder))
        ?? Bounds(layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, m =>
            placeholder.Index is { } index ? m.Index?.Value == index.Value : m.Type?.Value == (placeholder.Type?.Value ?? P.PlaceholderValues.Body))
        ?? this.FullContentArea(layout);

    // The step a text part is shrunk by in its placeholder (as text slides: 2 pt per step, down to 12 pt), the font scale
    // that gives, and whether it then fits.
    private (int Step, int Scale, bool Fits) PlaceholderFit(SlideLayoutPart layout, P.PlaceholderShape placeholder, IReadOnlyList<TextParagraph> text)
    {
        var box = this.PlaceholderBox(layout, placeholder);
        var size = this.LevelSize(layout, placeholder, 1) ?? TextSize;
        var maxStep = Math.Max((size - MinTextSize) / SizeStep, 0);
        int Size(int step) => Math.Max(size - (step * SizeStep), Math.Min(MinTextSize, size));
        var fits = Enumerable.Range(0, maxStep + 1).Cast<int?>().FirstOrDefault(s => EstimateHeight(text, Size(s!.Value), box.W) <= box.H);
        var step = fits ?? maxStep;
        return (step, (int)(Size(step) * 100000L / size), fits is not null);
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

        if (this.Bodies(layout).FirstOrDefault() is { } body)
        {
            var properties = step == 0 ? null : new A.BodyProperties(new A.NormalAutoFit { FontScale = (int)(this.BodySize(step) * 100000L / this.bodySize) });
            var box = this.footnotes.Count > 0 ? this.ContentArea(layout) : (Rect?)null; // shortened for the footnotes
            var levels = this.roles.Declared.TextLevels;
            canvas.AddText(Placeholder(canvas, body, "Content", slide.Body.Select(p => this.Paragraph(canvas, p, explicitBullets: this.listBullets, null, levels: levels)), box, properties), slide.Body);
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
        var placed = canvas.Building(slide.VisualBuild, () =>
        {
            if (slide.Table is { } table)
            {
                return this.Table(canvas, table, new Rect(area.X, y, area.W, 0), TableStartSize(table) - (step * SizeStep));
            }

            if (slide.Images is null && slide.Drawing is null && slide.Chart is null)
            {
                return new Rect(area.X, y, area.W, 0);
            }

            var room = area.Bottom - y - (captionHeight > 0 ? captionHeight + Gap : 0);
            var box = new Rect(area.X, y, area.W, Math.Max(room, area.H / 4));
            return slide.Images is { } images ? box with { H = this.Images(canvas, images, box, slide.VisualBuild) }
                : slide.Chart is { } chart ? this.Chart(canvas, chart, box)
                : this.Drawing(canvas, slide.Drawing!, box, slide.Placement);
        });

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

        var titleBox = slide.Kicker is { } kicker ? this.Kicker(canvas, layout, kicker, slide.Dark && !this.palette.DarkSlides) : (Rect?)null;
        if (!slide.NoTitle)
        {
            this.Title(canvas, slide.Title, null, titleBox, this.PaintsDark(slide) ? SlideColor.White : null);
        }

        var area = this.ContentArea(layout);
        this.flowArea = area;
        this.Stack(canvas, slide.Blocks, area, this.FlowStyleOf(slide, step));
        this.flowArea = null;
        return canvas;
    }

    // A small label in capitals at the top of the title area, in the accent colour (white on a dark slide of a light
    // deck); returns the title's box below it.
    private Rect Kicker(Canvas canvas, SlideLayoutPart layout, string kicker, bool white)
    {
        var titleArea = this.TitleArea(layout);
        var height = LineHeight(KickerSize) + TextInsets;
        var run = this.Run(canvas, new TextRun(kicker, RunStyle.Bold), KickerSize, color: white ? SlideColor.White : this.palette.Accent);
        run.RunProperties!.Capital = A.TextCapsValues.All;
        run.RunProperties.Spacing = 100;
        var paragraph = new A.Paragraph(new A.ParagraphProperties(new A.NoBullet()) { LeftMargin = 0, Indent = 0 }, run, new A.EndParagraphRunProperties { FontSize = KickerSize });
        canvas.Add(TextBoxShape(canvas, titleArea with { H = height }, [paragraph]));
        return titleArea with { Y = titleArea.Y + height, H = Math.Max(titleArea.H - height, height) };
    }

    // The template's own notes master, else the built-in one on a copy of the deck's theme: the brand's fonts and
    // colours on the built-in master, the template's theme fonts on a caller's template.
    private NotesMasterPart NotesMaster()
    {
        if (this.presentationPart.NotesMasterPart is { } own)
        {
            return own;
        }

        var part = this.presentationPart.AddNewPart<NotesMasterPart>();
        var theme = part.AddNewPart<ThemePart>();
        if ((this.presentationPart.ThemePart ?? this.presentationPart.SlideMasterParts.FirstOrDefault()?.ThemePart) is { } source)
        {
            using var stream = source.GetStream(FileMode.Open, FileAccess.Read);
            theme.FeedData(stream);
        }
        else
        {
            throw Damaged("it has no theme.");
        }

        NotesMasterBuilder.Write(part, this.slideWidth, this.slideHeight);
        this.presentation.NotesMasterIdList = new P.NotesMasterIdList(new P.NotesMasterId { Id = this.presentationPart.GetIdOfPart(part) });
        return part;
    }

    // A notes page: the slide image and the speaker notes in the body placeholder, lists with their own bullets, links
    // on the notes part, every run in the document's language.
    private void WriteSpeakerNotes(SlidePart slide, NotesMasterPart master, IReadOnlyList<TextParagraph> paragraphs)
    {
        var part = slide.AddNewPart<NotesSlidePart>();
        part.AddPart(master);
        part.AddPart(slide);
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties());
        var canvas = new Canvas(slide, tree, part);
        var id = canvas.NextId();
        canvas.Add(new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Slide Image " + id },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true, NoRotation = true, NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.SlideImage })),
            new P.ShapeProperties()));
        canvas.Add(Placeholder(
            canvas,
            new P.PlaceholderShape { Type = P.PlaceholderValues.Body, Index = 1U },
            "Notes",
            paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, null))));
        part.NotesSlide = new P.NotesSlide(new P.CommonSlideData(tree), new P.ColorMapOverride(new A.MasterColorMapping()));
        foreach (var properties in part.NotesSlide.Descendants<A.TextCharacterPropertiesType>())
        {
            if (properties is A.RunProperties or A.EndParagraphRunProperties)
            {
                properties.Language = this.language;
            }
        }
    }

    // Below the content area, which leaves room for them: each footnote with its number raised before it.
    private void Footnotes(Canvas canvas, Slide slide)
    {
        if (slide.Footnotes.Count == 0)
        {
            return;
        }

        var area = this.ContentArea(canvas.Part.SlideLayoutPart!);
        var paragraphs = FootnoteParagraphs(slide.Footnotes);
        var color = this.PaintsDark(slide) ? SlideColor.White : this.palette.Text;
        var top = (this.footnoteTop ?? area.Bottom) + (Gap / 2);
        this.footnoteTop = null;
        canvas.Add(TextBoxShape(
            canvas,
            new Rect(area.X, top, area.W, EstimateHeight(paragraphs, FootnoteSize, area.W)),
            paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, FootnoteSize, color: color))));
    }

    private static List<TextParagraph> FootnoteParagraphs(IReadOnlyList<SlideFootnote> footnotes) =>
        [.. footnotes.Select(f => new TextParagraph([new TextRun(f.Number.ToString(CultureInfo.InvariantCulture), RunStyle.Superscript), new TextRun(" ", RunStyle.None), .. f.Text]))];

    // The height the footnotes take at the foot of the content area, the gap above them included.
    private long FootnoteRoom(long width) =>
        this.footnotes.Count == 0 ? 0 : EstimateHeight(FootnoteParagraphs(this.footnotes), FootnoteSize, width) + (Gap / 2);

    // The marking in a text box named Classification, on top of everything: right-aligned at the right edge of the content
    // layout's title, hanging from the top edge (on the built-in cover below the logo), bold at the footer size. In the
    // footer colour, a confidential one in the danger colour; on dark slides white, a confidential one the danger colour
    // lightened halfway to white.
    private void Classification(Canvas canvas, Slide slide)
    {
        if (this.Marking is not { } marking)
        {
            return;
        }

        var dark = this.palette.DarkSlides || slide is FlowSlide { Dark: true } || TemplateTone.IsDark(canvas.Part.SlideLayoutPart!);
        var danger = this.palette.Labels[LabelCategory.Danger];
        var color = this.Confidential
            ? new SlideColor(dark ? ResolvedBrand.Mix(danger, 0.5) : danger)
            : dark ? SlideColor.White : this.palette.Footer;
        var text = XmlSafe(marking);
        const int size = TemplateBuilder.FooterSize;
        var width = (long)(text.Length * size / 100.0 * 0.66 * EmuPerPoint);
        var height = LineHeight(size);
        var right = this.TitleArea(this.contentLayout).Right;
        var top = this.builtIn && slide is TitleSlide { IsSection: false, Closing: false } ? TemplateBuilder.CoverLogoBottom + (Gap / 2) : ClassificationTop;
        var id = canvas.NextId();
        canvas.Add(new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Classification" },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new Rect(Math.Max(right - width, 0), top, Math.Min(width, right), height).Transform(),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.NoFill()),
            new P.TextBody(
                new A.BodyProperties(new A.NoAutoFit()) { Wrap = A.TextWrappingValues.None, LeftInset = 0, RightInset = 0, TopInset = 0, BottomInset = 0 },
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Right },
                    new A.Run(new A.RunProperties(color.Fill()) { FontSize = size, Bold = true }, new A.Text(text)),
                    new A.EndParagraphRunProperties { FontSize = size }))));
    }

    // Every run and paragraph end in the document's language, for spell checking; drawings and tables included.
    private void Tag(Canvas canvas)
    {
        foreach (var properties in canvas.Part.Slide!.Descendants<A.TextCharacterPropertiesType>())
        {
            if (properties is A.RunProperties or A.EndParagraphRunProperties)
            {
                properties.Language = this.language;
            }
        }
    }

    // On a template whose slides are all dark, a dark slide keeps the template's background.
    private bool PaintsDark(Slide slide) => slide is FlowSlide { Dark: true } && this.darkLayout is null && !this.palette.DarkSlides;

    // Slide number and date, where the layout has placeholders for them; their position and style come from there. The
    // footer text, or the footer pattern filled in, goes into the footer placeholder.
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

        var footer = this.footerPattern is { } pattern
            ? PptxTemplateRoles.FormatFooter(pattern, field => field switch
            {
                "footer" => this.footerText,
                "title" => this.deckTitle,
                "chapter" => this.chapter,
                _ => this.author,
            }) is { Length: > 0 } filled ? filled : null
            : this.footerText;
        if (footer is not null && FindPlaceholder(layout, p => p.Type?.Value == P.PlaceholderValues.Footer) is { } placeholder)
        {
            canvas.Add(Placeholder(canvas, placeholder, "Footer", [new A.Paragraph(new A.Run(Properties(), new A.Text(XmlSafe(footer))), new A.EndParagraphRunProperties())]));
        }
    }

    // Blocks top to bottom with a gap between them; returns the height they take. Without a canvas it only measures.
    // Images and drawings share the height the other blocks leave, each at least a quarter of the slide.
    // A conclusion, the last block, sits at the foot of the box; the others share the room above it.
    private long Stack(Canvas? canvas, IReadOnlyList<FlowBlock> blocks, Rect box, FlowStyle style)
    {
        if (blocks.Count > 0 && blocks[^1] is ConclusionBlock conclusion)
        {
            var others = blocks.Take(blocks.Count - 1).ToList();
            var height = this.Place(null, conclusion, box with { H = 0 }, style);
            var used = others.Count == 0 ? 0 : this.Stack(canvas, others, box with { H = Math.Max(box.H - height - style.BlockGap, 0) }, style) + style.BlockGap;
            var top = Math.Max(box.Bottom - height, box.Y + used);
            if (canvas is not null)
            {
                this.Place(canvas, conclusion, new Rect(box.X, top, box.W, height), style);
            }

            return canvas is null ? used + height : top + height - box.Y;
        }

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
        CommentBlock comment => IsVisual(comment.Target),
        _ => false,
    };

    // A block with a build marker: every shape written for it is part of its build. A comment's marker is its callout's.
    private long Place(Canvas? canvas, FlowBlock block, Rect box, FlowStyle style) =>
        canvas is not null && block.Build is { } mark && block is not CommentBlock
            ? canvas.Building(mark, () => this.PlaceBlock(canvas, block, box, style))
            : this.PlaceBlock(canvas, block, box, style);

    private long PlaceBlock(Canvas? canvas, FlowBlock block, Rect box, FlowStyle style) => block switch
    {
        TextBlock text => this.FlowText(canvas, text.Paragraphs, box, style.Size, style.Text),
        TableBlock table => this.Table(canvas, table.Table, box).H,
        ImageBlock image => this.Images(canvas, [image.Image], this.VisualBox(box)),
        ImageRowBlock row => this.Images(canvas, row.Images, this.VisualBox(box), row.Build),
        ColumnsBlock columns => this.Columns(canvas, columns, box, style),
        ChartBlock chart => this.Chart(canvas, chart.Chart, this.VisualBox(box)).H,
        DrawingBlock drawing => this.Drawing(canvas, drawing.Drawing, this.VisualBox(box), drawing.Placement).H,
        CardsBlock cards => this.Cards(canvas, cards, box, style),
        RowsBlock rows => this.Rows(canvas, rows, box, style),
        PanelBlock panel => this.Panel(canvas, panel, box, style),
        CalloutBlock callout => this.Callout(canvas, callout, box, style),
        StatementBlock statement => this.FlowText(canvas, [new TextParagraph(Bold(statement.Text))], box, style.Scaled(StatementSize), this.Heading(style)),
        TagsBlock tags => this.Tags(canvas, tags, box, style),
        CodeLinesBlock code => this.Code(canvas, code, box, style),
        LabelledTagsBlock labelled => this.LabelledTags(canvas, labelled, box, style),
        IconListBlock icons => this.IconList(canvas, icons, box, style),
        ConclusionBlock conclusion => this.Conclusion(canvas, conclusion, box, style),
        CommentBlock comment => this.Comment(canvas, comment, box, style),
        BylineBlock byline => this.Byline(canvas, byline, box, style),
        _ => throw new InvalidOperationException("Unknown kind of block."),
    };

    // An image or drawing gets the room left, but at least a quarter of the slide's height.
    private Rect VisualBox(Rect box) => box with { H = Math.Max(box.H, this.slideHeight / 4) };

    private long FlowText(Canvas? canvas, IReadOnlyList<TextParagraph> paragraphs, Rect box, int size, SlideColor? color)
    {
        var height = FlowHeight(paragraphs, size, box.W);
        canvas?.AddText(TextBoxShape(canvas, box with { H = height }, paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, size, color: color))), paragraphs);
        return height;
    }

    // Images side by side in cells of equal width, each fitted into its cell and centred in it, its alt text under it
    // when it asks for a caption; more than four wrap into further rows that share the height. Returns the height.
    // With a `build` marker, each image with its caption is an item of it, numbered from `first`.
    private long Images(Canvas? canvas, IReadOnlyList<SlideImage> images, Rect box, BuildMark? build = null, int first = 0)
    {
        if (images.Count > MaxImagesPerRow)
        {
            var rows = images.Chunk(MaxImagesPerRow).ToList();
            var rowHeight = Math.Max((box.H - ((rows.Count - 1) * Gap)) / rows.Count, 0);
            var y = box.Y;
            for (var r = 0; r < rows.Count; r++)
            {
                y += this.Images(canvas, rows[r], box with { Y = y, H = rowHeight }, build, first + (r * MaxImagesPerRow)) + Gap;
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
                canvas.Item(build, first + i);
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
            canvas?.Item(block.Build, i);
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
        var index = 0;
        foreach (var row in block.Cards.Chunk(columns))
        {
            var height = row.Max(card => this.Card(null, card, new Rect(0, 0, width, 0), inner));
            if (canvas is not null)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    canvas.Item(block.Build, index++);
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
        var y = inner.Y;
        if (card.Photo is { } photo)
        {
            y += this.Picture(canvas, photo, inner with { H = Math.Min(inner.W, this.slideHeight / 5) }).H + TextInsets;
        }

        y += this.FlowText(canvas, [new TextParagraph(Bold(card.Title))], inner with { Y = y }, style.Scaled(CardTitleSize), this.Heading(style));
        if (card.Figure is { } figure)
        {
            y += this.FlowText(canvas, [new TextParagraph(Bold(figure))], inner with { Y = y }, style.Scaled(FigureSize), style.Dark && !this.palette.DarkSlides ? SlideColor.White : this.palette.Accent);
        }
        else if (card.FigureIcon is { } icon)
        {
            // As tall as a line of figure text, so a card with an icon lines up with one with a figure.
            var side = LineHeight(style.Scaled(FigureSize));
            this.Icon(canvas, icon, inner.X, y + (TextInsets / 2), side, this.IconColour(style));
            y += side + TextInsets;
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
        foreach (var (index, row) in block.Rows.Index())
        {
            canvas?.Item(block.Build, index);
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
        var lines = code.Lines.Sum(line => EstimateLines(line.Count > 0 ? line : [new TextRun(string.Empty, RunStyle.Code)], style.Scaled(FlowSmallSize), inner));
        var height = (long)(lines * CodeLineSpacing / 100000.0) + TextInsets + (2 * RowPad);
        if (canvas is not null)
        {
            canvas.Add(Shape(canvas, box with { H = height }, A.ShapeTypeValues.RoundRectangle, style.Dark ? this.palette.DarkSurface : this.palette.Code, null));
            // Highlighted runs on the dark box take the colours for dark code.
            this.codeOnDark = style.Dark;
            var paragraphs = code.Lines.Select(line =>
            {
                var paragraph = this.Paragraph(canvas, new TextParagraph(line), explicitBullets: true, style.Scaled(FlowSmallSize), color: style.Text);
                paragraph.ParagraphProperties!.PrependChild(new A.LineSpacing(new A.SpacingPercent { Val = CodeLineSpacing }));
                return paragraph;
            }).ToList();
            this.codeOnDark = false;
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
            canvas?.Item(block.Build, i);
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

    // Each item a row: its icon in the accent colour, as tall as the first line, the item's text beside it.
    private long IconList(Canvas? canvas, IconListBlock block, Rect box, FlowStyle style)
    {
        var side = LineHeight(style.Size);
        var text = new Rect(box.X + side + Gap, 0, box.W - side - Gap, 0);
        var y = box.Y;
        for (var i = 0; i < block.Items.Count; i++)
        {
            if (i > 0)
            {
                y += ChipGap;
            }

            var item = block.Items[i];
            canvas?.Item(block.Build, i);
            var paragraph = new TextParagraph(item.Text);
            var height = Math.Max(side + TextInsets, FlowHeight([paragraph], style.Size, text.W));
            if (canvas is not null)
            {
                // The text box's first line starts below its top inset.
                this.Icon(canvas, item.Icon, box.X, y + (TextInsets / 2), side, this.IconColour(style));
                this.FlowText(canvas, [paragraph], text with { Y = y }, style.Size, style.Text);
            }

            y += height;
        }

        return y - box.Y;
    }

    // An icon as a group of editable shapes, `side` EMU square, every stroke and fill in one colour.
    private void Icon(Canvas? canvas, Drawing icon, long x, long y, long side, SlideColor colour) =>
        canvas?.Add(DrawingWriter.Group(icon, x, y, side / Math.Max(icon.Width, icon.Height), canvas.NextId, this.tableFont, this.codeFont, colour, "Icon "));

    // Icons take the accent colour, white on a dark block of a light deck, like a card's figure.
    private SlideColor IconColour(FlowStyle style) => style.Dark && !this.palette.DarkSlides ? SlideColor.White : this.palette.Accent;

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
        foreach (var run in title.Where(r => r.Icon is null))
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

    private void TextBox(Canvas canvas, Rect box, IReadOnlyList<TextParagraph> paragraphs, int size, bool center) =>
        canvas.AddText(TextBoxShape(canvas, box, paragraphs.Select(p => this.Paragraph(canvas, p, explicitBullets: true, size, center ? CellAlignment.Center : CellAlignment.Default, color: this.palette.Text))), paragraphs);

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
        var placeholder = Reference(layoutPlaceholder);
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
    // With `levels` (a template's text levels, in placeholders) the paragraph goes to its mapped level, a heading in that
    // level's style without the bold; bullets and indents come from the level, `explicitBullets` adds only the bullet.
    private A.Paragraph Paragraph(Canvas canvas, TextParagraph paragraph, bool explicitBullets, int? size, CellAlignment alignment = CellAlignment.Default, string? font = null, SlideColor? color = null, PptxTextLevels? levels = null)
    {
        var level = levels is null ? Math.Min(paragraph.Level, 8) : MappedLevel(paragraph, levels);
        var properties = new A.ParagraphProperties();
        if (level > 0)
        {
            properties.Level = level;
        }

        if (levels is not null)
        {
            return this.LevelParagraph(canvas, paragraph, properties, explicitBullets, levels);
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
            case ParagraphKind.Task or ParagraphKind.TaskDone:
                // Always its own box, also in placeholders, where the template supplies only the indents.
                if (explicitBullets)
                {
                    properties.LeftMargin = (int)((level + 1) * BulletIndent);
                    properties.Indent = (int)-BulletIndent;
                }

                properties.Append(new A.BulletFont { Typeface = TaskFont, PitchFamily = 2, CharacterSet = 2 }, new A.CharacterBullet { Char = paragraph.Kind == ParagraphKind.TaskDone ? TickedBox : EmptyBox });
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
        // An icon inside text cannot be drawn on a slide: left out (and reported by the parser).
        foreach (var run in paragraph.Runs.Where(r => r.Icon is null))
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

    // A paragraph at a template's text level: plain text without a bullet, a heading in the level's own style, list
    // items with the level's bullets (or Markdown's with `explicitBullets`), numbered and task items as always.
    private A.Paragraph LevelParagraph(Canvas canvas, TextParagraph paragraph, A.ParagraphProperties properties, bool explicitBullets, PptxTextLevels levels)
    {
        switch (paragraph.Kind)
        {
            case ParagraphKind.Plain when !paragraph.Heading:
                properties.Indent = 0;
                properties.Append(new A.NoBullet());
                break;
            case ParagraphKind.Bullet when explicitBullets:
                properties.Append(new A.BulletFont { Typeface = "Arial" }, new A.CharacterBullet { Char = (MappedLevel(paragraph, levels) - (levels.Bullet - 1)) % 2 == 0 ? "\u2022" : "\u2013" });
                break;
            case ParagraphKind.Numbered:
                var numbering = new A.AutoNumberedBullet { Type = A.TextAutoNumberSchemeValues.ArabicPeriod };
                if (paragraph.NumberStart != 1)
                {
                    numbering.StartAt = paragraph.NumberStart;
                }

                properties.Append(new A.BulletFont { Typeface = "+mj-lt" }, numbering);
                break;
            case ParagraphKind.Task or ParagraphKind.TaskDone:
                properties.Append(new A.BulletFont { Typeface = TaskFont, PitchFamily = 2, CharacterSet = 2 }, new A.CharacterBullet { Char = paragraph.Kind == ParagraphKind.TaskDone ? TickedBox : EmptyBox });
                break;
        }

        var result = new A.Paragraph(properties);
        // A heading's bold is the parser's; coloured words keep theirs.
        foreach (var run in paragraph.Heading ? paragraph.Runs.Select(r => r.Colour is null ? r with { Style = r.Style & ~RunStyle.Bold } : r) : paragraph.Runs)
        {
            result.Append(run.IsBreak ? new A.Break() : this.Run(canvas, run, null));
        }

        result.Append(new A.EndParagraphRunProperties());
        return result;
    }

    // The 0-based outline level of a paragraph under a template's 1-based text levels: a heading at its level, a
    // paragraph at its level (inside a nested list item: the item's level), a list item at the bullet level plus its depth.
    private static int MappedLevel(TextParagraph paragraph, PptxTextLevels levels) =>
        Math.Min(
            paragraph.Heading ? levels.Heading - 1
                : paragraph.Kind == ParagraphKind.Plain && paragraph.Level == 0 ? levels.Paragraph - 1
                : levels.Bullet - 1 + paragraph.Level,
            8);

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

        if (run.Style.HasFlag(RunStyle.Superscript))
        {
            properties.Baseline = 30000;
        }

        if (size is { } fontSize)
        {
            properties.FontSize = fontSize;
        }

        if (run.Style.HasFlag(RunStyle.Mark))
        {
            properties.Bold = true;
        }

        if ((run.Token is { } token && this.palette.CodeColour(token, this.codeOnDark) is { } tokenColor ? tokenColor
            : run.Colour is { } category ? this.palette.WordColour(category)
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
        else if (run.Symbol)
        {
            // In every script slot: PowerPoint may take geometric shapes for East Asian or complex script text.
            properties.Append(new A.LatinFont { Typeface = HarveyBall.SymbolFont });
            properties.Append(new A.EastAsianFont { Typeface = HarveyBall.SymbolFont });
            properties.Append(new A.ComplexScriptFont { Typeface = HarveyBall.SymbolFont });
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

    // Without a canvas, only where it would go. `fit=cover` fills the box and `.round` makes it a square, both cropped
    // by `a:srcRect` around the attributes' focus; `.round` is then an ellipse, `.grey` a greyscale effect on the blip.
    private Rect Picture(Canvas? canvas, SlideImage image, Rect box)
    {
        var info = ImageInfo.Read(image.Resource.Content.Span)
            ?? throw new DocumentConversionException(Format.Pptx, $"The image '{image.Resource.Path}' is not a PNG, JPEG or GIF.");
        var (naturalWidth, naturalHeight) = image.Natural ?? (info.Width, info.Height);
        var attributes = image.Attributes;
        var (shownWidth, shownHeight) = attributes.Round ? (Math.Min(naturalWidth, naturalHeight), Math.Min(naturalWidth, naturalHeight)) : (naturalWidth, naturalHeight);
        var width = shownWidth * EmuPerPixel;
        var height = shownHeight * EmuPerPixel;
        var fit = Math.Min(image.Scale ?? this.options.MaxImageUpscale, Math.Min(box.W / width, box.H / height));
        var cover = attributes.Fit == ImageFit.Cover;
        var side = Math.Min(box.W, box.H);
        var placed = !cover ? new Rect(box.X + (long)((box.W - (width * fit)) / 2), box.Y, (long)(width * fit), (long)(height * fit))
            : attributes.Round ? new Rect(box.X + ((box.W - side) / 2), box.Y, side, side)
            : box;
        if (canvas is null)
        {
            return placed;
        }

        var crop = cover || attributes.Round
            ? attributes.Crop(naturalWidth, naturalHeight, placed.H > 0 ? placed.W / (double)placed.H : 1)
            : default;
        if (cover)
        {
            var (visibleWidth, visibleHeight) = crop.Visible;
            fit = Math.Max(placed.W / (naturalWidth * EmuPerPixel * visibleWidth), placed.H / (naturalHeight * EmuPerPixel * visibleHeight));
        }

        this.Enlarged(image, fit);
        var relationshipId = this.ImageRelationship(canvas, image, info);
        var id = canvas.NextId();
        canvas.Add(new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Picture " + id, Description = XmlSafe(image.AltText) },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            BlipFill(this.Blip(canvas, image, relationshipId), crop),
            new P.ShapeProperties(
                placed.Transform(),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = attributes.Round ? A.ShapeTypeValues.Ellipse : A.ShapeTypeValues.Rectangle })));
        return placed;
    }

    // An image in a picture placeholder: cropped to the placeholder's shape around its focus (the centre by default),
    // without a position of its own, so that it takes the layout's box. Cutting off more than a tenth is reported.
    private Rect PicturePlaceholder(Canvas canvas, SlideImage image, P.PlaceholderShape placeholder, Rect box, string? layout)
    {
        var info = ImageInfo.Read(image.Resource.Content.Span)
            ?? throw new DocumentConversionException(Format.Pptx, $"The image '{image.Resource.Path}' is not a PNG, JPEG or GIF.");
        var (naturalWidth, naturalHeight) = image.Natural ?? (info.Width, info.Height);
        var crop = image.Attributes.Crop(naturalWidth, naturalHeight, box.H > 0 ? box.W / (double)box.H : 1);
        var (visibleWidth, visibleHeight) = crop.Visible;
        var line = this.current?.Line ?? 0;
        if (Math.Max(1 - visibleWidth, 1 - visibleHeight) > 0.1)
        {
            var side = visibleWidth < visibleHeight ? "width" : "height";
            var share = Math.Round(100 * Math.Max(1 - visibleWidth, 1 - visibleHeight));
            this.notes.Add(
                NoteCodes.ImageCropped,
                line,
                $"The image '{image.Resource.Path}' is cut by {share.ToString(CultureInfo.InvariantCulture)} % of its {side} to fill the picture placeholder of the layout '{layout}'. "
                + "Use an image of the placeholder's shape, or focus=top, bottom, left or right to choose the part that stays.");
        }

        if (image.Scale is not null)
        {
            this.notes.Add(NoteCodes.AttributeIgnored, line, $"The scale of the image '{image.Resource.Path}' has no effect: it fills the picture placeholder of the layout '{layout}'.");
        }

        this.Enlarged(image, Math.Max(box.W / (naturalWidth * EmuPerPixel * visibleWidth), box.H / (naturalHeight * EmuPerPixel * visibleHeight)));
        var relationshipId = this.ImageRelationship(canvas, image, info);
        var id = canvas.NextId();
        canvas.Add(new P.Picture(
            new P.NonVisualPictureProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Picture " + id, Description = XmlSafe(image.AltText) },
                new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoGrouping = true, NoChangeAspect = true }),
                new P.ApplicationNonVisualDrawingProperties(Reference(placeholder))),
            BlipFill(this.Blip(canvas, image, relationshipId), crop),
            new P.ShapeProperties()));
        return box;
    }

    // A converted SVG is drawn as a vector by PowerPoint, so only its PNG would blur.
    private void Enlarged(SlideImage image, double fit)
    {
        if (fit > MaxSharpUpscale && image.Scale is null && image.Vector is null)
        {
            this.notes.Add(
                NoteCodes.ImageEnlarged,
                this.current?.Line ?? 0,
                $"The image '{image.Resource.Path}' is drawn at {fit.ToString("0.#", CultureInfo.InvariantCulture)} times its size and may look blurred. Use a larger image, or \"scale=1\" in its title to keep its size.");
        }
    }

    // The relationship from the slide to the image's part, which is added once per deck.
    private string ImageRelationship(Canvas canvas, SlideImage image, ImageInfo info)
    {
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

        return canvas.Part.Parts.Where(p => p.OpenXmlPart == part).Select(p => p.RelationshipId).FirstOrDefault()
            ?? canvas.Part.CreateRelationshipToPart(part);
    }

    // A placeholder of the slide that inherits from `layoutPlaceholder`: its type and index.
    private static P.PlaceholderShape Reference(P.PlaceholderShape layoutPlaceholder)
    {
        var placeholder = new P.PlaceholderShape();
        if (layoutPlaceholder.Type is { } type)
        {
            placeholder.Type = type.Value;
        }

        if (layoutPlaceholder.Index is { } index)
        {
            placeholder.Index = index.Value;
        }

        return placeholder;
    }

    // The crop as `a:srcRect`, in thousandths of a percent of each side; none when nothing is cut off.
    private static P.BlipFill BlipFill(A.Blip blip, ImageCrop crop)
    {
        var fill = new P.BlipFill(blip);
        if (!crop.IsNone)
        {
            static int Share(double share) => (int)Math.Round(share * 100000);
            fill.Append(new A.SourceRectangle { Left = Share(crop.Left), Top = Share(crop.Top), Right = Share(crop.Right), Bottom = Share(crop.Bottom) });
        }

        fill.Append(new A.Stretch(new A.FillRectangle()));
        return fill;
    }

    // The picture's PNG, JPEG or GIF; for a converted SVG also the SVG, which PowerPoint 2016 and later draw instead.
    private A.Blip Blip(Canvas canvas, SlideImage image, string relationshipId)
    {
        var blip = new A.Blip { Embed = relationshipId };

        // PowerPoint's "Grayscale" colour; whether it also greys the SVG drawn instead is an Office check (E04-S05).
        if (image.Attributes.Grey)
        {
            blip.Append(new A.Grayscale());
        }

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
        blip.Append(new A.BlipExtensionList(new A.BlipExtension(new SVGBlip { Embed = svgId }) { Uri = SvgBlip.Uri }));
        return blip;
    }

    // A native chart filling the box's width, at most about 16:10 tall, so that it neither stretches nor squashes. Its
    // source takes a line off that height, in a text box under it in the table font at the smallest table size. In a
    // chart `placeholder` it fills the placeholder's box, less the source's line.
    private Rect Chart(Canvas? canvas, ChartSpec chart, Rect box, P.PlaceholderShape? placeholder = null)
    {
        var source = chart.Source is { } text ? new TextParagraph([new TextRun($"{this.texts.Source}: {text}", RunStyle.None)]) : null;
        var sourceHeight = source is null ? 0 : EstimateHeight([source], MinTableSize, box.W);
        var placed = box with { H = Math.Max(placeholder is null ? Math.Min(box.H - sourceHeight, box.W * 5 / 8) : box.H - sourceHeight, 0) };
        if (canvas is null)
        {
            return placed with { H = placed.H + sourceHeight };
        }

        var relationshipId = ChartWriter.Add(
            canvas.Part,
            chart,
            this.palette.Text?.Fill().OuterXml,
            (this.palette.DarkSlides ? SlideColor.White.Translucent(30) : new SlideColor("D9D9D9")).Fill().OuterXml,
            this.tableFont);
        var id = canvas.NextId();
        canvas.Add(new P.GraphicFrame(
            new P.NonVisualGraphicFrameProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = "Chart " + id },
                placeholder is null ? new P.NonVisualGraphicFrameDrawingProperties() : new P.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoGrouping = true }),
                placeholder is null ? new P.ApplicationNonVisualDrawingProperties() : new P.ApplicationNonVisualDrawingProperties(Reference(placeholder))),
            new P.Transform(new A.Offset { X = placed.X, Y = placed.Y }, new A.Extents { Cx = placed.W, Cy = placed.H }),
            new A.Graphic(new A.GraphicData(new DocumentFormat.OpenXml.Drawing.Charts.ChartReference { Id = relationshipId }) { Uri = ChartGraphicUri })));

        // A layout's own chart-source placeholder (E01-S06) would take the text here instead of a text box.
        if (source is not null)
        {
            var color = this.current is { } slide && this.PaintsDark(slide) ? SlideColor.White : this.palette.Text;
            canvas.Add(TextBoxShape(
                canvas,
                new Rect(placed.X, placed.Bottom, placed.W, sourceHeight),
                [this.Paragraph(canvas, source, explicitBullets: true, MinTableSize, font: this.tableFont, color: color)]));
        }

        return placed with { H = placed.H + sourceHeight };
    }

    // Fitted into the box like a picture: centred, enlarged at most by half, so that its text keeps a sensible size.
    // A QR code keeps to a third of the width, or with a scale to its own size.
    private Rect Drawing(Canvas? canvas, Drawing drawing, Rect box, DrawingPlacement placement)
    {
        drawing.Validate();
        var fit = Math.Min(placement.MaxUpscale, Math.Min(box.W * placement.MaxWidthShare / (drawing.Width * EmuPerPixel), box.H / (drawing.Height * EmuPerPixel)));
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
                .Select(c => table.Rows.Select((row, r) => RunMetrics.LongestWord(row[c], atSize, Header(r))).Max() + CellInsets)
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
                (long)(Enumerable.Range(0, columns).Max(c => RunMetrics.Lines(row[c], size, Header(r), widths[c] - CellInsets)) * lineHeight) + 91440))
            .ToArray();
        return (size, widths, heights);
    }

    private Rect TitleArea(SlideLayoutPart layout) =>
        Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, IsTitle)
        ?? Bounds(layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, IsTitle)
        ?? new Rect(this.slideWidth / 20, this.slideHeight / 25, this.slideWidth * 9 / 10, this.slideHeight * 3 / 20);

    // Less the room the current slide's footnotes take at its foot.
    private Rect ContentArea(SlideLayoutPart layout)
    {
        var area = this.FullContentArea(layout);
        var room = this.FootnoteRoom(area.W);
        return room == 0 ? area : area with { H = Math.Max(area.H - room, area.H / 2) };
    }

    private Rect FullContentArea(SlideLayoutPart layout)
    {
        if (this.roles.Declared.ContentArea == PptxContentArea.Guides && layout.SlideMasterPart is { } guided)
        {
            return this.GuideArea(layout, guided);
        }

        if (Bounds(layout.SlideLayout?.CommonSlideData?.ShapeTree, p => IsBody(p) && !this.roles.IsExcluded(layout, p)) is { } own)
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

    // The content grid of `contentArea: guides`: between the outermost vertical guides of the master, and from the first
    // horizontal guide below the layout's title area to the last one above the master's footer placeholders.
    private Rect GuideArea(SlideLayoutPart layout, SlideMasterPart master)
    {
        var (vertical, horizontal) = this.Guides(master);
        var titleBottom = this.TitleArea(layout).Bottom;
        var footerTop = (master.SlideMaster?.CommonSlideData?.ShapeTree?.Descendants<P.Shape>() ?? [])
            .Where(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value is { } type
                && (type == P.PlaceholderValues.Footer || type == P.PlaceholderValues.DateAndTime || type == P.PlaceholderValues.SlideNumber))
            .Select(s => s.ShapeProperties?.Transform2D?.Offset?.Y?.Value)
            .OfType<long>()
            .DefaultIfEmpty(this.slideHeight)
            .Min();
        var top = horizontal.Where(y => y >= titleBottom).Cast<long?>().FirstOrDefault();
        var bottom = horizontal.Where(y => y <= footerTop).Cast<long?>().LastOrDefault();
        if (top is not { } y0 || bottom is not { } y1 || y1 <= y0)
        {
            throw new DocumentConversionException(
                Format.Pptx,
                $"The content area of {this.templateName} is set to its guides, but the master has no horizontal guide below the title of the layout '{LayoutName(layout)}' and another above the footer.");
        }

        return new Rect(vertical[0], y0, vertical[^1] - vertical[0], y1 - y0);
    }

    // The drawing guides of a master (p15:sldGuideLst in its extensions), in EMU and sorted: positions are in eighths of a
    // point, and a guide without `orient` is horizontal. At least two of each, else the template cannot give a grid.
    private (List<long> Vertical, List<long> Horizontal) Guides(SlideMasterPart master)
    {
        const string P15 = "http://schemas.microsoft.com/office/powerpoint/2012/main";
        var guides = master.SlideMaster?.Descendants<DocumentFormat.OpenXml.OpenXmlElement>()
            .Where(e => e.LocalName == "guide" && e.NamespaceUri == P15)
            .Select(e => (
                Vertical: e.GetAttributes().Any(a => a.LocalName == "orient" && a.Value == "vert"),
                Position: e.GetAttributes().Where(a => a.LocalName == "pos").Select(a => long.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0).FirstOrDefault() * EmuPerPoint / 8))
            .ToList() ?? [];
        var vertical = guides.Where(g => g.Vertical).Select(g => g.Position).Order().ToList();
        var horizontal = guides.Where(g => !g.Vertical).Select(g => g.Position).Order().ToList();
        if (vertical.Count < 2 || horizontal.Count < 2)
        {
            throw new DocumentConversionException(
                Format.Pptx,
                $"The content area of {this.templateName} is set to its guides, but its slide master has {vertical.Count} vertical and {horizontal.Count} horizontal drawing guides; it needs at least two of each.");
        }

        return (vertical, horizontal);
    }

    // The placeholders of a layout that take body text, in tree order: not those declared decorative or with another role.
    private IEnumerable<P.PlaceholderShape> Bodies(SlideLayoutPart layout) =>
        Placeholders(layout).Where(p => IsBody(p) && !this.roles.IsExcluded(layout, p));

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

    private static bool IsBody(P.PlaceholderShape placeholder) => TemplateRoles.IsBody(placeholder);

    private static long EstimateHeight(IEnumerable<TextParagraph> paragraphs, int size, long width) =>
        paragraphs.Sum(p => EstimateLines(p.Runs, size, width - ((p.Level + (p.Kind == ParagraphKind.Plain ? 0 : 1)) * BulletIndent))
            + (long)(size / 100.0 * 0.3 * EmuPerPoint)) + CellInsets;

    // Height of the lines a text needs at the given width, wrapping between words as measured by RunMetrics.
    private static long EstimateLines(IReadOnlyList<TextRun> runs, int size, long width) =>
        (long)(RunMetrics.Lines(runs, size, false, width) * size / 100.0 * 1.2 * EmuPerPoint);

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

    /// <summary>
    /// The slide being written: its part, its shape tree, the ids used on it, and what its build markers cover.
    /// <paramref name="links"/> is the part hyperlinks go on, when it is not the slide's: the notes page of its speaker notes.
    /// </summary>
    private sealed class Canvas(SlidePart part, P.ShapeTree tree, OpenXmlPart? links = null)
    {
        private readonly Dictionary<string, string?> hyperlinks = new(StringComparer.Ordinal);
        private readonly List<BuildTarget> targets = [];

        // The marked blocks being written, innermost last, each with the item being written.
        private readonly List<(BuildMark Mark, int Item)> building = [];
        private uint nextId = 2;

        public SlidePart Part => part;

        /// <summary>The shapes and paragraphs the slide's build markers cover, in the order they were written.</summary>
        public IReadOnlyList<BuildTarget> Targets => this.targets;

        public uint NextId() => this.nextId++;

        /// <summary>Adds a shape; while a marked block is written, it becomes part of that block's build.</summary>
        public void Add(OpenXmlElement shape)
        {
            tree.Append(shape);
            if (this.building.Count > 0)
            {
                var (mark, item) = this.building[^1];
                this.targets.Add(new BuildTarget(mark, item, IdOf(shape), shape switch
                {
                    P.Shape => TargetKind.Shape,
                    P.GraphicFrame => TargetKind.Graphic,
                    _ => TargetKind.Other,
                }, null));
            }
        }

        /// <summary>Adds a shape with text whose paragraphs are <paramref name="paragraphs"/>: each marked one is built on its own.</summary>
        public void AddText(P.Shape shape, IEnumerable<TextParagraph> paragraphs)
        {
            this.Add(shape);
            foreach (var (index, paragraph) in paragraphs.Index())
            {
                if (paragraph.Build is { } mark)
                {
                    this.targets.Add(new BuildTarget(mark, mark.Item, IdOf(shape), TargetKind.Shape, index));
                }
            }
        }

        /// <summary>Adds a build target of its own, such as a scene shape's step.</summary>
        public void Target(BuildTarget target) => this.targets.Add(target);

        /// <summary>Writes a block with <paramref name="mark"/>, if any: the shapes it adds are built with it.</summary>
        public T Building<T>(BuildMark? mark, Func<T> write)
        {
            if (mark is null)
            {
                return write();
            }

            this.building.Add((mark, mark.Item));
            try
            {
                return write();
            }
            finally
            {
                this.building.RemoveAt(this.building.Count - 1);
            }
        }

        /// <summary>The shapes added from now on belong to <paramref name="item"/> of the block marked <paramref name="mark"/>.</summary>
        public void Item(BuildMark? mark, int item)
        {
            if (mark is not null && this.building.Count > 0 && ReferenceEquals(this.building[^1].Mark, mark))
            {
                this.building[^1] = (mark, item);
            }
        }

        private static uint IdOf(OpenXmlElement shape) => shape.Descendants<P.NonVisualDrawingProperties>().First().Id!.Value;

        public string? Hyperlink(string url)
        {
            if (!this.hyperlinks.TryGetValue(url, out var id))
            {
                id = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? (links ?? part).AddHyperlinkRelationship(uri, isExternal: true).Id : null;
                this.hyperlinks[url] = id;
            }

            return id;
        }
    }
}
