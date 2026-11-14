using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>The reader of the HTML deck (E07-S02): inheritance, colours, geometry, text layout, tables and notes.</summary>
public class DeckReaderTests
{
    private const string Namespaces = "xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'";

    private const double Pt = 12700;

    /// <summary>A one-slide deck with <paramref name="shapes"/> (p:sp and others, in the a/p namespaces) added to its shape tree.</summary>
    private static (DeckScene Deck, GenerationResult Result) Read(string shapes, Action<XDocument>? slide = null, Action<PresentationDocument>? parts = null)
    {
        var pptx = Samples.Generate(Samples.Markdown("## Title\n\nBody\n"));
        pptx = HtmlDeckTests.Edit(pptx, (slides, _) =>
        {
            var tree = slides[0].Descendants(Ns.P + "spTree").Single();
            tree.Add(XElement.Parse($"<x {Namespaces}>{shapes}</x>").Elements());
            slide?.Invoke(slides[0]);
        }, parts);
        var (deck, notes) = DeckReader.Read(new MemoryStream(pptx));
        return (deck, notes.Result(deck.Slides.Count));
    }

    private static string Shape(int id, string geometry, string xfrm = "<a:xfrm><a:off x='1000' y='2000'/><a:ext cx='3000' cy='4000'/></a:xfrm>", string fill = "<a:solidFill><a:srgbClr val='FF0000'/></a:solidFill>", string text = "") =>
        $"<p:sp><p:nvSpPr><p:cNvPr id='{id}' name='Shape {id}'/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>{xfrm}{geometry}{fill}</p:spPr>{text}</p:sp>";

    private static T Item<T>(DeckScene deck, int id)
        where T : SceneItem =>
        deck.Slides[0].Items.SelectMany(Flatten).OfType<T>().Single(i => i.Id == id);

    private static IEnumerable<SceneItem> Flatten(SceneItem item) => item is SceneGroup group ? [group, .. group.Items.SelectMany(Flatten)] : [item];

    private static SceneShape Placeholder(DeckScene deck, string text) =>
        deck.Slides[0].Items.SelectMany(Flatten).OfType<SceneShape>().Single(s => s.Text?.Paragraphs.Any(p => p.Lines.Any(l => string.Concat(l.Runs.Select(r => r.Text)).Contains(text, StringComparison.Ordinal))) == true);

