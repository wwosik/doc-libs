using System.Globalization;
using System.IO.Packaging;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>A part's XML with the part itself, for its relationships.</summary>
internal sealed class PartXml
{
    public PartXml(OpenXmlPart part)
    {
        this.Part = part;
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        this.Root = XDocument.Load(reader).Root ?? throw new InvalidDataException($"{part.Uri} is empty.");
    }

    public OpenXmlPart Part { get; }

    public XElement Root { get; }

    public OpenXmlPart? Child(string? id)
    {
        if (id is null)
        {
            return null;
        }

        try
        {
            return this.Part.GetPartById(id);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>An external hyperlink target, if it is <c>http</c>, <c>https</c> or <c>mailto</c>.</summary>
    public string? Link(string id)
    {
        var target = this.Part.HyperlinkRelationships.FirstOrDefault(r => r.Id == id)?.Uri
            ?? this.Part.ExternalRelationships.FirstOrDefault(r => r.Id == id)?.Uri;
        return target is { IsAbsoluteUri: true } uri && uri.Scheme is "http" or "https" or "mailto" ? uri.OriginalString : null;
    }
}

/// <summary>
/// Reads a deck's DrawingML into a <see cref="DeckScene"/>: slides in <c>sldIdLst</c> order (hidden ones skipped),
/// with layout and master decorations, placeholders resolved through layout and master, theme colours, fonts and
/// styles, geometry, text laid out in real font metrics, tables, charts and pictures. What it cannot draw is left out
/// and reported in <see cref="RenderNotes"/>.
/// </summary>
internal sealed partial class DeckReader
{
    private const long DefaultWidth = 12192000;
    private const long DefaultHeight = 6858000;

    private readonly Dictionary<Uri, SceneImage?> images = [];
    private readonly Dictionary<Uri, PartXml> parts = [];
    private readonly RenderNotes notes = new();
    private FontLibrary fonts = null!;
    private XElement? defaultTextStyle;
    private XElement? tableStyles;

    private DeckReader()
    {
    }

    /// <summary>
    /// Reads <paramref name="pptx"/>; <paramref name="slides"/> limits which slides (by their number in the deck) are read.
    /// Throws <see cref="DocumentConversionException"/> for a package that is not a presentation.
    /// </summary>
    public static (DeckScene Scene, RenderNotes Notes) Read(Stream pptx, IEnumerable<ReadOnlyMemory<byte>>? fonts = null, ISet<int>? slides = null)
    {
        var buffer = new MemoryStream();
        pptx.CopyTo(buffer);
        buffer.Position = 0;
        PresentationDocument document;
        try
        {
            document = PresentationDocument.Open(buffer, false);
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or FileFormatException or IOException or XmlException)
        {
            throw new DocumentConversionException(Format.Pptx, "The file is not a PowerPoint presentation: " + e.Message, e);
        }

        using (document)
        {
            try
            {
                var reader = new DeckReader();
                var scene = reader.Deck(document, fonts ?? [], slides);
                return (scene, reader.notes);
            }
            catch (Exception e) when (e is InvalidDataException or XmlException or OpenXmlPackageException)
            {
                throw new DocumentConversionException(Format.Pptx, "The presentation is damaged: " + e.Message, e);
            }
        }
    }

    private PartXml Xml(OpenXmlPart part)
    {
        if (!this.parts.TryGetValue(part.Uri, out var xml))
        {
            this.parts[part.Uri] = xml = new PartXml(part);
        }

        return xml;
    }

    private DeckScene Deck(PresentationDocument document, IEnumerable<ReadOnlyMemory<byte>> passed, ISet<int>? only)
    {
        var presentationPart = document.PresentationPart
            ?? throw new DocumentConversionException(Format.Pptx, "The package has no presentation part, so it is not a PowerPoint presentation.");
        var presentation = this.Xml(presentationPart).Root;
        if (presentation.Name != Ns.P + "presentation")
        {
            throw new DocumentConversionException(Format.Pptx, $"The package's main part is a {presentation.Name.LocalName}, not a presentation.");
        }

        var size = presentation.Element(Ns.P + "sldSz");
        var width = size is null ? DefaultWidth : (long)Palette.Number(size, "cx");
        var height = size is null ? DefaultHeight : (long)Palette.Number(size, "cy");
        this.defaultTextStyle = presentation.Element(Ns.P + "defaultTextStyle");
        this.tableStyles = presentationPart.TableStylesPart is { } styles ? this.Xml(styles).Root : null;
        this.fonts = new FontLibrary(this.EmbeddedFonts(presentationPart, presentation), passed, this.notes);

        var slides = new List<SlideScene>();
        var number = 0;
        string? language = null;

        // From the slide named Backup on (DeckWriter.BackupSlideName), the deck's backup.
        var backup = false;
        foreach (var id in presentation.Element(Ns.P + "sldIdLst")?.Elements(Ns.P + "sldId") ?? [])
        {
            number++;
            if (presentationPart.TryGetPartById(id.Attribute(Ns.R + "id")?.Value ?? string.Empty, out var part) is false || part is not SlidePart slidePart)
            {
                continue;
            }

            var slide = this.Xml(slidePart);
            backup |= slide.Root.Element(Ns.P + "cSld")?.Attribute("name")?.Value == DeckWriter.BackupSlideName;
            language ??= slide.Root.Descendants(Ns.A + "rPr").Select(r => r.Attribute("lang")?.Value).FirstOrDefault(l => !string.IsNullOrEmpty(l));
            if (slide.Root.Attribute("show")?.Value is "0" or "false" || (only is not null && !only.Contains(number)))
            {
                continue;
            }

            this.notes.Slide = number;
            slides.Add(this.Slide(slidePart, slide, number) with { Backup = backup });
        }

        this.notes.Slide = 0;
        var title = document.PackageProperties.Title;
        return new DeckScene(width, height, slides, string.IsNullOrWhiteSpace(title) ? null : title, language, [.. this.fonts.Used]);
    }

    private IEnumerable<(string Family, byte[] File, bool? Bold, bool? Italic)> EmbeddedFonts(PresentationPart presentationPart, XElement presentation)
    {
        foreach (var font in presentation.Element(Ns.P + "embeddedFontLst")?.Elements(Ns.P + "embeddedFont") ?? [])
        {
            var family = font.Element(Ns.P + "font")?.Attribute("typeface")?.Value;
            if (family is null)
            {
                continue;
            }

            foreach (var (face, bold, italic) in new[] { ("regular", false, false), ("bold", true, false), ("italic", false, true), ("boldItalic", true, true) })
            {
                if (font.Element(Ns.P + face)?.Attribute(Ns.R + "id")?.Value is not { } id
                    || !presentationPart.TryGetPartById(id, out var part))
                {
                    continue;
                }

                using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
                using var data = new MemoryStream();
                stream.CopyTo(data);
                if (FontLibrary.FromEmbeddedOpenType(data.ToArray()) is { } ttf)
                {
                    yield return (family, ttf, bold, italic);
                }
                else
                {
                    this.notes.FontMissing($"The embedded font '{family}' is compressed or damaged, so its {face} face is not used");
                }
            }
        }
    }

    /// <summary>The parts and resolution context of the slide being read.</summary>
    private sealed record SlideParts(PartXml Slide, PartXml? Layout, PartXml? Master, Palette Palette, TextLayout Text, int Number);

    private SlideScene Slide(SlidePart slidePart, PartXml slide, int number)
    {
        var layout = slidePart.SlideLayoutPart is { } layoutPart ? this.Xml(layoutPart) : null;
        var master = slidePart.SlideLayoutPart?.SlideMasterPart is { } masterPart ? this.Xml(masterPart) : null;
        var themePart = slidePart.SlideLayoutPart?.SlideMasterPart?.ThemePart;
        var theme = DeckTheme.Read(themePart is null ? null : this.Xml(themePart).Root);

        var map = Palette.Map(master?.Root.Element(Ns.P + "clrMap"), null);
        map = Palette.Map(layout?.Root.Element(Ns.P + "clrMapOvr")?.Element(Ns.A + "overrideClrMapping"), map);
        map = Palette.Map(slide.Root.Element(Ns.P + "clrMapOvr")?.Element(Ns.A + "overrideClrMapping"), map);
        var palette = new Palette(theme, map, this.notes);
        var context = new SlideParts(slide, layout, master, palette, new TextLayout(palette, this.fonts, slide.Link, number), number);

        // Background: the slide's, else the layout's, else the master's.
        SceneFill? background = null;
        foreach (var part in new[] { slide, layout, master })
        {
            if (part?.Root.Element(Ns.P + "cSld")?.Element(Ns.P + "bg") is { } bg)
            {
                background = this.Background(bg, part, palette);
                break;
            }
        }

        var items = new List<SceneItem>();
        var slideShows = slide.Root.Attribute("showMasterSp")?.Value is not ("0" or "false");
        var layoutShows = layout?.Root.Attribute("showMasterSp")?.Value is not ("0" or "false");
        if (master is not null && slideShows && layoutShows)
        {
            items.AddRange(this.Tree(Tree(master), master, context, Matrix.Identity, null, decorations: true));
        }

        if (layout is not null && slideShows)
        {
            items.AddRange(this.Tree(Tree(layout), layout, context, Matrix.Identity, null, decorations: true));
        }

        var decorations = items.Count;
        var own = this.Tree(Tree(slide), slide, context, Matrix.Identity, null, decorations: false);
        items.AddRange(own);
        return new SlideScene(number, Title(slide.Root) ?? $"Slide {number}", background, items)
        {
            Decorations = decorations,
            Notes = this.SpeakerNotes(slidePart),
            Steps = this.Steps(slide.Root, own),
            Transition = this.Transition(slide.Root),
            Line = SourceLines.Read(slide.Root),
        };
    }

    private static XElement? Tree(PartXml part) => part.Root.Element(Ns.P + "cSld")?.Element(Ns.P + "spTree");

    private static string? Title(XElement slide)
    {
        var title = slide.Descendants(Ns.P + "sp").FirstOrDefault(sp => Placeholder(sp)?.Attribute("type")?.Value is "title" or "ctrTitle");
        var text = title is null ? null : string.Join(" ", title.Descendants(Ns.A + "p").Select(p => string.Concat(p.Descendants(Ns.A + "t").Select(t => t.Value)).Trim()).Where(t => t.Length > 0));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private SceneFill? Background(XElement bg, PartXml part, Palette palette)
    {
        if (bg.Element(Ns.P + "bgPr") is { } properties)
        {
            if (properties.Element(Ns.A + "effectLst")?.HasElements == true)
            {
                this.notes.Unsupported("background effects");
            }

            return palette.Fill(properties, null, fill => this.BlipImage(fill, part));
        }

        if (bg.Element(Ns.P + "bgRef") is { } reference)
        {
            var index = (int)Palette.Number(reference, "idx");
            var style = index >= 1001 ? palette.Theme.Style("bgFillStyleLst", index - 1000) : palette.Theme.Style("fillStyleLst", index);
            return style is null ? null : palette.Fill(new XElement("x", style), palette.Color(reference), fill => this.BlipImage(fill, part));
        }

        return null;
    }

    private SceneImage? BlipImage(XElement fill, PartXml part)
    {
        var blip = fill.Element(Ns.A + "blip");
        return blip is null ? null : this.Image(part, blip.Attribute(Ns.R + "embed")?.Value);
    }

    /// <summary>The image of a relationship; <see langword="null"/> and reported for a format a browser cannot show.</summary>
    private SceneImage? Image(PartXml part, string? id)
    {
        if (part.Child(id) is not { } image)
        {
            return null;
        }

        if (!this.images.TryGetValue(image.Uri, out var scene))
        {
            var type = image.ContentType;
            if (type is "image/png" or "image/jpeg" or "image/gif" or "image/svg+xml")
            {
                using var stream = image.GetStream(FileMode.Open, FileAccess.Read);
                using var data = new MemoryStream();
                stream.CopyTo(data);
                scene = new SceneImage(data.ToArray(), type);
            }
            else
            {
                this.notes.Unsupported($"pictures in {Path.GetExtension(image.Uri.OriginalString).TrimStart('.').ToUpperInvariant()} ({type})");
            }

            this.images[image.Uri] = scene;
        }
        else if (scene is null)
        {
            this.notes.Unsupported($"pictures in {Path.GetExtension(image.Uri.OriginalString).TrimStart('.').ToUpperInvariant()} ({image.ContentType})");
        }

        return scene;
    }

    // The items of a shape tree in z-order. On masters and layouts placeholders are prompts and are left out.
    private List<SceneItem> Tree(XElement? tree, PartXml part, SlideParts context, Matrix parent, SceneFill? groupFill, bool decorations)
    {
        var items = new List<SceneItem>();
        foreach (var element in tree?.Elements() ?? [])
        {
            if (decorations && Placeholder(element) is not null)
            {
                continue;
            }

            if (Hidden(element))
            {
                continue;
            }

            switch (element.Name.LocalName)
            {
                case "nvGrpSpPr" or "grpSpPr" or "extLst":
                    break;
                case "sp" or "cxnSp":
                    if (this.Shape(element, part, context, parent, groupFill) is { } shape)
                    {
                        items.Add(shape);
                    }

                    break;
                case "grpSp":
                    items.Add(this.Group(element, part, context, parent, groupFill, decorations));
                    break;
                case "pic":
                    if (this.Picture(element, part, context, parent) is { } picture)
                    {
                        items.Add(picture);
                    }

                    break;
                case "graphicFrame":
                    if (this.Frame(element, part, context, parent) is { } frame)
                    {
                        items.Add(frame);
                    }

                    break;
                case "AlternateContent":
                    var chosen = element.Element(Ns.Mc + "Fallback") ?? element.Elements(Ns.Mc + "Choice").FirstOrDefault();
                    items.AddRange(this.Tree(chosen, part, context, parent, groupFill, decorations));
                    break;
                case "contentPart":
                    this.notes.Unsupported("ink");
                    break;
                default:
                    this.notes.Unsupported($"the element {element.Name.LocalName}");
                    break;
            }
        }

        return items;
    }

    private static bool Hidden(XElement element) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("nv", StringComparison.Ordinal))
            ?.Element(Ns.P + "cNvPr")?.Attribute("hidden")?.Value is "1" or "true";

    private static XElement? NonVisual(XElement element) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("nv", StringComparison.Ordinal));

    /// <summary>The <c>p:ph</c> of a shape, picture or frame, if it is a placeholder.</summary>
    private static XElement? Placeholder(XElement element) => NonVisual(element)?.Element(Ns.P + "nvPr")?.Element(Ns.P + "ph");

    private static (int Id, string Name) Identity(XElement element)
    {
        var properties = NonVisual(element)?.Element(Ns.P + "cNvPr");
        return ((int)(properties is null ? 0 : Palette.Number(properties, "id")), properties?.Attribute("name")?.Value ?? string.Empty);
    }

    /// <summary>The layout's and the master's placeholders a slide placeholder inherits from, nearest first.</summary>
    private static List<XElement> Inherited(XElement? ph, SlideParts context, XElement element)
    {
        var chain = new List<XElement>();
        if (ph is null)
        {
            return chain;
        }

        // A layout placeholder inherits from the master only; a master placeholder from nothing.
        var own = element.Document?.Root;
        if (context.Layout is { } layout && !ReferenceEquals(own, layout.Root) && !ReferenceEquals(own, context.Master?.Root) && Match(layout, ph, master: false) is { } fromLayout)
        {
            chain.Add(fromLayout);
        }

        if (context.Master is { } master && !ReferenceEquals(own, master.Root) && Match(master, ph, master: true) is { } fromMaster)
        {
            chain.Add(fromMaster);
        }

        return chain;
    }

    // By idx, else by type (title ↔ ctrTitle; on the master every content type is its body).
    private static XElement? Match(PartXml part, XElement ph, bool master)
    {
        var candidates = Tree(part)?.Descendants().Where(e => e.Name.LocalName is "sp" or "pic" or "graphicFrame" && Placeholder(e) is not null).ToList() ?? [];
        var type = ph.Attribute("type")?.Value ?? "obj";
        string Normal(string t) => t switch
        {
            "ctrTitle" => "title",
            "subTitle" or "obj" or "pic" or "tbl" or "chart" or "dgm" or "media" or "clipArt" when master => "body",
            _ => t,
        };
        static bool Content(string t) => t is "obj" or "body" or "subTitle" or "pic" or "tbl" or "chart" or "dgm" or "media" or "clipArt";
        string TypeOf(XElement candidate) => Placeholder(candidate)!.Attribute("type")?.Value ?? "obj";

        // By idx among placeholders of a compatible type (a layout may give a picture and the date the same idx).
        if (!master && ph.Attribute("idx")?.Value is { } idx && type is not ("title" or "ctrTitle"))
        {
            var byIndex = candidates.Where(c => Placeholder(c)!.Attribute("idx")?.Value == idx).ToList();
            if (byIndex.FirstOrDefault(c => Normal(TypeOf(c)) == Normal(type) || (Content(TypeOf(c)) && Content(type))) is { } compatible)
            {
                return compatible;
            }
        }

        return candidates.FirstOrDefault(c => Normal(TypeOf(c)) == Normal(type));
    }

    private static XElement? Properties(XElement element) => element.Element(Ns.P + "spPr");

    /// <summary>The position of a shape: its own <c>xfrm</c>, else its placeholder's.</summary>
    private static XElement? Transform(XElement? own, IEnumerable<XElement> inherited, bool frame = false)
    {
        static bool Placed(XElement? xfrm) => xfrm?.Element(Ns.A + "off") is not null && xfrm.Element(Ns.A + "ext") is not null;
        if (Placed(own))
        {
            return own;
        }

        foreach (var shape in inherited)
        {
            var xfrm = frame ? shape.Element(Ns.P + "xfrm") ?? Properties(shape)?.Element(Ns.A + "xfrm") : Properties(shape)?.Element(Ns.A + "xfrm");
            if (Placed(xfrm))
            {
                return xfrm;
            }
        }

        return null;
    }

    /// <summary>Box and matrices of an <c>xfrm</c>: the geometry transform (flips included) and the text transform (rotation only).</summary>
    private static (double Width, double Height, Matrix Geometry, Matrix Text) Place(XElement xfrm, Matrix parent)
    {
        var off = xfrm.Element(Ns.A + "off")!;
        var ext = xfrm.Element(Ns.A + "ext")!;
        var (x, y, w, h) = (Palette.Number(off, "x"), Palette.Number(off, "y"), Palette.Number(ext, "cx"), Palette.Number(ext, "cy"));
        var rotation = Palette.Number(xfrm, "rot") / 60000;
        var flipH = xfrm.Attribute("flipH")?.Value is "1" or "true";
        var flipV = xfrm.Attribute("flipV")?.Value is "1" or "true";
        var placed = parent * Matrix.Translate(x, y) * Matrix.Rotate(rotation, w / 2, h / 2);
        var flips = (flipH ? Matrix.Translate(w, 0) * Matrix.Scale(-1, 1) : Matrix.Identity) * (flipV ? Matrix.Translate(0, h) * Matrix.Scale(1, -1) : Matrix.Identity);
        var text = flipV ? placed * Matrix.Rotate(180, w / 2, h / 2) : placed;
        return (w, h, placed * flips, text);
    }

    private SceneItem Group(XElement group, PartXml part, SlideParts context, Matrix parent, SceneFill? groupFill, bool decorations)
    {
        var (id, name) = Identity(group);
        var properties = group.Element(Ns.P + "grpSpPr");
        var xfrm = properties?.Element(Ns.A + "xfrm");
        var matrix = parent;
        if (xfrm?.Element(Ns.A + "off") is not null && xfrm.Element(Ns.A + "ext") is { } ext)
        {
            var (w, h, placed, _) = Place(xfrm, parent);
            var chOff = xfrm.Element(Ns.A + "chOff");
            var chExt = xfrm.Element(Ns.A + "chExt");
            var (cw, ch) = chExt is null ? (w, h) : (Palette.Number(chExt, "cx"), Palette.Number(chExt, "cy"));
            var (cx, cy) = chOff is null ? (0.0, 0.0) : (Palette.Number(chOff, "x"), Palette.Number(chOff, "y"));
            matrix = placed * Matrix.Scale(cw > 0 ? w / cw : 1, ch > 0 ? h / ch : 1) * Matrix.Translate(-cx, -cy);
            _ = ext;
        }

        var fill = context.Palette.Fill(properties, null, f => this.BlipImage(f, part));
        this.Effects(properties, null, context.Palette);
        return new SceneGroup(id, name, this.Tree(group, part, context, matrix, ReferenceEquals(fill, Palette.GroupFill) ? groupFill : fill ?? groupFill, decorations));
    }

    private void Effects(XElement? properties, XElement? style, Palette palette)
    {
        if (properties?.Element(Ns.A + "effectLst")?.HasElements == true || properties?.Element(Ns.A + "effectDag") is not null)
        {
            this.notes.Unsupported("effects such as shadows, glow, reflection and soft edges");
        }
        else if (properties?.Element(Ns.A + "effectLst") is null
            && style?.Element(Ns.A + "effectRef") is { } reference
            && palette.Theme.Style("effectStyleLst", (int)Palette.Number(reference, "idx")) is { } effect
            && (effect.Element(Ns.A + "effectLst")?.HasElements == true || effect.Element(Ns.A + "effectDag") is not null))
        {
            this.notes.Unsupported("effects such as shadows, glow, reflection and soft edges");
        }

        if (properties?.Element(Ns.A + "scene3d") is not null || properties?.Element(Ns.A + "sp3d") is not null)
        {
            this.notes.Unsupported("3-D effects");
        }
    }

    /// <summary>The paths of a shape's geometry with its fill and line; an unknown preset as its bounding rectangle, reported.</summary>
    private List<ScenePath> Paths(XElement? geometry, double width, double height, SceneFill? fill, SceneStroke? stroke)
    {
        IReadOnlyList<GeometryPath>? paths = null;
        if (geometry?.Name.LocalName == "custGeom")
        {
            paths = Geometry.Evaluate(geometry, width, height);
            if (paths is null)
            {
                this.notes.Unsupported("custom geometry with damaged guides (drawn as a rectangle)");
            }
        }
        else if (geometry?.Name.LocalName == "prstGeom")
        {
            var preset = geometry.Attribute("prst")?.Value ?? "rect";
            if (PresetShapes.Definition(preset) is { } definition)
            {
                paths = Geometry.Evaluate(definition, width, height, geometry.Element(Ns.A + "avLst"));
            }
            else
            {
                this.notes.Unsupported($"the preset shape {preset} (drawn as a rectangle)");
            }
        }

        paths ??= Geometry.Evaluate(PresetShapes.Definition("rect")!, width, height)!;
        var visibleFill = fill is SolidFill { Color.A: 0 } ? null : fill;
        return [.. paths.Select(p => new ScenePath(p.Segments, p.Filled ? visibleFill : null, p.Stroked ? stroke : null))
            .Where(p => p.Fill is not null || p.Stroke is not null)];
    }

    private SceneItem? Shape(XElement shape, PartXml part, SlideParts context, Matrix parent, SceneFill? groupFill)
    {
        var (id, name) = Identity(shape);
        var ph = Placeholder(shape);
        var inherited = Inherited(ph, context, shape);
        var own = Properties(shape);
        var xfrm = Transform(own?.Element(Ns.A + "xfrm"), inherited);
        if (xfrm is null)
        {
            return null;
        }

        var (width, height, geometryMatrix, textMatrix) = Place(xfrm, parent);
        var palette = context.Palette;
        var chain = new List<XElement?> { own };
        chain.AddRange(inherited.Select(Properties));
        var style = shape.Element(Ns.P + "style") ?? inherited.Select(i => i.Element(Ns.P + "style")).FirstOrDefault(s => s is not null);

        // Fill: the shape's own, its style's, then its placeholder's.
        var fill = palette.Fill(own, null, f => this.BlipImage(f, part)) ?? this.StyleFill(style, palette, part);
        foreach (var properties in chain.Skip(1))
        {
            fill ??= palette.Fill(properties, null, f => this.BlipImage(f, part));
        }

        if (ReferenceEquals(fill, Palette.GroupFill))
        {
            fill = groupFill;
        }

        var line = chain.Select(p => p?.Element(Ns.A + "ln")).FirstOrDefault(l => l is not null);
        var lineReference = style?.Element(Ns.A + "lnRef") is { } lnRef && (int)Palette.Number(lnRef, "idx") is > 0 and var lineIndex
            ? palette.Theme.Style("lnStyleLst", lineIndex) : null;
        var stroke = palette.Stroke(line, lineReference, style?.Element(Ns.A + "lnRef") is { } r ? palette.Color(r) : null);
        this.Effects(own, style, palette);

        var geometry = chain.Select(p => p?.Elements().FirstOrDefault(e => e.Name.LocalName is "prstGeom" or "custGeom")).FirstOrDefault(g => g is not null);
        var paths = this.Paths(geometry, width, height, fill, stroke);

        // Text through the shape's own list style, the style's font colour, the placeholders' and the master's styles.
        SceneText? text = null;
        if (shape.Element(Ns.P + "txBody") is { } body)
        {
            var lists = new List<XElement?> { body.Element(Ns.A + "lstStyle") };
            if (style?.Element(Ns.A + "fontRef") is { } fontRef && fontRef.Elements().Any())
            {
                lists.Add(new XElement(Ns.A + "lstStyle", Enumerable.Range(1, 9).Select(level =>
                    new XElement(Ns.A + $"lvl{level}pPr", new XElement(Ns.A + "defRPr", new XElement(Ns.A + "solidFill", fontRef.Elements().First()))))));
            }

            lists.AddRange(inherited.Select(i => i.Element(Ns.P + "txBody")?.Element(Ns.A + "lstStyle")));
            lists.Add(this.MasterStyle(ph, context));
            lists.Add(this.defaultTextStyle);
            var bodies = inherited.Select(i => i.Element(Ns.P + "txBody")?.Element(Ns.A + "bodyPr")).ToList();
            text = context.Text.Layout(body, new TextSources(lists, bodies), width, height)?.Text;
        }

        if (paths.Count == 0 && text is null)
        {
            return null;
        }

        var link = NonVisual(shape)?.Element(Ns.P + "cNvPr")?.Element(Ns.A + "hlinkClick")?.Attribute(Ns.R + "id")?.Value is { } linkId ? part.Link(linkId) : null;
        return new SceneShape(id, name, geometryMatrix, paths, text, textMatrix, link);
    }

    private SceneFill? StyleFill(XElement? style, Palette palette, PartXml part)
    {
        if (style?.Element(Ns.A + "fillRef") is not { } reference)
        {
            return null;
        }

        var index = (int)Palette.Number(reference, "idx");
        var fill = index >= 1001 ? palette.Theme.Style("bgFillStyleLst", index - 1000) : palette.Theme.Style("fillStyleLst", index);
        return fill is null ? Palette.NoFill : palette.Fill(new XElement("x", fill), palette.Color(reference), f => this.BlipImage(f, part));
    }

    /// <summary>The master's title, body or other style for a placeholder; the presentation default for other shapes.</summary>
    private XElement? MasterStyle(XElement? ph, SlideParts context)
    {
        if (ph is null)
        {
            return null;
        }

        var styles = context.Master?.Root.Element(Ns.P + "txStyles");
        return (ph.Attribute("type")?.Value ?? "obj") switch
        {
            "title" or "ctrTitle" => styles?.Element(Ns.P + "titleStyle"),
            "dt" or "ftr" or "sldNum" or "hdr" => styles?.Element(Ns.P + "otherStyle"),
            _ => styles?.Element(Ns.P + "bodyStyle"),
        };
    }

    private SceneItem? Picture(XElement picture, PartXml part, SlideParts context, Matrix parent)
    {
        var (id, name) = Identity(picture);
        var ph = Placeholder(picture);
        var inherited = Inherited(ph, context, picture);
        var own = Properties(picture);
        var xfrm = Transform(own?.Element(Ns.A + "xfrm"), inherited);
        if (xfrm is null)
        {
            return null;
        }

        if (NonVisual(picture)?.Element(Ns.P + "nvPr")?.Elements().Any(e => e.Name.LocalName is "videoFile" or "audioFile" or "quickTimeFile" or "wavAudioFile") == true)
        {
            this.notes.Unsupported("video and audio (their poster picture is drawn)");
        }

        var fill = picture.Element(Ns.P + "blipFill");
        var blip = fill?.Element(Ns.A + "blip");
        var image = this.Image(part, blip?.Attribute(Ns.R + "embed")?.Value);
        if (image is null)
        {
            return null;
        }

        var svgId = blip?.Descendants(Ns.Svg + "svgBlip").FirstOrDefault()?.Attribute(Ns.R + "embed")?.Value;
        var svg = svgId is null ? null : this.Image(part, svgId);
        if (fill?.Element(Ns.A + "tile") is not null)
        {
            this.notes.Unsupported("tiled pictures (drawn stretched)");
        }

        if (blip?.Elements().Any(e => e.Name.LocalName is not ("grayscl" or "extLst" or "alphaModFix")) == true)
        {
            this.notes.Unsupported("picture effects such as recolouring and brightness");
        }

        var crop = fill?.Element(Ns.A + "srcRect");
        double Share(string attribute) => crop is null ? 0 : Palette.Number(crop, attribute) / 100000;
        var (width, height, matrix, _) = Place(xfrm, parent);
        var chain = new List<XElement?> { own };
        chain.AddRange(inherited.Select(Properties));
        var geometry = chain.Select(p => p?.Elements().FirstOrDefault(e => e.Name.LocalName is "prstGeom" or "custGeom")).FirstOrDefault(g => g is not null);
        IReadOnlyList<PathSegment>? clip = null;
        if (geometry is not null && !(geometry.Name.LocalName == "prstGeom" && geometry.Attribute("prst")?.Value is null or "rect"))
        {
            clip = this.Paths(geometry, width, height, new SolidFill(Rgba.Black), null).SelectMany(p => p.Segments).ToList();
        }

        this.Effects(own, picture.Element(Ns.P + "style"), context.Palette);
        var link = NonVisual(picture)?.Element(Ns.P + "cNvPr")?.Element(Ns.A + "hlinkClick")?.Attribute(Ns.R + "id")?.Value is { } linkId ? part.Link(linkId) : null;
        var scene = new ScenePicture(id, name, matrix, width, height, image, svg, (Share("l"), Share("t"), Share("r"), Share("b")), clip, blip?.Element(Ns.A + "grayscl") is not null, link);

        // A picture's outline is drawn over it.
        var line = chain.Select(p => p?.Element(Ns.A + "ln")).FirstOrDefault(l => l is not null);
        if (context.Palette.Stroke(line, null, null) is { } stroke)
        {
            var outline = new SceneShape(id, name, matrix, this.Paths(geometry, width, height, null, stroke), null, matrix, null);
            return new SceneGroup(id, name, [scene, outline]);
        }

        return scene;
    }

    private SceneItem? Frame(XElement frame, PartXml part, SlideParts context, Matrix parent)
    {
        var (id, name) = Identity(frame);
        var inherited = Inherited(Placeholder(frame), context, frame);
        var xfrm = Transform(frame.Element(Ns.P + "xfrm"), inherited, frame: true);
        var data = frame.Element(Ns.A + "graphic")?.Element(Ns.A + "graphicData");
        if (xfrm is null || data is null)
        {
            return null;
        }

        var (width, height, matrix, _) = Place(xfrm, parent);
        switch (data.Attribute("uri")?.Value)
        {
            case "http://schemas.openxmlformats.org/drawingml/2006/table":
                return data.Element(Ns.A + "tbl") is { } table ? new SceneGroup(id, name, this.Table(table, context, matrix, width)) : null;
            case ChartWriter.GraphicUri:
                var chartId = data.Element(Ns.C + "chart")?.Attribute(Ns.R + "id")?.Value;
                return part.Child(chartId) is { } chart ? new SceneGroup(id, name, this.Chart(this.Xml(chart).Root, context, matrix, width, height)) : null;
            default:
                // OLE objects and others: their fallback picture, if the package has one.
                if (data.Descendants(Ns.P + "pic").FirstOrDefault() is { } fallback)
                {
                    return this.Picture(fallback, part, context, parent);
                }

                this.notes.Unsupported(data.Attribute("uri")?.Value?.EndsWith("/diagram", StringComparison.Ordinal) == true
                    ? "SmartArt"
                    : $"graphic frames of the kind {data.Attribute("uri")?.Value}");
                return null;
        }
    }

    internal static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
