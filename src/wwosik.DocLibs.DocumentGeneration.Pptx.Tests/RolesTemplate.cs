using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using P15 = DocumentFormat.OpenXml.Office2013.PowerPoint;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// <c>brand-roles</c>: a made-up brand whose own template names its layouts in its own way and carries no layout types
/// for them, like many corporate templates — built from the built-in master, so it needs no binary sample. Its layouts
/// for two columns, quotes, charts and dark slides are renamed and typed <c>cust</c>; the content layout gets a
/// decorative body placeholder before its real one; every layout gets a footer placeholder; and the master has drawing
/// guides (<see cref="GuideArea"/>).
/// </summary>
internal static class RolesTemplate
{
    public const string TwoColumns = "Side by Side";
    public const string Quote = "Big Quote";
    public const string Chart = "Figure";
    public const string Dark = "Night";
    public const string ContentLayout = "Title and Content";
    public const string Decoration = "Accent Bar";

    /// <summary>The box inside the master's guides, in EMU: 1 inch from the left and right, 2 inches from the top, above 6.75 inches.</summary>
    public static readonly (long X, long Y, long W, long H) GuideArea = (914400, 1828800, 12192000 - (2 * 914400), 6172200 - 1828800);

    public static readonly byte[] Content = Build(guides: true);

    /// <summary>The same template without guides on its master.</summary>
    public static readonly byte[] WithoutGuides = Build(guides: false);

    public static readonly PptxTemplateRoles Roles = new()
    {
        Layouts = new PptxLayoutRoles { TwoContent = TwoColumns, Quote = Quote, Chart = Chart, Dark = Dark },
        Placeholders =
        [
            new(ContentLayout, Decoration, PlaceholderRole.Decorative),
            new(Quote, "byline", PlaceholderRole.Byline),
            new(TwoColumns, "Content Placeholder 3", PlaceholderRole.Body),
            new(TwoColumns, "Content Placeholder 2", PlaceholderRole.Body),
        ],
        FooterPattern = "{footer} | {chapter}",
    };

    public static readonly Brand Brand = new()
    {
        Name = "brand-roles",
        Colors = new BrandColors { Primary = "0F3D5E" },
        FooterText = "Made-up Ltd",
        Pptx = new PptxBrandTemplate(Content) { Roles = Roles },
    };

    public static PptxOptions Options => new() { Template = new BrandTemplateOptions { Brand = Brand } };

    private static byte[] Build(bool guides)
    {
        using var buffer = new MemoryStream();
        buffer.Write(Samples.TemplateBytes(new BrandTemplateOptions { Brand = Brand.Neutral with { Name = "plain" } }));
        using (var package = PresentationDocument.Open(buffer, true))
        {
            var master = package.PresentationPart!.SlideMasterParts.Single();
            master.SlideMaster!.CommonSlideData!.ShapeTree!.Append(Footer(90));
            foreach (var layout in master.SlideLayoutParts)
            {
                layout.SlideLayout!.CommonSlideData!.ShapeTree!.Append(Footer(90));
            }

            Rename(master, "Two Content", TwoColumns);
            Rename(master, "Quote", Quote);
            Rename(master, "Chart", Chart);
            Rename(master, "Title Only Dark", Dark);

            // A coloured bar drawn by a body placeholder, first in the tree: undeclared, it would take the text.
            var content = Layout(master, ContentLayout).SlideLayout!.CommonSlideData!.ShapeTree!;
            content.InsertAfter(Bar(), content.GetFirstChild<P.GroupShapeProperties>());

            if (guides)
            {
                // Positions in eighths of a point: 1 inch is 576. One horizontal guide above the title, one below the footer.
                master.SlideMaster.Append(new P.SlideMasterExtensionList(new P.SlideMasterExtension(
                    new P15.SlideGuideList(
                        Guide(1, P.DirectionValues.Vertical, 576),
                        Guide(2, P.DirectionValues.Vertical, 7104),
                        Guide(3, null, 288),
                        Guide(4, null, 1152),
                        Guide(5, null, 3888),
                        Guide(6, null, 4200))) { Uri = "{27BBF7A9-308A-43DC-89C8-2F10F3537804}" }));
            }
        }

        return buffer.ToArray();
    }

    private static P15.ExtendedGuide Guide(uint id, P.DirectionValues? orientation, int position)
    {
        var guide = new P15.ExtendedGuide(new P15.ColorType(new A.RgbColorModelHex { Val = "F26B43" })) { Id = id, Position = position };
        if (orientation is { } value)
        {
            guide.Orientation = value;
        }

        return guide;
    }

    public static SlideLayoutPart Layout(SlideMasterPart master, string name) =>
        master.SlideLayoutParts.Single(l => l.SlideLayout!.CommonSlideData!.Name!.Value == name);

    private static void Rename(SlideMasterPart master, string from, string to)
    {
        var layout = Layout(master, from).SlideLayout!;
        layout.CommonSlideData!.Name = to;
        layout.Type = P.SlideLayoutValues.Custom;
    }

    private static P.Shape Bar() => new(
        new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = 91, Name = Decoration },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Body, Size = P.PlaceholderSizeValues.Quarter, Index = 20 })),
        new P.ShapeProperties(
            new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 12192000, Cy = 91440 }),
            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
            new A.SolidFill(new A.RgbColorModelHex { Val = "F5C04A" })),
        new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.EndParagraphRunProperties())));

    private static P.Shape Footer(uint id) => new(
        new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = "Footer Placeholder" },
            new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
            new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Footer, Size = P.PlaceholderSizeValues.Quarter, Index = 13 })),
        new P.ShapeProperties(new A.Transform2D(new A.Offset { X = 4038600, Y = 6400800 }, new A.Extents { Cx = 4114800, Cy = 228600 })),
        new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.EndParagraphRunProperties())));
}
