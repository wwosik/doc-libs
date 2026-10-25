using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// <c>brand-dark</c>: a made-up brand whose own template has dark slides, like many corporate templates — a dark
/// gradient behind every slide, white title and body text, a footer placeholder, and two extra layouts (a title-only
/// layout for two-line titles and a closing layout). Built from the built-in master, so it needs no binary sample.
/// </summary>
internal static class DarkTemplate
{
    public const string TwoLineLayout = "Title Only Two Lines";
    public const string ClosingLayout = "Closing";

    public static readonly byte[] Content = Build();

    /// <summary>Navy on PDF pages; a light blue primary and an amber highlight on the dark slides.</summary>
    public static readonly Brand Brand = new()
    {
        Name = "brand-dark",
        Colors = new BrandColors { Primary = "123B5C", Highlight = "B45309" },
        SlideColors = new BrandColors { Primary = "8CC4F0", Highlight = "F5C04A" },
        FooterText = "Made-up Corporation",
        Pptx = new PptxBrandTemplate(Content),
    };

    public static PptxOptions Options => new() { Template = new BrandTemplateOptions { Brand = Brand } };

    private static byte[] Build()
    {
        using var buffer = new MemoryStream();
        buffer.Write(Samples.TemplateBytes(new BrandTemplateOptions { Brand = Brand.Neutral with { Name = "plain" }, ShowDate = true }));
        using (var package = PresentationDocument.Open(buffer, true))
        {
            var master = package.PresentationPart!.SlideMasterParts.Single();
            var slideMaster = master.SlideMaster!;
            slideMaster.CommonSlideData!.Background = new P.Background(new P.BackgroundProperties(
                new A.GradientFill(
                    new A.GradientStopList(
                        new A.GradientStop(new A.RgbColorModelHex { Val = "06121F" }) { Position = 0 },
                        new A.GradientStop(new A.RgbColorModelHex { Val = "1B4D72" }) { Position = 100000 }),
                    new A.LinearGradientFill { Angle = 2700000, Scaled = false }),
                new A.EffectList()));
            White(slideMaster.TextStyles!);
            slideMaster.CommonSlideData.ShapeTree!.Append(FooterPlaceholder(90));
            foreach (var layout in master.SlideLayoutParts)
            {
                layout.SlideLayout!.CommonSlideData!.Background = null;
                layout.SlideLayout.CommonSlideData.ShapeTree!.Append(FooterPlaceholder(90));
            }

            var titleOnly = master.SlideLayoutParts.Single(l => l.SlideLayout!.Type?.Value == P.SlideLayoutValues.TitleOnly);
            var title = master.SlideLayoutParts.Single(l => l.SlideLayout!.Type?.Value == P.SlideLayoutValues.Title);
            Copy(master, titleOnly, TwoLineLayout);
            Copy(master, title, ClosingLayout);
        }

        return buffer.ToArray();
    }

    // Every level of the title, body and other text styles in white.
    private static void White(P.TextStyles styles)
    {
        foreach (var properties in styles.Descendants<A.DefaultRunProperties>())
        {
            foreach (var fill in properties.Elements<A.SolidFill>().ToList())
            {
                fill.Remove();
            }

            var white = new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" });
            if (properties.GetFirstChild<A.LatinFont>() is { } latin)
            {
                properties.InsertBefore(white, latin);
            }
            else
            {
                properties.PrependChild(white);
            }
        }
    }

    private static P.Shape FooterPlaceholder(uint id) => new(
        new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Footer Placeholder" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Footer, Size = P.PlaceholderSizeValues.Quarter, Index = 13 })),
        new P.ShapeProperties(new A.Transform2D(new A.Offset { X = 4038600, Y = 6400800 }, new A.Extents { Cx = 4114800, Cy = 228600 })),
        new P.TextBody(
            new A.BodyProperties(),
            new A.ListStyle(new A.Level1ParagraphProperties(new A.DefaultRunProperties(new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" })) { FontSize = 1000 })),
            new A.Paragraph(new A.EndParagraphRunProperties())));

    // A copy of `source` under another name, as a custom layout of the master.
    private static void Copy(SlideMasterPart master, SlideLayoutPart source, string name)
    {
        var copy = master.AddNewPart<SlideLayoutPart>();
        var layout = (P.SlideLayout)source.SlideLayout!.CloneNode(true);
        layout.Type = P.SlideLayoutValues.Custom;
        layout.CommonSlideData!.Name = name;
        copy.SlideLayout = layout;
        copy.AddPart(master);
        foreach (var image in source.ImageParts)
        {
            copy.AddPart(image, source.GetIdOfPart(image));
        }

        var ids = master.SlideMaster!.SlideLayoutIdList!;
        var next = ids.Elements<P.SlideLayoutId>().Max(i => i.Id!.Value) + 1;
        ids.Append(new P.SlideLayoutId { Id = next, RelationshipId = master.GetIdOfPart(copy) });
    }
}
