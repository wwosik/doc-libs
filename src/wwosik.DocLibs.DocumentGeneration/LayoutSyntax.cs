using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The names in slide-layout Markdown that both generators check alike: <c>:::</c> containers and alert kinds. Decks
/// draw them; PDFs show their content plainly, but reject the same mistakes.
/// </summary>
internal static class LayoutSyntax
{
    /// <summary>The containers, for error messages.</summary>
    public const string Containers = "cards, rows, panel, statement";

    /// <summary>The alerts, for error messages.</summary>
    public const string Alerts = "NOTE, TIP, IMPORTANT, WARNING, CAUTION";

    /// <summary>Checks a container's name and arguments: only <c>panel</c> takes one, <c>dark</c>.</summary>
    /// <exception cref="DocumentConversionException">The name is unknown or the arguments are not allowed.</exception>
    public static void CheckContainer(string name, string arguments, DocumentFormat format)
    {
        if (name is not ("cards" or "rows" or "panel" or "statement"))
        {
            throw new DocumentConversionException(format, $"Unknown container '::: {name}'. Valid containers: {Containers}.");
        }

        if (arguments.Length > 0 && !(name == "panel" && arguments == "dark"))
        {
            throw new DocumentConversionException(
                format,
                $"The container '::: {name} {arguments}' takes no arguments{(name == "panel" ? " but dark" : string.Empty)}.");
        }
    }

    /// <summary>Whether an alert kind is one to stand out (important, warning, caution) rather than a note or tip.</summary>
    /// <exception cref="DocumentConversionException">The kind is unknown.</exception>
    public static bool IsImportant(string kind, DocumentFormat format) => kind.ToUpperInvariant() switch
    {
        "NOTE" or "TIP" => false,
        "IMPORTANT" or "WARNING" or "CAUTION" => true,
        _ => throw new DocumentConversionException(format, $"Unknown alert '[!{kind}]'. Valid alerts: {Alerts}."),
    };
}
