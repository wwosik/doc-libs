using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A generator bound to its options: <c>PptxGenerator.ForOptions</c>, <c>PdfGenerator.ForOptions</c> and
/// <c>DocxGenerator.ForOptions</c> return one. It joins the generators, which live in packages of their own, so that
/// <see cref="DocumentTool"/> can offer them without depending on any of them.
/// </summary>
public interface IMarkdownGenerator
{
    /// <summary>The format's short name, the value of the tool's <c>format</c> parameter: <c>pptx</c>, <c>pdf</c>, <c>docx</c>.</summary>
    string Format { get; }

    /// <summary>What the format is called in prose: <c>PowerPoint deck</c>, <c>PDF document</c>, <c>Word document</c>.</summary>
    string Title { get; }

    /// <summary>The file extension with its dot, e.g. <c>.pptx</c>.</summary>
    string FileExtension { get; }

    /// <summary>The MIME type of the files it writes, e.g. <c>application/pdf</c>.</summary>
    string ContentType { get; }

    /// <summary>The generator's authoring guide for its options: Markdown starting at <c>##</c>, in English.</summary>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    string AuthoringGuide();

    /// <summary>Writes <paramref name="document"/> to <paramref name="output"/> and returns the pages and notes.</summary>
    /// <exception cref="DocumentConversions.DocumentConversionException">The document has an error.</exception>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    GenerationResult Generate(MarkdownDocument document, Stream output);

    /// <summary>Checks <paramref name="document"/> without writing anything; document errors are returned, not thrown.</summary>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    CheckResult Check(MarkdownDocument document);
}
