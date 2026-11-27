using System.Text.Json;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A generator that takes structured input, JSON that follows a JSON Schema, instead of Markdown: a workbook from
/// <c>XlsxGenerator.ForOptions</c> is the first. <see cref="DocumentTool"/> offers it beside Markdown when the
/// <see cref="IMarkdownGenerator"/> it is given also implements this interface: the tool's arguments then get the
/// parameter <see cref="ParameterName"/> with <see cref="ParameterSchema"/>, inline, and
/// <see cref="ParameterName"/><c>Resource</c>, the path of an attached <c>.json</c> resource with the same content.
/// </summary>
public interface IStructuredGenerator
{
    /// <summary>The format's short name, as <see cref="IMarkdownGenerator.Format"/>: <c>xlsx</c>.</summary>
    string Format { get; }

    /// <summary>The name of the tool's parameter that carries the input, in camelCase: <c>workbook</c>.</summary>
    string ParameterName { get; }

    /// <summary>The JSON Schema of the input, as indented JSON.</summary>
    string ParameterSchema { get; }

    /// <summary>
    /// How to write the input, for <see cref="DocumentTool.Guide"/>, which puts it after the format's
    /// <see cref="IMarkdownGenerator.AuthoringGuide"/>: Markdown starting at <c>##</c>, in English, without the schema itself.
    /// </summary>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    string StructuredGuide();

    /// <summary>Writes the file <paramref name="input"/> describes to <paramref name="output"/> and returns the pages and notes.</summary>
    /// <param name="input">The input, following <see cref="ParameterSchema"/>.</param>
    /// <param name="resources">The files the host gave the agent, such as images.</param>
    /// <param name="output">Where the file goes.</param>
    /// <exception cref="DocumentConversionException">
    /// The input does not follow the schema, or the file it describes has an error; the message names each problem's JSON
    /// path, such as <c>$.sheets[0].rows[2][1]</c>.
    /// </exception>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    GenerationResult Generate(JsonElement input, IReadOnlyList<DocumentResource> resources, Stream output);

    /// <summary>
    /// Checks <paramref name="input"/> without writing anything: every problem as a note whose message starts with its JSON
    /// path, <c>$.sheets[0].rows[2][1]: …</c>, problems with the shape (<c>json-invalid</c>) first. Errors are returned, not
    /// thrown; with any, <see cref="CheckResult.Error"/> says what <see cref="Generate"/> would throw.
    /// </summary>
    /// <exception cref="ArgumentException">The options are invalid.</exception>
    CheckResult Check(JsonElement input);
}