    [Fact]
    public void Placeholders_TakeTheirBoxAndStyleFromLayoutAndMaster()
    {
        var (deck, result) = Read(string.Empty);
        var title = Placeholder(deck, "Title");
        var run = title.Text!.Paragraphs[0].Lines[0].Runs[0];

        // The built-in master's title: its box, 32 pt bold, in the master's colour; the layout has no box of its own.
        Assert.Equal((912283.0, 256032.0), (title.Transform.E, title.Transform.F));
        Assert.Equal(32 * Pt, run.Size);
        Assert.True(run.Font.Bold);
        Assert.Equal("1F2937", run.Color.Hex);
        Assert.Equal("Arial", run.Font.Family);
        Assert.Equal("Liberation Sans", run.Font.Face!.Family);
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.FontMissing);
    }

    [Fact]
    public void AShapesListStyle_BeatsTheMasters()
    {
        var (deck, _) = Read(string.Empty, slide =>
        {
            var title = slide.Descendants(Ns.P + "sp").First();
            title.Element(Ns.P + "txBody")!.Element(Ns.A + "lstStyle")!.Add(XElement.Parse($"<a:lvl1pPr {Namespaces}><a:defRPr sz='1000' b='0'/></a:lvl1pPr>"));
        });
        var run = Placeholder(deck, "Title").Text!.Paragraphs[0].Lines[0].Runs[0];

        Assert.Equal(10 * Pt, run.Size);
        Assert.False(run.Font.Bold);
    }

    [Fact]
    public void SchemeColours_GoThroughTheColourMap_AndTheirModifiers()
    {
        var text = "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr sz='1200'><a:solidFill><a:schemeClr val='tx1'/></a:solidFill></a:rPr><a:t>mapped</a:t></a:r></a:p></p:txBody>";
        var (deck, _) = Read(
            Shape(10, "<a:prstGeom prst='rect'/>", fill: "<a:solidFill><a:srgbClr val='808080'><a:lumMod val='50000'/><a:alpha val='40000'/></a:srgbClr></a:solidFill>", text: text),
            slide => slide.Root!.Element(Ns.P + "clrMapOvr")?.Remove());
        var shape = Item<SceneShape>(deck, 10);
        var fill = Assert.IsType<SolidFill>(shape.Paths[0].Fill);
        Assert.Equal(("404040", 0.4), (fill.Color.Hex, fill.Color.A));
        Assert.Equal("000000", shape.Text!.Paragraphs[0].Lines[0].Runs[0].Color.Hex);

        // A slide that maps tx1 to the light colour draws the same run white.
        var (light, _) = Read(Shape(10, "<a:prstGeom prst='rect'/>", text: text), slide =>
        {
            slide.Root!.Element(Ns.P + "clrMapOvr")?.Remove();
            slide.Root!.Element(Ns.P + "cSld")!.AddAfterSelf(XElement.Parse($"<p:clrMapOvr {Namespaces}><a:overrideClrMapping bg1='dk1' tx1='lt1' bg2='dk2' tx2='lt2' accent1='accent1' accent2='accent2' accent3='accent3' accent4='accent4' accent5='accent5' accent6='accent6' hlink='hlink' folHlink='folHlink'/></p:clrMapOvr>"));
        });
        Assert.Equal("FFFFFF", Item<SceneShape>(light, 10).Text!.Paragraphs[0].Lines[0].Runs[0].Color.Hex);
    }

    [Theory]
    [InlineData("*/ 10 6 4", 15)]
    [InlineData("+- 10 6 4", 12)]
    [InlineData("+/ 10 6 4", 4)]
    [InlineData("?: -1 6 4", 4)]
    [InlineData("?: 1 6 4", 6)]
    [InlineData("abs -7", 7)]
    [InlineData("at2 1 1", 2700000)]
    [InlineData("cat2 10 1 0", 10)]
    [InlineData("cos 10 5400000", 0)]
    [InlineData("max 3 4", 4)]
    [InlineData("min 3 4", 3)]
    [InlineData("mod 3 4 0", 5)]
    [InlineData("pin 0 120 100", 100)]
    [InlineData("pin 0 -5 100", 0)]
    [InlineData("sat2 10 0 1", 10)]
    [InlineData("sin 10 5400000", 10)]
    [InlineData("sqrt 16", 4)]
    [InlineData("tan 10 2700000", 10)]
    [InlineData("val w", 200)]
    [InlineData("*/ ss 1 2", 50)]
    public void GuideFormulas_ComputeAsPowerPointDoes(string formula, double expected)
    {
        var guides = new Dictionary<string, double> { ["w"] = 200, ["ss"] = 100 };
        Assert.Equal(expected, Geometry.Formula(formula, guides), 6);
    }

    [Fact]
    public void ARoundRectangle_TakesItsAdjustValue()
    {
        var adjust = XElement.Parse("<avLst><gd name='adj' fmla='val 50000'/></avLst>");
        var path = Geometry.Evaluate(PresetShapes.Definition("roundRect")!, 200, 100, adjust)!.Single();

        // A corner radius of half the shorter side: the left edge is only its middle point.
        Assert.Equal(new PathMove(0, 50), path.Segments[0]);
        Assert.Equal(new PathLine(150, 0), path.Segments[2]);
    }

    [Fact]
    public void CustomGeometry_WithGuidesAndArcs_IsEvaluated()
    {
        var geometry = XElement.Parse("<custGeom><avLst/><gdLst><gd name='half' fmla='*/ w 1 2'/></gdLst><pathLst>"
            + "<path w='100' h='100'><moveTo><pt x='0' y='0'/></moveTo><lnTo><pt x='half' y='0'/></lnTo><arcTo wR='10' hR='10' stAng='0' swAng='5400000'/><quadBezTo><pt x='0' y='100'/><pt x='0' y='50'/></quadBezTo><close/></path>"
            + "<path fill='none' stroke='0'><moveTo><pt x='l' y='b'/></moveTo><lnTo><pt x='r' y='t'/></lnTo></path></pathLst></custGeom>");
        var paths = Geometry.Evaluate(geometry, 200, 100)!;

        // The path's own 100 × 100 space is stretched to 200 × 100; the guide is in the shape's.
        Assert.Equal(new PathLine(200, 0), paths[0].Segments[1]);
        var arc = Assert.IsType<PathCubic>(paths[0].Segments[2]);
        Assert.Equal((180, 10), (Math.Round(arc.X), Math.Round(arc.Y)));
        Assert.False(paths[1].Filled);
        Assert.False(paths[1].Stroked);
    }

    [Fact]
    public void Groups_MapTheirChildSpace_AndShapesRotateAndFlip()
    {
        var group = "<p:grpSp><p:nvGrpSpPr><p:cNvPr id='20' name='Group'/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x='1000' y='1000'/><a:ext cx='2000' cy='2000'/><a:chOff x='500' y='500'/><a:chExt cx='1000' cy='1000'/></a:xfrm></p:grpSpPr>"
            + Shape(21, "<a:prstGeom prst='rect'/>", "<a:xfrm><a:off x='600' y='600'/><a:ext cx='100' cy='100'/></a:xfrm>")
            + "</p:grpSp>"
            + Shape(22, "<a:prstGeom prst='rect'/>", "<a:xfrm rot='5400000' flipH='1'><a:off x='0' y='0'/><a:ext cx='200' cy='100'/></a:xfrm>");
        var (deck, _) = Read(group);

        var child = Item<SceneShape>(deck, 21);
        Assert.Equal((1200.0, 1200.0), child.Transform.Apply(0, 0));
        Assert.Equal((1400.0, 1400.0), child.Transform.Apply(100, 100));

        // Rotated a quarter turn clockwise about its centre (100, 50), and mirrored first: (0, 0) → (200, 0) → (150, 150).
        var turned = Item<SceneShape>(deck, 22);
        var (x, y) = turned.Transform.Apply(0, 0);
        Assert.Equal((150.0, 150.0), (Math.Round(x, 6), Math.Round(y, 6)));
        var (tx, ty) = turned.TextTransform.Apply(0, 0);
        Assert.Equal((150.0, -50.0), (Math.Round(tx, 6), Math.Round(ty, 6)));
    }

    [Fact]
    public void Bullets_AndNumbering_CountPerLevel()
    {
        var text = "<p:txBody><a:bodyPr/><a:lstStyle/>"
            + "<a:p><a:pPr marL='342900' indent='-342900'><a:buAutoNum type='romanUcPeriod' startAt='3'/></a:pPr><a:r><a:rPr sz='1800'/><a:t>one</a:t></a:r></a:p>"
            + "<a:p><a:pPr marL='685800' lvl='1' indent='-342900'><a:buAutoNum type='alphaLcParenR'/></a:pPr><a:r><a:rPr sz='1800'/><a:t>nested</a:t></a:r></a:p>"
            + "<a:p><a:pPr marL='342900' indent='-342900'><a:buAutoNum type='romanUcPeriod' startAt='3'/></a:pPr><a:r><a:rPr sz='1800'/><a:t>two</a:t></a:r></a:p>"
            + "<a:p><a:pPr marL='342900' indent='-342900'><a:buFont typeface='Wingdings'/><a:buChar char=''/></a:pPr><a:r><a:rPr sz='1800'/><a:t>done</a:t></a:r></a:p>"
            + "</p:txBody>";
        var (deck, _) = Read(Shape(30, "<a:prstGeom prst='rect'/>", "<a:xfrm><a:off x='0' y='0'/><a:ext cx='6000000' cy='3000000'/></a:xfrm>", "<a:noFill/>", text));
        var bullets = Item<SceneShape>(deck, 30).Text!.Paragraphs.Select(p => p.Lines[0].Runs[0]).ToList();

        Assert.Equal(["III.", "a)", "IV.", "☑"], bullets.Select(b => b.Text));
        Assert.Equal(0, bullets[0].X - 91440);
        Assert.Equal(342900, bullets[1].X - 91440);
        Assert.True(Item<SceneShape>(deck, 30).Text!.Paragraphs[0].Lines[0].Runs[1].X >= 342900 + 91440);
    }

    [Fact]
    public void Autofit_ScalesTheFont_AndLinesBreakBetweenWords()
    {
        var words = "The quick brown fox jumps over the lazy dog and keeps running far away";
        var text = $"<p:txBody><a:bodyPr wrap='square' lIns='0' rIns='0'><a:normAutofit fontScale='50000' lnSpcReduction='10000'/></a:bodyPr><a:lstStyle/><a:p><a:r><a:rPr sz='4000'><a:latin typeface='Arial'/></a:rPr><a:t>{words}</a:t></a:r></a:p></p:txBody>";
        var width = 2 * 914400;
        var (deck, _) = Read(Shape(40, "<a:prstGeom prst='rect'/>", $"<a:xfrm><a:off x='0' y='0'/><a:ext cx='{width}' cy='3000000'/></a:xfrm>", "<a:noFill/>", text));
        var lines = Item<SceneShape>(deck, 40).Text!.Paragraphs[0].Lines;
        var face = lines[0].Runs[0].Font;

        Assert.Equal(20 * Pt, lines[0].Runs[0].Size);
        Assert.True(lines.Count > 1);
        Assert.Equal(words, string.Join(" ", lines.Select(l => string.Concat(l.Runs.Select(r => r.Text)).Trim())));
        for (var i = 0; i < lines.Count; i++)
        {
            var line = string.Concat(lines[i].Runs.Select(r => r.Text)).TrimEnd();
            Assert.True(FontLibrary.Width(face, line, 20 * Pt) <= width + 1, line);
            if (i + 1 < lines.Count)
            {
                // The next word would not have fitted.
                var next = string.Concat(lines[i + 1].Runs.Select(r => r.Text)).Split(' ')[0];
                Assert.True(FontLibrary.Width(face, line + " " + next, 20 * Pt) > width, line + " " + next);
            }
        }

        // 1.2 × size × 90 %.
        Assert.Equal(1.2 * 20 * Pt * 0.9, lines[1].Baseline - lines[0].Baseline, 3);
    }

    [Fact]
    public void AMissingFamily_IsEstimated_AndReported()
    {
        var text = "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr sz='1800'><a:latin typeface='Made Up Sans'/></a:rPr><a:t>Hello</a:t></a:r></a:p></p:txBody>";
        var (deck, result) = Read(Shape(50, "<a:prstGeom prst='rect'/>", text: text));

        Assert.Null(Item<SceneShape>(deck, 50).Text!.Paragraphs[0].Lines[0].Runs[0].Font.Face);
        var note = Assert.Single(result.Notes, n => n.Code == NoteCodes.FontMissing);
        Assert.Equal("The font 'Made Up Sans' is neither embedded nor passed and has no stand-in, so its text is measured by estimate (slide 1).", note.Message);
        Assert.Equal(0, note.Line);
    }

    [Fact]
    public void APassedFont_MeasuresItsFamily()
    {
        var serif = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample", "fonts", "LiberationSerif-Regular.ttf"));
        var pptx = Samples.Generate(Samples.Markdown("## T\n\nx\n"));
        pptx = HtmlDeckTests.Edit(pptx, (slides, _) => slides[0].Descendants(Ns.P + "spTree").Single().Add(XElement.Parse($"<x {Namespaces}>{Shape(51, "<a:prstGeom prst='rect'/>", text: "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr><a:latin typeface='Liberation Serif'/></a:rPr><a:t>Hi</a:t></a:r></a:p></p:txBody>")}</x>").Elements()));
        var (deck, notes) = DeckReader.Read(new MemoryStream(pptx), [serif]);

        var face = Item<SceneShape>(deck, 51).Text!.Paragraphs[0].Lines[0].Runs[0].Font.Face!;
        Assert.Equal((FaceSource.Passed, serif.Length), (face.Source, face.File.Length));
        Assert.Empty(notes.Kinds);
    }

    [Fact]
    public void Tables_MergeCells_GrowRows_AndTakeTheBuiltInStyle()
    {
        static string Cell(string text, string attributes = "") => $"<a:tc{attributes}><a:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr sz='1200'/><a:t>{text}</a:t></a:r></a:p></a:txBody><a:tcPr/></a:tc>";
        var table = "<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id='60' name='Table'/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x='0' y='0'/><a:ext cx='2000000' cy='600000'/></p:xfrm>"
            + "<a:graphic><a:graphicData uri='http://schemas.openxmlformats.org/drawingml/2006/table'><a:tbl><a:tblPr firstRow='1' bandRow='1'><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr>"
            + "<a:tblGrid><a:gridCol w='1000000'/><a:gridCol w='1000000'/></a:tblGrid>"
            + $"<a:tr h='100000'>{Cell("Wide header", " gridSpan='2'")}{Cell(string.Empty, " hMerge='1'")}</a:tr>"
            + $"<a:tr h='100000'>{Cell("a")}{Cell("a much longer text that has to wrap onto several lines")}</a:tr>"
            + "</a:tbl></a:graphicData></a:graphic></p:graphicFrame>";
        var (deck, result) = Read(table);
        var shapes = Item<SceneGroup>(deck, 60).Items.OfType<SceneShape>().Where(s => s.Text is not null).ToList();

        Assert.Equal(3, shapes.Count);
        var header = shapes[0];
        Assert.Equal(2000000, header.Paths[0].Segments.OfType<PathLine>().Max(l => l.X));
        var headerFill = Assert.IsType<SolidFill>(header.Paths[0].Fill).Color;
        var bandFill = Assert.IsType<SolidFill>(shapes[1].Paths[0].Fill).Color;
        Assert.NotEqual(headerFill, bandFill);
        Assert.True(bandFill.R > headerFill.R && bandFill.G > headerFill.G, "the band is a tint of the header's accent");
        Assert.True(header.Text!.Paragraphs[0].Lines[0].Runs[0].Font.Bold);
        Assert.Equal("FFFFFF", header.Text.Paragraphs[0].Lines[0].Runs[0].Color.Hex);

        // The second row grew to fit the wrapped text.
        Assert.True(shapes[2].Text!.Paragraphs[0].Lines.Count > 2);
        Assert.True(shapes[2].Transform.F > 0 && shapes[2].Paths[0].Segments.OfType<PathLine>().Max(l => l.Y) > 100000);
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
    }

    [Fact]
    public void MasterDecorations_UseTheThemesStyles_UnlessHidden_AndBackgroundsInherit()
    {
        var decoration = "<p:sp><p:nvSpPr><p:cNvPr id='80' name='Band'/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='500000' cy='100000'/></a:xfrm>"
            + "<a:custGeom><a:avLst/><a:gdLst><a:gd name='mid' fmla='*/ w 1 2'/></a:gdLst><a:rect l='l' t='t' r='r' b='b'/><a:pathLst><a:path><a:moveTo><a:pt x='l' y='t'/></a:moveTo><a:lnTo><a:pt x='mid' y='b'/></a:lnTo><a:lnTo><a:pt x='r' y='t'/></a:lnTo><a:close/></a:path></a:pathLst></a:custGeom></p:spPr>"
            + "<p:style><a:lnRef idx='0'><a:schemeClr val='accent2'/></a:lnRef><a:fillRef idx='1'><a:schemeClr val='accent2'/></a:fillRef><a:effectRef idx='0'><a:schemeClr val='accent2'/></a:effectRef><a:fontRef idx='minor'><a:schemeClr val='lt1'/></a:fontRef></p:style></p:sp>";
        var pptx = Samples.Generate(Samples.Markdown("# Cover\n\n## Content\n\nText\n", Samples.Image("photo.png")));
        pptx = HtmlDeckTests.Edit(pptx, (slides, master) =>
        {
            master.Descendants(Ns.P + "spTree").Single().Add(XElement.Parse($"<x {Namespaces}>{decoration}</x>").Elements());
            var bg = slides[1].Root!.Element(Ns.P + "cSld")!;
            bg.AddFirst(XElement.Parse($"<p:bg {Namespaces}><p:bgPr><a:blipFill><a:blip r:embed='rIdBg'/><a:stretch><a:fillRect/></a:stretch></a:blipFill><a:effectLst/></p:bgPr></p:bg>"));
        }, package =>
        {
            var image = Samples.Slides(package)[1].AddImagePart(ImagePartType.Png, "rIdBg");
            image.FeedData(new MemoryStream(Samples.Image("photo.png").Content.ToArray()));
        });
        var (deck, notes) = DeckReader.Read(new MemoryStream(pptx));

        // The cover's layout hides the master's shapes; the content slide shows the decoration in accent 2.
        Assert.DoesNotContain(deck.Slides[0].Items, i => i.Id == 80);
        var band = Assert.IsType<SceneShape>(deck.Slides[1].Items.First(i => i.Id == 80));
        Assert.Equal(new PathLine(250000, 100000), band.Paths[0].Segments[1]);
        Assert.IsType<SolidFill>(band.Paths[0].Fill);
        Assert.IsType<ImageFill>(deck.Slides[1].Background);
        Assert.IsType<SolidFill>(deck.Slides[0].Background);
        Assert.Empty(notes.Kinds);
    }

    [Fact]
    public void UnknownThings_AreLeftOut_AndReportedOncePerKind()
    {
        var shadow = "<a:effectLst><a:outerShdw blurRad='40000' dist='20000'><a:srgbClr val='000000'/></a:outerShdw></a:effectLst>";
        var shapes = Shape(70, "<a:prstGeom prst='cloud'/>", fill: "<a:solidFill><a:srgbClr val='00FF00'/></a:solidFill>" + shadow)
            + Shape(71, "<a:prstGeom prst='rect'/>", fill: "<a:solidFill><a:srgbClr val='00FF00'/></a:solidFill>" + shadow)
            + "<p:pic><p:nvPicPr><p:cNvPr id='72' name='Logo'/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed='rIdEmf'/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='100' cy='100'/></a:xfrm><a:prstGeom prst='rect'/></p:spPr></p:pic>";
        var (deck, result) = Read(shapes, parts: package =>
        {
            var slide = Samples.Slides(package)[0];
            var image = slide.AddImagePart("image/x-emf", "rIdEmf");
            image.FeedData(new MemoryStream([1, 0, 0, 0]));
        });

        Assert.Single(Item<SceneShape>(deck, 70).Paths);
        Assert.DoesNotContain(deck.Slides[0].Items.SelectMany(Flatten), i => i.Id == 72);
        Assert.Equal(
            [
                "Not drawn: effects such as shadows, glow, reflection and soft edges (slide 1).",
                "Not drawn: the preset shape cloud (drawn as a rectangle) (slide 1).",
                "Not drawn: pictures in EMF (image/x-emf) (slide 1).",
            ],
            result.Notes.Where(n => n.Code == NoteCodes.DrawingUnsupported).Select(n => n.Message));
    }
}
