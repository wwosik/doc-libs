namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>What a piece of highlighted code is: the kinds <see cref="BrandCodeColors"/> gives colours to.</summary>
public enum CodeTokenKind
{
    /// <summary>Everything else: names, operators' surroundings, white space. In the text colour.</summary>
    Plain,

    /// <summary>Keywords, built-in functions and constants such as <c>true</c>.</summary>
    Keyword,

    /// <summary>String and character literals.</summary>
    String,

    /// <summary>Comments.</summary>
    Comment,

    /// <summary>Number literals.</summary>
    Number,

    /// <summary>Type, class and namespace names, attributes, shell variables.</summary>
    Type,

    /// <summary>Operators, brackets and delimiters. Drawn like <see cref="Plain"/>.</summary>
    Punctuation,

    /// <summary>Tags and attribute names in markup, keys in JSON and YAML.</summary>
    Markup,
}

/// <summary>A run of code of one kind, by position in the code the highlighter was given.</summary>
internal readonly record struct CodeToken(int Start, int Length, CodeTokenKind Kind);

/// <summary>A piece of one line of code and how it is drawn; <see cref="CodeTokenKind.Punctuation"/> never occurs here.</summary>
internal readonly record struct CodeSpan(string Text, CodeTokenKind Kind);

/// <summary>A language the highlighter knows: the name the guides list, its aliases, and the id of its definition.</summary>
internal sealed record CodeLanguage(string Name, IReadOnlyList<string> Aliases, string Id);

/// <summary>The languages fenced code blocks can be highlighted in, by the name after the fence (any case).</summary>
internal static class CodeLanguages
{
    /// <summary>Languages written after the fence on purpose for plain text: never highlighted, never a note.</summary>
    public static readonly IReadOnlySet<string> PlainNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "plain", "console", "output" };

    public static IReadOnlyList<CodeLanguage> All { get; } =
    [
        new("csharp", ["cs", "c#"], "c#"),
        new("vb", ["vbnet", "vb.net"], "vb.net"),
        new("fsharp", ["fs", "f#"], "f#"),
        new("cpp", ["c", "c++"], "cpp"),
        new("java", [], "java"),
        new("javascript", ["js"], "javascript"),
        new("typescript", ["ts"], "typescript"),
        new("php", [], "php"),
        new("python", ["py"], "python"),
        new("powershell", ["ps1", "pwsh"], "powershell"),
        new("sql", [], "sql"),
        new("html", ["htm"], "html"),
        new("xml", ["xaml"], "xml"),
        new("css", [], "css"),
        new("markdown", ["md"], "markdown"),
        new("haskell", ["hs"], "haskell"),
        new("json", [], OwnLanguages.JsonId),
        new("yaml", ["yml"], OwnLanguages.YamlId),
        new("bash", ["sh", "shell", "zsh"], OwnLanguages.BashId),
    ];

    private static readonly Dictionary<string, CodeLanguage> ByName = All
        .SelectMany(l => l.Aliases.Prepend(l.Name).Select(name => (name, l)))
        .ToDictionary(p => p.name, p => p.l, StringComparer.OrdinalIgnoreCase);

    /// <summary>The language <paramref name="name"/> names, or <see langword="null"/>.</summary>
    public static CodeLanguage? Find(string name) => ByName.GetValueOrDefault(name.Trim());

    /// <summary>The names for the guides: <c>`csharp`, `vb`, …</c>; the aliases are in the docs only.</summary>
    public static string List() => string.Join(", ", All.Select(l => $"`{l.Name}`"));
}

/// <summary>
/// Code highlighting for one document, or <see langword="null"/> when it is off (<see cref="For"/>): the front-matter
/// key <c>highlight</c> over the generator's <c>HighlightCode</c> option. It turns a fenced block's code into lines of
/// <see cref="CodeSpan"/>s; a block it does not highlight is left to the generator's plain path, so documents with
/// highlighting off come out exactly as before.
/// </summary>
internal sealed class CodeHighlighting(GenerationNotes notes)
{
    /// <summary>The front-matter key: <c>true</c> or <c>false</c>, over the option.</summary>
    public const string Key = "highlight";

    public static bool IsOn(FrontMatter frontMatter, bool option) =>
        frontMatter.Value(Key) is { } value ? frontMatter.Bool(Key, value) : option;

    public static CodeHighlighting? For(FrontMatter frontMatter, bool option, GenerationNotes notes) =>
        IsOn(frontMatter, option) ? new CodeHighlighting(notes) : null;

    /// <summary>
    /// The lines of <paramref name="code"/> as highlighted spans, or <see langword="null"/> when the block stays plain:
    /// no language, a plain one (<see cref="CodeLanguages.PlainNames"/>), or one the highlighter does not know — which
    /// adds <see cref="NoteCodes.CodeLanguageUnknown"/>, unless a renderer already reported on the block's line.
    /// Lines are split at <c>\n</c>, as the generators split code; empty spans are left out.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<CodeSpan>>? Lines(string? language, string code, int line)
    {
        if (string.IsNullOrWhiteSpace(language) || CodeLanguages.PlainNames.Contains(language.Trim()))
        {
            return null;
        }

        if (CodeHighlighter.Highlight(language, code) is not { } tokens)
        {
            if (!notes.Has(NoteCodes.CodeNotDrawn, line))
            {
                notes.Add(
                    NoteCodes.CodeLanguageUnknown,
                    line,
                    $"The code block's language '{language.Trim()}' is not one the highlighter knows, so the block is not highlighted.");
            }

            return null;
        }

        var lines = new List<IReadOnlyList<CodeSpan>>();
        var current = new List<CodeSpan>();
        foreach (var token in tokens)
        {
            var kind = token.Kind == CodeTokenKind.Punctuation ? CodeTokenKind.Plain : token.Kind;
            var parts = code.Substring(token.Start, token.Length).Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    lines.Add(current);
                    current = [];
                }

                if (parts[i].Length == 0)
                {
                    continue;
                }

                if (current.Count > 0 && current[^1].Kind == kind)
                {
                    current[^1] = current[^1] with { Text = current[^1].Text + parts[i] };
                }
                else
                {
                    current.Add(new CodeSpan(parts[i], kind));
                }
            }
        }

        lines.Add(current);
        return lines;
    }
}
