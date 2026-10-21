namespace wwosik.DocLibs.DocumentConversions.Jira;

/// <summary>
/// Converts between Markdown and JIRA wiki markup, the format the description field of JIRA's REST
/// API v2 accepts. What either direction does not know passes through as text.
/// </summary>
public static class JiraWikiMarkup
{
    /// <summary>
    /// Markdown to wiki markup. Soft line breaks inside a block are folded, so wrapped and unwrapped
    /// Markdown give the same result. Plain text that looks like JIRA markup is escaped.
    /// </summary>
    public static string FromMarkdown(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return MarkdownToWiki.ToWiki(markdown);
    }

    /// <summary>Wiki markup to Markdown. Returns an empty string for null or blank input.</summary>
    /// <param name="wiki">The wiki markup as JIRA stores it.</param>
    /// <param name="options">Heading level and attachment resolution; defaults if omitted.</param>
    public static string ToMarkdown(string? wiki, JiraMarkdownOptions? options = null) =>
        WikiToMarkdown.ToMarkdown(wiki, options?.TopLevel ?? 1, options?.ResolveAttachment);
}

/// <summary>Options for <see cref="JiraWikiMarkup.ToMarkdown"/>.</summary>
public sealed record JiraMarkdownOptions
{
    /// <summary>The Markdown heading level <c>h1.</c> becomes, so headings stay below their section. 1 to 6; default 1.</summary>
    public int TopLevel { get; init; } = 1;

    /// <summary>Resolves an attachment's file name to its address, for images and attachment links. Unresolved names stay as text.</summary>
    public Func<string, string?>? ResolveAttachment { get; init; }
}
