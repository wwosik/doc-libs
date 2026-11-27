using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// A tool an AI agent can call to write documents: a name, a description, the JSON Schema of its parameters
/// (<c>format</c>, <c>markdown</c>, <c>audience</c>, <c>mode</c>), a combined authoring guide for the formats it offers, and
/// <see cref="Run"/>, which takes the agent's arguments as JSON and returns a reply in a fixed JSON shape. It supplies
/// the parts, not a vendor's envelope: the host puts them into its model API's tool definition. It knows no generator;
/// pass the formats to offer as <see cref="IMarkdownGenerator"/>s, e.g. <c>PdfGenerator.ForOptions(options)</c>.
/// </summary>
public sealed class DocumentTool
{
    /// <summary>The parameter that picks the format.</summary>
    public const string FormatParameter = "format";

    /// <summary>The parameter that holds the whole document.</summary>
    public const string MarkdownParameter = "markdown";

    /// <summary>
    /// The parameter that picks the version, <c>customer</c>, <c>internal</c> or <c>trainer</c> (<see cref="Audience"/>):
    /// optional, it wins over the front matter's <c>audience</c> but not over a version the host's options set.
    /// </summary>
    public const string AudienceParameter = "audience";

    /// <summary>The parameter that picks <c>generate</c> or <c>check</c>.</summary>
    public const string ModeParameter = "mode";

    /// <summary>
    /// The end of the parameter that names an attached <c>.json</c> resource in place of a structured parameter:
    /// <c>workbookResource</c> for <c>workbook</c> (<see cref="IStructuredGenerator.ParameterName"/>).
    /// </summary>
    public const string ResourceSuffix = "Resource";

    private const string Generating = "generate";
    private const string Checking = "check";
    private const string DeckContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",

        // The JSON goes to a model and a host's logs, never into HTML: keep letters such as é and quotes readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IMarkdownGenerator[] generators;
    private readonly Structured[] structured;
    private readonly bool offerCheck;
    private readonly IDeckPreview? preview;

    /// <summary>
    /// Creates the tool for <paramref name="generators"/>, in the order they are offered. A generator that is also an
    /// <see cref="IStructuredGenerator"/>, such as <c>XlsxGenerator.ForOptions</c>, is offered with its structured input
    /// too: the parameters get its <see cref="IStructuredGenerator.ParameterName"/> and that name with
    /// <see cref="ResourceSuffix"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// There is no generator, one is <see langword="null"/> or has an empty <see cref="IMarkdownGenerator.Format"/>, two
    /// share a format, the name does not match <c>^[a-zA-Z0-9_-]{1,64}$</c>, or the description is blank; or a structured
    /// generator's format differs from its Markdown format, its parameter name is invalid or taken, or its schema is not a
    /// JSON object.
    /// </exception>
    public DocumentTool(IReadOnlyList<IMarkdownGenerator> generators, DocumentToolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(generators);
        options ??= new DocumentToolOptions();
        if (generators.Count == 0)
        {
            throw new ArgumentException("The tool needs at least one generator.", nameof(generators));
        }

        var formats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var generator in generators)
        {
            if (generator is null)
            {
                throw new ArgumentException("A generator is null.", nameof(generators));
            }

            if (string.IsNullOrWhiteSpace(generator.Format))
            {
                throw new ArgumentException("A generator has an empty Format.", nameof(generators));
            }

            if (!formats.Add(generator.Format))
            {
                throw new ArgumentException($"Two generators share the format '{generator.Format}'.", nameof(generators));
            }
        }

        if (!IsValidName(options.Name))
        {
            throw new ArgumentException(
                $"The tool name '{options.Name}' must be 1 to 64 letters, digits, '_' or '-'.", nameof(options));
        }

        if (options.Description is { } description && string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("The tool description is blank.", nameof(options));
        }

        this.generators = [.. generators];
        this.structured = [.. this.generators.Select(StructuredOf).OfType<Structured>()];
        var names = new HashSet<string>(StringComparer.Ordinal) { FormatParameter, MarkdownParameter, AudienceParameter, ModeParameter };
        foreach (var offered in this.structured)
        {
            if (!names.Add(offered.Name) || !names.Add(offered.ResourceName))
            {
                throw new ArgumentException($"The structured parameter '{offered.Name}' of format '{offered.Generator.Format}' is taken.", nameof(generators));
            }
        }

