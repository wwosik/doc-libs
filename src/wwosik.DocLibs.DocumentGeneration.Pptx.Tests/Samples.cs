using System.Runtime.CompilerServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using wwosik.DocLibs.DocumentConversions;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>Sample files, their committed <c>*.expected.md</c>, and helpers to inspect generated decks.</summary>
internal static class Samples
{
    /// <summary>Set to <c>1</c> to write the actual output over the committed expectations instead of comparing.</summary>
    public const string UpdateVariable = "DOCLIBS_UPDATE_EXPECTED";

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", name);

    public static DocumentResource Image(string name) => new(name, "image/png", File.ReadAllBytes(Path(name)));

    /// <summary>The sample deck with its two images and an author.</summary>
    public static MarkdownDocument Deck() => new(
        File.ReadAllText(Path("deck.md")).ReplaceLineEndings("\n"),
        [Image("diagram.png"), Image("photo.png")],
        new Dictionary<string, string> { [MetadataKeys.Author] = "DocLibs" });

    public static MarkdownDocument Markdown(string markdown, params DocumentResource[] resources) =>
        new(markdown, resources, new Dictionary<string, string>());

    public static byte[] Generate(MarkdownDocument document, PptxOptions? options = null)
    {
        using var output = new MemoryStream();
        PptxGenerator.Generate(document, output, options);
        return output.ToArray();
    }

    /// <summary>The made-up brand of <c>Samples/brand-sample</c>: colours, footer text, all four images, Liberation Serif with PT Sans Narrow in tables and Inconsolata for code, a highlight and two label colours.</summary>
    public static readonly Brand Brand = BrandPack.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Samples", "brand-sample"));

    /// <summary>The neutral and the sample brand, by their front-matter name, for theories.</summary>
    public static TheoryData<string> BuiltInTemplates => ["neutral", "sample"];

    public static PptxTemplateOptions BuiltIn(string name) => new BrandTemplateOptions { Brand = name == "sample" ? Brand : Brand.Neutral };

    public static byte[] TemplateBytes(PptxTemplateOptions template)
    {
        using var output = new MemoryStream();
        template.Write(output);
        return output.ToArray();
    }

    /// <summary>A template whose layouts are changed by <paramref name="edit"/> before use.</summary>
    public static byte[] EditedTemplate(PptxTemplateOptions template, Action<SlideMasterPart> edit)
    {
        using var edited = new MemoryStream();
        edited.Write(TemplateBytes(template));
        using (var package = PresentationDocument.Open(edited, true))
        {
            edit(package.PresentationPart!.SlideMasterParts.Single());
        }

        return edited.ToArray();
    }

    /// <summary>Removes the layout of <paramref name="type"/> from the master.</summary>
    public static void RemoveLayout(SlideMasterPart master, P.SlideLayoutValues type)
    {
        var layout = master.SlideLayoutParts.Single(l => l.SlideLayout!.Type!.Value == type);
        var relationshipId = master.GetIdOfPart(layout);
        master.SlideMaster!.SlideLayoutIdList!.Elements<P.SlideLayoutId>().Single(id => id.RelationshipId == relationshipId).Remove();
        master.DeletePart(layout);
    }

    public static string LayoutName(SlidePart slide) => slide.SlideLayoutPart!.SlideLayout!.CommonSlideData!.Name!.Value!;

    /// <summary>The text of the slide's placeholder of <paramref name="type"/>, or <see langword="null"/> if it has none.</summary>
    public static string? PlaceholderText(SlidePart slide, P.PlaceholderValues type) =>
        slide.Slide!.Descendants<P.Shape>()
            .FirstOrDefault(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == type)
            ?.TextBody?.InnerText;

    public static PresentationDocument Open(byte[] pptx) => PresentationDocument.Open(new MemoryStream(pptx), false);

    public static List<SlidePart> Slides(PresentationDocument package)
    {
        var presentation = package.PresentationPart!;
        return presentation.Presentation!.SlideIdList?.Elements<P.SlideId>()
            .Select(id => (SlidePart)presentation.GetPartById(id.RelationshipId!))
            .ToList() ?? [];
    }

    public static void AssertValid(byte[] pptx)
    {
        using var package = Open(pptx);
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(package)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.Empty(errors);
    }

    public static void AssertMatchesExpected(string sample, string actual, [CallerFilePath] string caller = "")
    {
        var name = sample + ".expected.md";
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(caller)!, "Samples", name), actual);
            return;
        }

        Assert.Equal(File.ReadAllText(Path(name)).ReplaceLineEndings("\n"), actual);
    }

    /// <summary>Writes a generated file to <c>.tmp/review/</c> at the repo root, for a human to open.</summary>
    public static void WriteForReview(string name, byte[] content, [CallerFilePath] string caller = "")
    {
        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(caller)!, "..", "..", ".tmp", "review"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(System.IO.Path.Combine(directory, name), content);
    }

    /// <summary>A code-block renderer from a function.</summary>
    public static ICodeBlockRenderer Renderer(Func<CodeBlockRequest, CodeBlockGraphic?> render) => new FunctionRenderer(render);

    private sealed class FunctionRenderer(Func<CodeBlockRequest, CodeBlockGraphic?> render) : ICodeBlockRenderer
    {
        public CodeBlockGraphic? Render(CodeBlockRequest request) => render(request);
    }
}
