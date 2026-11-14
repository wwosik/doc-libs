using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The names in slide-layout Markdown that both generators check alike: <c>:::</c> containers, alert kinds and
/// transitions. Decks draw them; PDFs show their content plainly or leave it out, but reject the same mistakes.
/// </summary>
internal static class LayoutSyntax
{
    /// <summary>
    /// The containers, for error messages: the layouts, <c>pagebreak</c>, <c>notes</c>, then the drawn ones
    /// (<see cref="VisualContainers"/>).
    /// </summary>
    public static readonly string Containers = string.Join(", ", ["cards", "rows", "panel", "statement", "columns", PageBreak, SlideKinds.Conclusion, SlideKinds.Comment, Notes, .. VisualContainers.Names]);

    /// <summary>The alerts, for error messages.</summary>
    public const string Alerts = "NOTE, TIP, IMPORTANT, WARNING, CAUTION";

    /// <summary>
    /// A forced page break in documents, <c>::: pagebreak</c> (<see cref="PageBreaks"/>): no arguments, its content
    /// follows the break, its closing <c>:::</c> is optional. Slides have no pages to break.
    /// </summary>
    public const string PageBreak = "pagebreak";

    /// <summary>The container of a slide's speaker notes, <c>::: notes</c>.</summary>
    public const string Notes = "notes";

    /// <summary>The transitions between slides, as <c>transition:</c> and <c>transition="…"</c> name them.</summary>
    public static readonly IReadOnlyList<string> Transitions = ["none", "fade", "push", "wipe", "split", "cover", "uncover", "zoom", "cut", "morph"];

    /// <summary>
    /// Checks a container's name and arguments: <c>panel</c> takes <c>dark</c>, <c>columns</c> a ratio of two or three
    /// widths (<c>1:2</c>), the drawn containers their <c>key=value</c> arguments; the others, <c>pagebreak</c> among
    /// them, take none.
    /// </summary>
    /// <exception cref="DocumentConversionException">The name is unknown or the arguments are not allowed.</exception>
    public static void CheckContainer(string name, string arguments, DocumentFormat format)
    {
        if (VisualContainers.IsVisual(name))
        {
            VisualContainers.Check(name, arguments, format);
            return;
        }

        if (name is not ("cards" or "rows" or "panel" or "statement" or "columns" or PageBreak or SlideKinds.Conclusion or SlideKinds.Comment or Notes))
        {
            throw new DocumentConversionException(format, $"Unknown container '::: {name}'. Valid containers: {Containers}.");
        }

        if (name == "columns")
        {
            if (arguments.Length > 0 && ColumnWidths(arguments) is null)
            {
                throw new DocumentConversionException(
                    format,
                    $"The container '::: columns {arguments}' takes the widths of its columns as a ratio of two or three whole numbers, such as 1:2.");
            }

            return;
        }

        if (arguments.Length > 0 && !(name == "panel" && arguments == "dark"))
        {
            throw new DocumentConversionException(
                format,
                $"The container '::: {name} {arguments}' takes no arguments{(name == "panel" ? " but dark" : string.Empty)}.");
        }
    }

    /// <summary>The transition named <paramref name="name"/>, as it is listed in <see cref="Transitions"/>; case is ignored.</summary>
    /// <exception cref="DocumentConversionException">The name is not a transition.</exception>
    public static string Transition(string name, DocumentFormat format) =>
        Transitions.FirstOrDefault(t => t.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new DocumentConversionException(format, $"Unknown transition '{name}'. Valid transitions: {string.Join(", ", Transitions)}.");

    /// <summary>
    /// Throws if <paramref name="block"/> holds a <c>::: notes</c> anywhere inside it: speaker notes go at the top level
    /// of a section, never in another container, a list or a quote.
    /// </summary>
    /// <exception cref="DocumentConversionException">There is a nested <c>::: notes</c>.</exception>
    public static void CheckNoNestedNotes(Block block, DocumentFormat format)
    {
        if (block is ContainerBlock container
            && container.Descendants<Markdig.Extensions.CustomContainers.CustomContainer>().Any(c => c.Info == Notes))
        {
            throw NestedNotes(format);
        }
    }

    /// <summary>The error for a <c>::: notes</c> inside another block.</summary>
    public static DocumentConversionException NestedNotes(DocumentFormat format) => new(
        format,
        "A '::: notes' goes at the top level of a section, after its heading: not inside another container, a list, a quote or another '::: notes'.");

    /// <summary>Whether an alert kind is one to stand out (important, warning, caution) rather than a note or tip.</summary>
    /// <exception cref="DocumentConversionException">The kind is unknown.</exception>
    public static bool IsImportant(string kind, DocumentFormat format) => kind.ToUpperInvariant() switch
    {
        "NOTE" or "TIP" => false,
        "IMPORTANT" or "WARNING" or "CAUTION" => true,
        _ => throw new DocumentConversionException(format, $"Unknown alert '[!{kind}]'. Valid alerts: {Alerts}."),
    };

    /// <summary>The widths of <c>::: columns 1:2</c> as weights; <see langword="null"/> when they are not two or three whole numbers.</summary>
    public static IReadOnlyList<int>? ColumnWidths(string arguments)
    {
        var parts = arguments.Split(':');
        return parts.Length is 2 or 3 && parts.All(p => int.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is > 0 and < 100)
            ? parts.Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToList()
            : null;
    }
}