        this.offerCheck = options.OfferCheck;
        this.preview = options.Preview;
        this.Name = options.Name;
        this.Description = options.Description ?? this.DefaultDescription();
        this.ParametersSchema = this.Schema();
    }

    /// <summary>The tool's name, <c>create_document</c> unless <see cref="DocumentToolOptions.Name"/> says otherwise.</summary>
    public string Name { get; }

    /// <summary>One paragraph for the model: what the tool does, the formats, how to call it, what it returns.</summary>
    public string Description { get; }

    /// <summary>
    /// The JSON Schema of the arguments, as indented JSON: an object of <c>format</c> (an <c>enum</c> of the offered
    /// formats, required when there are several), <c>markdown</c> (required), <c>audience</c> (optional) and, with
    /// <see cref="DocumentToolOptions.OfferCheck"/>, <c>mode</c>; no other properties. It uses only <c>type</c>,
    /// <c>properties</c>, <c>enum</c>, <c>description</c>, <c>required</c>, <c>default</c> and
    /// <c>additionalProperties</c>, which the major tool APIs all accept. With a structured generator, the object also has
    /// its parameter with its schema (which may use <c>items</c>, <c>anyOf</c> and <c>format</c> as well) and the
    /// parameter of its resource, both optional, after <c>markdown</c>, and <c>markdown</c> is no longer required: a
    /// call gives one of them.
    /// </summary>
    public string ParametersSchema { get; }

    /// <summary>
    /// The combined authoring guide, Markdown under one <c>#</c> heading: how to call the tool and what comes back, then
    /// each offered format's full <see cref="IMarkdownGenerator.AuthoringGuide"/> under <c>## &lt;Title&gt; (format:
    /// "&lt;format&gt;")</c>, its headings moved one level down. Built on first use.
    /// </summary>
    /// <exception cref="ArgumentException">A generator's options are invalid.</exception>
    public string Guide => field ??= this.CombinedGuide();

    /// <summary>The generators offered, in order.</summary>
    public IReadOnlyList<IMarkdownGenerator> Generators => this.generators;

    /// <summary>
    /// Runs one call of the agent: parses <paramref name="arguments"/> (the JSON object the model sent), builds the
    /// document from its <c>markdown</c> and <paramref name="resources"/> (the images the host gave the agent), and
    /// generates it into <paramref name="output"/>, or with <c>"mode": "check"</c> checks it and writes nothing. The
    /// agent's mistakes (arguments that are not a valid call, an error in the document) come back as an error reply, so
    /// the agent can fix them; only the host's errors throw. When <see cref="DocumentToolRun.Succeeded"/> is false,
    /// <paramref name="output"/> may hold partial bytes: generate into a buffer and keep it only on success.
    /// </summary>
    /// <exception cref="ArgumentException">A generator's options are invalid.</exception>
    public DocumentToolRun Run(string arguments, IReadOnlyList<DocumentResource> resources, Stream output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(output);

        var call = this.Parse(arguments, resources, out var problem);
        if (call is null)
        {
            return new DocumentToolRun(ErrorReply(problem!));
        }

        var generator = call.Generator;
        var version = call.Audience is { } asked ? generator.ForAudience(asked) : generator;
        Func<CheckResult> checking;
        Func<Stream, GenerationResult> generating;
        if (call.Input is { } input)
        {
            var data = version as IStructuredGenerator ?? call.Structured!.Input;
            checking = () => data.Check(input);
            generating = stream => data.Generate(input, resources, stream);
        }
        else
        {
            var document = new MarkdownDocument(call.Markdown!, resources, new Dictionary<string, string>());
            checking = () => version.Check(document);
            generating = stream => version.Generate(document, stream);
        }

        if (call.Check)
        {
            var result = checking();
            return new DocumentToolRun(CheckedReply(generator, result)) { Generator = generator, Check = result };
        }

        // With a preview, a deck is written to a buffer first, so the painter can read it back.
        var sheet = this.preview is not null && IsDeck(generator);
        using var buffer = sheet ? new MemoryStream() : null;
        GenerationResult generated;
        try
        {
            generated = generating(buffer ?? output);
        }
        catch (DocumentConversionException exception)
        {
            return new DocumentToolRun(ErrorReply(exception.Reason)) { Generator = generator };
        }

        byte[]? contactSheet = null;
        if (buffer is not null)
        {
            buffer.Position = 0;
            buffer.CopyTo(output);
            try
            {
                contactSheet = this.preview!.ContactSheet(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), generated);
            }
            catch (DocumentConversionException)
            {
                // The deck is written; a deck the painter cannot paint only goes without its sheet.
            }
        }

        return new DocumentToolRun(OkReply(generator, generated, contactSheet is not null))
        {
            Succeeded = true,
            Generator = generator,
            Result = generated,
            ContactSheet = contactSheet,
        };
    }

    // The structured input of `generator`, checked, or null when it takes Markdown only.
    private static Structured? StructuredOf(IMarkdownGenerator generator)
    {
        if (generator is not IStructuredGenerator input)
        {
            return null;
        }

        if (!string.Equals(input.Format, generator.Format, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The generator of format '{generator.Format}' takes structured input of format '{input.Format}'.", "generators");
        }

        if (input.ParameterName is not { Length: >= 1 and <= 40 } name || !char.IsAsciiLetterLower(name[0]) || !name.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException($"The structured parameter '{input.ParameterName}' of format '{generator.Format}' must be 1 to 40 ASCII letters and digits, starting with a lower-case letter.", "generators");
        }

        JsonElement schema;
        try
        {
            using var document = JsonDocument.Parse(input.ParameterSchema);
            schema = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The schema of '{name}' is not valid JSON: {exception.Message}", "generators", exception);
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"The schema of '{name}' is not a JSON object.", "generators");
        }

        return new Structured(generator, input, name, schema);
    }

    /// <summary>Whether <paramref name="generator"/> writes decks, the only files that get a contact sheet.</summary>
    private static bool IsDeck(IMarkdownGenerator generator) =>
        string.Equals(generator.ContentType, DeckContentType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(generator.FileExtension, ".pptx", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidName(string? name) =>
        name is { Length: >= 1 and <= 64 } && name.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-');

    private Call? Parse(string arguments, IReadOnlyList<DocumentResource> resources, out string? problem)
    {
        problem = null;
        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(arguments);
        }
        catch (JsonException exception)
        {
            problem = $"The arguments are not valid JSON ({exception.Message}). Send one JSON object with {this.PropertyList()}.";
            return null;
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problem = $"The arguments must be a JSON object with {this.PropertyList()}, not {Kind(root.ValueKind)}.";
                return null;
            }

            string? format = null, markdown = null, mode = null, audience = null;
            var data = new List<(Structured Offered, string Name, JsonElement? Inline, string? Resource)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                var offered = Array.Find(this.structured, s => s.Name == property.Name || s.ResourceName == property.Name);
                var known = offered is not null || property.Name is FormatParameter or MarkdownParameter or AudienceParameter || (this.offerCheck && property.Name == ModeParameter);
                if (!known)
                {
                    problem = $"Unknown property '{property.Name}'. The tool takes {this.PropertyList()}.";
                    return null;
                }

                if (!seen.Add(property.Name))
                {
                    problem = $"The property '{property.Name}' is given twice.";
                    return null;
                }

                // A null counts as not given: some models send every property, with null for those they leave out.
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (offered is not null && property.Name == offered.Name)
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        problem = $"'{property.Name}' must be an object, not {Kind(property.Value.ValueKind)}.";
                        return null;
                    }

                    data.Add((offered, property.Name, property.Value.Clone(), null));
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    problem = $"'{property.Name}' must be a string, not {Kind(property.Value.ValueKind)}.";
                    return null;
                }

                var value = property.Value.GetString()!;
                if (offered is not null)
                {
                    data.Add((offered, property.Name, null, value));
                    continue;
                }

                switch (property.Name)
                {
                    case FormatParameter: format = value; break;
                    case MarkdownParameter: markdown = value; break;
                    case AudienceParameter: audience = value; break;
                    default: mode = value; break;
                }
            }

            if (data.Count > 1)
            {
                problem = $"Give only one of {Join([.. data.Select(d => $"'{d.Name}'")], "and")}.";
                return null;
            }

            var given = data.Count == 1 ? data[0] : default;
            if (given.Offered is not null && markdown is not null)
            {
                problem = $"Give either '{MarkdownParameter}' or '{given.Name}', not both.";
                return null;
            }

            IMarkdownGenerator? generator;
            if (format is null)
            {
                if (given.Offered is not null)
                {
                    generator = given.Offered.Generator;
                }
                else if (this.generators.Length > 1)
                {
                    problem = $"'{FormatParameter}' is missing: give one of {this.FormatList("or")}.";
                    return null;
                }
                else
                {
                    generator = this.generators[0];
                }
            }
            else
            {
                generator = Array.Find(this.generators, g => string.Equals(g.Format, format.Trim(), StringComparison.OrdinalIgnoreCase));
                if (generator is null)
                {
                    problem = this.generators.Length > 1
                        ? $"Unknown format '{format}': give one of {this.FormatList("or")}."
                        : $"Unknown format '{format}': the only format is {Quoted(this.generators[0].Format)}.";
                    return null;
                }

                if (given.Offered is not null && generator != given.Offered.Generator)
                {
                    problem = $"'{given.Name}' is for the format {Quoted(given.Offered.Generator.Format)}, but '{FormatParameter}' is {Quoted(format)}. Leave '{FormatParameter}' out, or give {Quoted(given.Offered.Generator.Format)}.";
                    return null;
                }
            }

            var check = false;
            if (mode is not null)
            {
                check = string.Equals(mode.Trim(), Checking, StringComparison.OrdinalIgnoreCase);
                if (!check && !string.Equals(mode.Trim(), Generating, StringComparison.OrdinalIgnoreCase))
                {
                    problem = $"Unknown mode '{mode}': give \"{Generating}\" or \"{Checking}\".";
                    return null;
                }
            }

            Audience? version = null;
            if (audience is not null)
            {
                version = audience.Trim().ToLowerInvariant() switch
                {
                    "customer" => Audience.Customer,
                    "internal" => Audience.Internal,
                    "trainer" => Audience.Trainer,
                    _ => null,
                };
                if (version is null)
                {
                    problem = $"Unknown audience '{audience}': give \"customer\", \"internal\" or \"trainer\".";
                    return null;
                }
            }

            if (given.Offered is { } structured)
            {
                var input = given.Inline ?? Load(given.Resource!, given.Name, resources, out problem);
                return input is null ? null : new Call(generator, null, structured, input, check, version);
            }

            if (markdown is null)
            {
                problem = $"'{MarkdownParameter}' is missing: {this.WhatToPut()}";
                return null;
            }

            if (string.IsNullOrWhiteSpace(markdown))
            {
                problem = $"'{MarkdownParameter}' is empty: {this.WhatToPut()}";
                return null;
            }

            return new Call(generator, markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), null, null, check, version);
        }
    }

    // What goes into 'markdown', or into a structured parameter instead.
    private string WhatToPut() => this.structured.Length == 0
        ? "put the whole document in it, front matter included."
        : "put the whole document in it, front matter included, or " + Join(
            [.. this.structured.Select(s => $"for the format {Quoted(s.Generator.Format)} the data in '{s.Name}' or '{s.ResourceName}'")], "or") + ".";

    // The JSON of the resource at `path`, or null with the problem.
    private static JsonElement? Load(string path, string parameter, IReadOnlyList<DocumentResource> resources, out string? problem)
    {
        problem = null;
        var resource = resources.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.Ordinal))
            ?? resources.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            problem = resources.Count == 0
                ? $"'{parameter}' names the file '{path}', but no file is attached."
                : $"'{parameter}' names the file '{path}', which is not attached; the attached files are {Join([.. resources.Select(r => $"'{r.Path}'")], "and")}.";
            return null;
        }

        try
        {
            var content = resource.Content.Span;
            if (content.StartsWith("\uFEFF"u8))
            {
                content = content[3..];
            }

            var reader = new Utf8JsonReader(content, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            problem = $"The file '{path}' that '{parameter}' names is not valid JSON ({exception.Message}).";
            return null;
        }
    }

    private static string Kind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null",
    };

    // A title with its article: "a PDF document", "an Excel workbook".
    private static string A(string title) => (title.Length > 0 && "AEIOUaeiou".Contains(title[0], StringComparison.Ordinal) ? "an " : "a ") + title;

    private static string Quoted(string value) => "\"" + value + "\"";

    private static string Join(IReadOnlyList<string> items, string last) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " " + last + " " + items[^1];

    private string FormatList(string last) => Join([.. this.generators.Select(g => Quoted(g.Format))], last);

    private string PropertyList()
    {
        var names = new List<string>();
        names.Add(this.generators.Length > 1 ? $"'{FormatParameter}'" : $"'{FormatParameter}' (optional)");
        names.Add($"'{MarkdownParameter}'");
        foreach (var offered in this.structured)
        {
            names.Add($"'{offered.Name}' (optional)");
            names.Add($"'{offered.ResourceName}' (optional)");
        }

        names.Add($"'{AudienceParameter}' (optional)");
        if (this.offerCheck)
        {
            names.Add($"'{ModeParameter}' (optional)");
        }

        return "the properties " + Join(names, "and");
    }

    private string Offered(string last) => Join([.. this.generators.Select(g => $"{A(g.Title)} (format \"{g.Format}\")")], last);

    private string DefaultDescription()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Creates a document from Markdown: {this.Offered("or")}. ");
        text.Append("Pass the whole document, front matter included, in 'markdown', one call per document. ");
        foreach (var offered in this.structured)
        {
            text.Append(CultureInfo.InvariantCulture, $"For {A(offered.Generator.Title)} (format \"{offered.Generator.Format}\") the data may go in '{offered.Name}' as JSON instead, or in an attached .json file named by '{offered.ResourceName}'. ");
        }

        if (this.offerCheck)
        {
            text.Append("With mode \"check\" it checks the Markdown and writes nothing, returning every problem at once; check first, fix, then generate. ");
            text.Append("The reply is JSON: status \"ok\" with the pages and notes, \"checked\" with notes and statistics, or \"error\" with what to change. ");
        }
        else
        {
            text.Append("The reply is JSON: status \"ok\" with the pages and notes, or \"error\" with what to change. ");
        }

        text.Append(CultureInfo.InvariantCulture, $"Write the Markdown as the guide 'Writing documents with {this.Name}' describes.");
        return text.ToString();
    }

    private string Schema()
    {
        var several = this.generators.Length > 1;
        return Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "object");
            writer.WriteStartObject("properties");

            writer.WriteStartObject(FormatParameter);
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            foreach (var generator in this.generators)
            {
                writer.WriteStringValue(generator.Format);
            }

            writer.WriteEndArray();
            if (!several)
            {
                writer.WriteString("default", this.generators[0].Format);
            }

            writer.WriteString(
                "description",
                several
                    ? $"The file to create: {Join([.. this.generators.Select(g => $"\"{g.Format}\" for {A(g.Title)}")], "or")}. The guide has a section on the Markdown of each."
                    : $"The file to create; the only format is \"{this.generators[0].Format}\", {A(this.generators[0].Title)}, so it may be left out.");
            writer.WriteEndObject();

            writer.WriteStartObject(MarkdownParameter);
            writer.WriteString("type", "string");
            writer.WriteString(
                "description",
                this.structured.Length == 0
                    ? "The whole document as Markdown, front matter included, written as the guide describes for the format."
                    : $"The whole document as Markdown, front matter included, written as the guide describes for the format. Leave it out when you give {Join([.. this.structured.Select(s => $"'{s.Name}' or '{s.ResourceName}'")], "or")}.");
            writer.WriteEndObject();

            foreach (var offered in this.structured)
            {
                var format = Quoted(offered.Generator.Format);
                writer.WriteStartObject(offered.Name);
                var own = offered.Schema.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? " " + d.GetString() : string.Empty;
                writer.WriteString("description", $"The {offered.Generator.Title} as data, instead of '{MarkdownParameter}'; it implies the format {format}.{own}");
                foreach (var keyword in offered.Schema.EnumerateObject().Where(k => k.Name != "description"))
                {
                    keyword.WriteTo(writer);
                }

                writer.WriteEndObject();

                writer.WriteStartObject(offered.ResourceName);
                writer.WriteString("type", "string");
                writer.WriteString(
                    "description",
                    $"The path of an attached .json file that holds what '{offered.Name}' would hold, instead of '{offered.Name}' or '{MarkdownParameter}'; it implies the format {format}.");
                writer.WriteEndObject();
            }

            writer.WriteStartObject(AudienceParameter);
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            writer.WriteStringValue("customer");
            writer.WriteStringValue("internal");
            writer.WriteStringValue("trainer");
            writer.WriteEndArray();
            writer.WriteString("default", "customer");
            writer.WriteString(
                "description",
                "The version to write, as the guide's section *Versions* describes: \"customer\" (the default) leaves out what is marked internal and the solutions, \"internal\" shows what is marked internal, \"trainer\" also the solutions. Wins over the front matter's 'audience'.");
            writer.WriteEndObject();

            if (this.offerCheck)
            {
                writer.WriteStartObject(ModeParameter);
                writer.WriteString("type", "string");
                writer.WriteStartArray("enum");
                writer.WriteStringValue(Generating);
                writer.WriteStringValue(Checking);
                writer.WriteEndArray();
                writer.WriteString("default", Generating);
                writer.WriteString(
                    "description",
                    "\"generate\" (the default) writes the file; \"check\" writes nothing and returns every problem in the Markdown at once, with statistics.");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartArray("required");
            if (several)
            {
                writer.WriteStringValue(FormatParameter);
            }

            if (this.structured.Length == 0)
            {
                writer.WriteStringValue(MarkdownParameter);
            }

            writer.WriteEndArray();
            writer.WriteBoolean("additionalProperties", false);
            writer.WriteEndObject();
        });
    }

    private string CombinedGuide()
    {
        var several = this.generators.Length > 1;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# Writing documents with {this.Name}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"The tool `{this.Name}` turns Markdown into a file: {this.Offered("or")}. ");
        text.Append("This guide says how to call it and, below, what Markdown each format understands.\n\n");

        text.Append("## Calling the tool\n\n");
        text.Append("- **One call per document.** Put the whole document in `markdown`, front matter included.\n");
        text.Append(several
            ? $"- **`format`** picks the file: {this.FormatList("or")}. It is required.\n"
            : $"- **`format`** may be left out: the only format is {Quoted(this.generators[0].Format)}.\n");
        foreach (var offered in this.structured)
        {
            text.Append(CultureInfo.InvariantCulture, $"- **Data instead of Markdown.** For {A(offered.Generator.Title)} (`{offered.Generator.Format}`), give the data as JSON in `{offered.Name}` instead of `markdown`, ");
            text.Append(CultureInfo.InvariantCulture, $"or attach a `.json` file and give its path in `{offered.ResourceName}`; its section says how. Either one implies the format. ");
            text.Append(CultureInfo.InvariantCulture, $"Giving it with `markdown`, both of them, or with another `format` is an error. Each problem names the JSON path of its value, `$.sheets[0].rows[2][1]: …`.\n");
        }

        text.Append("- **`audience`** may be left out; it picks the version, `customer`, `internal` or `trainer`, as each format's section *Versions* says.\n");
        if (this.offerCheck)
        {
            text.Append("- **Check first.** With `\"mode\": \"check\"` the tool checks the Markdown and writes nothing. It returns every problem at once, ");
            text.Append("where generating stops at the first error. Fix what it reports, then call again with `\"mode\": \"generate\"` or without `mode`.\n");
        }

        text.Append('\n');
        text.Append("## What comes back\n\n");
        text.Append("The tool replies with JSON, in one of these shapes:\n\n");
        text.Append("- **`\"status\": \"ok\"`**: the file was written. `pages` counts the slides or pages written, ");
        text.Append("and is 0 when the program that opens the file lays out the pages, as Word does. `notes` lists what the format did to your Markdown that you cannot see, ");
        text.Append("such as a slide that did not fit: each with `code`, `line` (in your Markdown, front matter included; 0 when unknown) and `message`. ");
        text.Append("Each format's section *Notes you may get back* explains the codes. When a note matters, fix the Markdown and call again.\n");
        if (this.offerCheck)
        {
            text.Append("- **`\"status\": \"checked\"`**: the result of a check: `pages`, `notes` as above with the check's own rules among them, ");
            text.Append("and `statistics` (`words`, `images`, `tables`, `diagrams`; for decks `slidesByKind` and `talkTimeMinutes`). ");
            text.Append("When the document cannot be generated, `error` says why.\n");
        }

        text.Append("- **`\"status\": \"error\"`**: nothing was written. `error` says what to change, in the call or in the Markdown.\n");

        if (this.preview is not null && this.generators.Any(IsDeck))
        {
            text.Append("\n## Looking at the slides\n\n");
            text.Append("When a deck is written, the reply has `\"contactSheet\": \"attached\"`, and an image comes with it: ");
            text.Append("a contact sheet of the slides, four to a row, each numbered as in the deck. ");
            text.Append(CultureInfo.InvariantCulture, $"It shows at most {IDeckPreview.MaxSlides} slides; for a longer deck, `contactSheetNote` says which. ");
            text.Append("Look at it before you finish: text that is cut off or crowded, a slide that is nearly empty, ");
            text.Append("a picture that is too small, slides that look alike. Fix the Markdown and call again. ");
            text.Append("Without `contactSheet` in the reply, there is no image for that file.\n");
        }

        foreach (var generator in this.generators)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## {generator.Title} (format: \"{generator.Format}\")\n\n");
            text.Append(HeadingsDown(generator.AuthoringGuide().ReplaceLineEndings("\n")).TrimEnd('\n'));
            text.Append('\n');
            if (Array.Find(this.structured, s => s.Generator == generator) is { } offered)
            {
                text.Append('\n').Append(HeadingsDown(offered.Input.StructuredGuide().ReplaceLineEndings("\n")).TrimEnd('\n')).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// <paramref name="markdown"/> with every ATX heading one level down (<c>##</c> to <c>###</c>), headings inside fenced
    /// code untouched; a <c>######</c> heading stays at six, the deepest Markdown has.
    /// </summary>
    internal static string HeadingsDown(string markdown)
    {
        var lines = markdown.Split('\n');
        char fence = '\0';
        var fenceLength = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = 0;
            while (indent < line.Length && indent < 4 && line[indent] == ' ')
            {
                indent++;
            }

            if (indent > 3)
            {
                continue;
            }

            var rest = line.AsSpan(indent);
            if (fence != '\0')
            {
                var run = Count(rest, fence);
                if (run >= fenceLength && rest[run..].IsWhiteSpace())
                {
                    fence = '\0';
                }

                continue;
            }

            if (rest.Length > 0 && rest[0] is '`' or '~')
            {
                var run = Count(rest, rest[0]);
                if (run >= 3 && (rest[0] == '~' || !rest[run..].Contains('`')))
                {
                    fence = rest[0];
                    fenceLength = run;
                    continue;
                }
            }

            var level = Count(rest, '#');
            if (level is >= 1 and <= 5 && (rest.Length == level || rest[level] is ' ' or '\t'))
            {
                lines[i] = line[..indent] + "#" + line[indent..];
            }
        }

        return string.Join('\n', lines);

        static int Count(ReadOnlySpan<char> text, char c)
        {
            var n = 0;
            while (n < text.Length && text[n] == c)
            {
                n++;
            }

            return n;
        }
    }

    private static string OkReply(IMarkdownGenerator generator, GenerationResult result, bool contactSheet) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("status", "ok");
        writer.WriteString("format", generator.Format);
        writer.WriteNumber("pages", result.Pages);
        Notes(writer, result.Notes);
        if (contactSheet)
        {
            writer.WriteString("contactSheet", "attached");
            if (result.Pages > IDeckPreview.MaxSlides)
            {
                writer.WriteString(
                    "contactSheetNote",
                    string.Create(CultureInfo.InvariantCulture, $"The contact sheet shows slides 1 to {IDeckPreview.MaxSlides} of {result.Pages}."));
            }
        }

        writer.WriteEndObject();
    });

    private static string CheckedReply(IMarkdownGenerator generator, CheckResult result) => Write(writer =>
    {
        var statistics = result.Statistics;
        writer.WriteStartObject();
        writer.WriteString("status", "checked");
        writer.WriteString("format", generator.Format);
        if (result.Error is { } error)
        {
            writer.WriteString("error", error);
        }

        writer.WriteNumber("pages", statistics.Pages);
        Notes(writer, result.Notes);
        writer.WriteStartObject("statistics");
        writer.WriteNumber("words", statistics.Words);
        writer.WriteNumber("images", statistics.Images);
        writer.WriteNumber("tables", statistics.Tables);
        writer.WriteNumber("diagrams", statistics.Diagrams);
        if (statistics.TalkTime is { } talkTime)
        {
            writer.WriteStartObject("slidesByKind");
            foreach (var (kind, count) in statistics.SlidesByKind)
            {
                writer.WriteNumber(kind, count);
            }

            writer.WriteEndObject();
            writer.WriteNumber("talkTimeMinutes", (int)Math.Round(talkTime.TotalMinutes, MidpointRounding.AwayFromZero));
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static string ErrorReply(string error) => Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("status", "error");
        writer.WriteString("error", error);
        writer.WriteEndObject();
    });

    private static void Notes(Utf8JsonWriter writer, IReadOnlyList<GenerationNote> notes)
    {
        writer.WriteStartArray("notes");
        foreach (var note in notes)
        {
            writer.WriteStartObject();
            writer.WriteString("code", note.Code);
            writer.WriteNumber("line", note.Line);
            writer.WriteString("message", note.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>A generator's structured input as the tool offers it.</summary>
    private sealed record Structured(IMarkdownGenerator Generator, IStructuredGenerator Input, string Name, JsonElement Schema)
    {
        public string ResourceName => this.Name + ResourceSuffix;
    }

    /// <summary>One valid call: the generator and either the Markdown or the structured input.</summary>
    private sealed record Call(IMarkdownGenerator Generator, string? Markdown, Structured? Structured, JsonElement? Input, bool Check, Audience? Audience);

    private static string Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

/// <summary>How <see cref="DocumentTool"/> presents itself.</summary>
public sealed record DocumentToolOptions
{
    /// <summary>The tool's name: 1 to 64 letters, digits, <c>_</c> or <c>-</c>, as the major model APIs accept. Default <c>create_document</c>.</summary>
    public string Name { get; init; } = "create_document";

    /// <summary>A description of your own, replacing the generated one. Default <see langword="null"/>: generated, naming the formats.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the tool offers <c>"mode": "check"</c>. Default <see langword="true"/>.</summary>
    public bool OfferCheck { get; init; } = true;

    /// <summary>
    /// Paints a contact sheet of every deck the tool writes, such as <c>new DeckContactSheet()</c> from
    /// <c>wwosik.DocLibs.DocumentConversions.Svg</c>: the run then has <see cref="DocumentToolRun.ContactSheet"/>, the reply
    /// <c>"contactSheet": "attached"</c>, and the guide a section on looking at the slides. PDFs and Word documents get
    /// none. Default <see langword="null"/>: no sheet, and replies and guide as without this option.
    /// </summary>
    public IDeckPreview? Preview { get; init; }
}

/// <summary>What one <see cref="DocumentTool.Run"/> did.</summary>
/// <param name="Reply">The reply for the agent, JSON: pass it back as the tool result as it is.</param>
public sealed record DocumentToolRun(string Reply)
{
    /// <summary>Whether a file was written to the output.</summary>
    public bool Succeeded { get; init; }

    /// <summary>The format the call picked, for the file's name and type; <see langword="null"/> when the arguments were not a valid call.</summary>
    public IMarkdownGenerator? Generator { get; init; }

    /// <summary>The generator's result, when a file was written.</summary>
    public GenerationResult? Result { get; init; }

    /// <summary>The check's result, in check mode.</summary>
    public CheckResult? Check { get; init; }

    /// <summary>
    /// A contact sheet of the deck as a PNG (<c>image/png</c>), when <see cref="DocumentToolOptions.Preview"/> is set and
    /// a deck was written; the reply then says <c>"contactSheet": "attached"</c>. Pass it to the model with the reply, as
    /// an image in the same tool result.
    /// </summary>
    public byte[]? ContactSheet { get; init; }
}
