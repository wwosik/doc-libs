using System.Globalization;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// How confidential a document is, as front matter (<c>classification:</c>) and the host's options set it. The texts
/// shown for it are <see cref="GeneratedTexts.Internal"/> and <see cref="GeneratedTexts.Confidential"/>.
/// </summary>
public enum Classification
{
    /// <summary>Not marked: the default, which shows nothing.</summary>
    Public,

    /// <summary>For the organisation only: "Internal".</summary>
    Internal,

    /// <summary>For named readers only: "Confidential".</summary>
    Confidential,
}

/// <summary>
/// The classification marking of one document (E03-S04's keys): its <see cref="Classification"/> and whether it is a
/// draft. Front matter can raise the host's classification but not lower it (trying gives a
/// <see cref="NoteCodes.ClassificationKept"/> note), and can make a document a draft but not undo the host's draft.
/// </summary>
internal sealed record Marking(Classification Classification, bool Draft)
{
    /// <summary>No marking.</summary>
    public static Marking None { get; } = new(Classification.Public, false);

    /// <summary>Whether anything is shown: a classification above public, or a draft.</summary>
    public bool IsEmpty => this.Classification == Classification.Public && !this.Draft;

    /// <summary>
    /// The marking of a document: the higher of the host's classification and the front matter's, and a draft if
    /// either says so.
    /// </summary>
    /// <exception cref="DocumentConversionException">A value in the front matter is not one of the valid ones.</exception>
    public static Marking Resolve(FrontMatter frontMatter, Classification host, bool hostDraft, GenerationNotes notes, DocumentFormat format)
    {
        var classification = host;
        if (frontMatter.Value(FrontMatter.ClassificationKey) is { } value)
        {
            var asked = frontMatter.OneOf(
                FrontMatter.ClassificationKey,
                value,
                ("public", Classification.Public),
                ("internal", Classification.Internal),
                ("confidential", Classification.Confidential));
            if (asked < host)
            {
                notes.Add(
                    NoteCodes.ClassificationKept,
                    frontMatter.LineOf(FrontMatter.ClassificationKey),
                    $"The front matter asks for 'classification: {value.Trim()}', but the host marks the document '{Name(host)}', which is kept: front matter can raise the classification, not lower it.");
            }

            classification = asked > host ? asked : host;
        }

        var draft = hostDraft;
        if (frontMatter.Value(FrontMatter.DraftKey) is { } draftValue)
        {
            draft |= frontMatter.Bool(FrontMatter.DraftKey, draftValue);
        }

        return new Marking(classification, draft);
    }

    /// <summary>The texts of the marking joined with " · ": "Confidential · Draft"; empty for <see cref="None"/>.</summary>
    public string Text(GeneratedTexts texts) => string.Join(
        " · ",
        new[]
        {
            this.Classification switch
            {
                Classification.Internal => texts.Internal,
                Classification.Confidential => texts.Confidential,
                _ => null,
            },
            this.Draft ? texts.Draft : null,
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>
    /// The text of a draft's watermark: <see cref="GeneratedTexts.Draft"/> in capitals, upper-cased in the culture of
    /// <paramref name="language"/> (the invariant culture for a tag .NET does not know).
    /// </summary>
    public static string Watermark(GeneratedTexts texts, string language)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        return texts.Draft.ToUpper(culture);
    }

    private static string Name(Classification classification) => classification.ToString().ToLowerInvariant();
}
