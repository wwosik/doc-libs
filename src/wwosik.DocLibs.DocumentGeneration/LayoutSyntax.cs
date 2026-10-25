using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The names in slide-layout Markdown that both generators check alike: <c>:::</c> containers and alert kinds. Decks
/// draw them; PDFs show their content plainly, but reject the same mistakes.
/// </summary>
internal static class LayoutSyntax
{
    /// <summary>The containers, for error messages.</summary>
    public const string Containers = "cards, rows, panel, statement, columns";

    /// <summary>The alerts, for error messages.</summary>
    public const string Alerts = "NOTE, TIP, IMPORTANT, WARNING, CAUTION";

    /// <summary>
    /// Checks a container's name and arguments: <c>panel</c> takes <c>dark</c>, <c>columns</c> a ratio of two or three
    /// widths (<c>1:2</c>); the others take none.
    /// </summary>
    /// <exception cref="DocumentConversionException">The name is unknown or the arguments are not allowed.</exception>
    public static void CheckContainer(string name, string arguments, DocumentFormat format)
    {
        if (name is not ("cards" or "rows" or "panel" or "statement" or "columns"))
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
