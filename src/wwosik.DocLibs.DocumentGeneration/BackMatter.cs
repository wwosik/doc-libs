using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// What comes after the main part (E06-S07): <c># Backup {.backup}</c> makes that chapter and every later one a deck's
/// backup, slides behind the closing slide for questions; <c># Glossary {.appendix}</c> makes that chapter and every
/// later one a document's appendices, lettered <c>A</c>, <c>B</c> and headed "Appendix A Glossary". Decks ignore
/// <c>.appendix</c>, PDFs and Word documents <c>.backup</c>, each with a <see cref="NoteCodes.AttributeIgnored"/> note;
/// the mistakes are errors in every format alike.
/// </summary>
internal static class BackMatter
{
    /// <summary>The heading attribute of a deck's backup, as written.</summary>
    public const string Backup = ".backup";

    /// <summary>The heading attribute of a document's appendices, as written.</summary>
    public const string Appendix = ".appendix";

    /// <summary>Whether a heading's attribute tokens start a backup or the appendices.</summary>
    public static bool Starts(IReadOnlyCollection<string> tokens) => tokens.Contains(Backup) || tokens.Contains(Appendix);

    /// <summary>
    /// Checks where <c>.backup</c> and <c>.appendix</c> stand in <paramref name="document"/>, before a version takes
    /// anything out, and notes the one <paramref name="format"/> ignores.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// Either attribute is on two headings, on a heading below <c>#</c>, on a heading inside another block, on the cover
    /// or together with <c>.closing</c>; or a closing slide comes after the backup.
    /// </exception>
    public static void Check(ContainerBlock document, DocumentFormat format, Func<MarkdownObject, int> lineOf, GenerationNotes notes)
    {
        var cover = Cover(document, format);
        var first = new Dictionary<string, HeadingBlock>(StringComparer.Ordinal);
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (AudienceFilter.HeadingTokens(heading) is not { } tokens)
            {
                continue;
            }

            if (tokens.Contains(".closing") && ReferenceEquals(heading.Parent, document) && first.TryGetValue(Backup, out var backup))
            {
                throw new DocumentConversionException(
                    format,
                    $"The closing slide on line {lineOf(heading)} comes after the backup that starts on line {lineOf(backup)}. The closing slide goes before '{{.backup}}': backup slides stay behind it.");
            }

            foreach (var token in new[] { Backup, Appendix }.Where(tokens.Contains))
            {
                var what = token == Backup ? "the backup" : "the appendices";
                var line = lineOf(heading);
                if (heading.Level != 1)
                {
                    throw new DocumentConversionException(
                        format,
                        $"'{{{token}}}' on line {line} is on a '{new string('#', heading.Level)}' heading. It goes on the '#' heading that starts {what}; every later chapter belongs to it.");
                }

                if (!ReferenceEquals(heading.Parent, document))
                {
                    throw new DocumentConversionException(
                        format,
                        $"The heading with '{{{token}}}' on line {line} is inside another block. {Capital(what)} start at a '#' heading outside any container, list or quote.");
                }

                if (ReferenceEquals(heading, cover))
                {
                    throw new DocumentConversionException(
                        format,
                        $"'{{{token}}}' on line {line} is on the cover, the first '#' heading. It goes on a later '#' heading, the one that starts {what}.");
                }

                if (tokens.Contains(".closing"))
                {
                    throw new DocumentConversionException(
                        format,
                        $"The heading on line {line} has both '{{{token}}}' and '{{.closing}}'. The closing slide is one slide of its own; {what} start at another '#' heading.");
                }

                if (first.TryGetValue(token, out var earlier))
                {
                    throw new DocumentConversionException(
                        format,
                        $"'{{{token}}}' is on line {line} and already on line {lineOf(earlier)}. It goes once, on the '#' heading that starts {what}; every later chapter belongs to it.");
                }

                first[token] = heading;
                if ((token == Backup) != (format == DocumentFormat.Pptx))
                {
                    notes.Add(
                        NoteCodes.AttributeIgnored,
                        line,
                        token == Backup
                            ? $"The heading attribute '{{.backup}}' has no effect: a {(format == DocumentFormat.Docx ? "Word document" : "PDF")} has no backup slides, so the chapters after it stay where they are. Use '{{.appendix}}' for appendices."
                            : "The heading attribute '{.appendix}' has no effect: a deck has no appendices, so the chapters after it stay slides like any other. Use '{.backup}' for slides behind the closing slide.");
                }
            }
        }
    }

    /// <summary>The letters of the appendix number <paramref name="number"/>, from 1: <c>A</c> … <c>Z</c>, <c>AA</c>, <c>AB</c>.</summary>
    public static string Letter(int number)
    {
        var letters = string.Empty;
        for (var n = number; n > 0; n = (n - 1) / 26)
        {
            letters = (char)('A' + ((n - 1) % 26)) + letters;
        }

        return letters;
    }

    // The cover: in a deck its first '#'; in a document a '#' that is its first block (markers, speaker notes, footnotes
    // and link definitions left aside).
    private static HeadingBlock? Cover(ContainerBlock document, DocumentFormat format)
    {
        foreach (var block in document)
        {
            switch (block)
            {
                case HeadingBlock { Level: 1 } heading:
                    return heading;
                case FootnoteGroup or LinkReferenceDefinitionGroup:
                case CustomContainer { Info: LayoutSyntax.Notes }:
                case ParagraphBlock paragraph when BuildMarker.IsMarker(paragraph):
                    continue;
                default:
                    if (format != DocumentFormat.Pptx)
                    {
                        return null;
                    }

                    continue;
            }
        }

        return null;
    }

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
