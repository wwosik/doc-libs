using ColorCode;
using ColorCode.Common;
using ColorCode.Compilation;
using ColorCode.Parsing;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The built-in highlighter: ColorCode.Core's languages and the own definitions of <c>json</c>, <c>yaml</c> and
/// <c>bash</c> (<see cref="OwnLanguages"/>), with ColorCode's scope names mapped to <see cref="CodeTokenKind"/>. Its
/// languages are compiled on the first block highlighted, into a cache of its own: ColorCode's global registry is
/// neither read for compiled languages nor changed, so a host that uses ColorCode itself is not affected.
/// </summary>
internal static class CodeHighlighter
{
    /// <summary>
    /// The tokens of <paramref name="code"/> in <paramref name="language"/> (a name or alias of
    /// <see cref="CodeLanguages"/>, any case), covering it from start to end, neighbours of the same kind merged;
    /// <see langword="null"/> for a language it does not know.
    /// </summary>
    public static IReadOnlyList<CodeToken>? Highlight(string language, string code)
    {
        if (CodeLanguages.Find(language) is not { } known)
        {
            return null;
        }

        var kinds = new CodeTokenKind[code.Length];
        if (code.Length > 0)
        {
            var offset = 0;
            Engine.Parser.Parse(code, Engine.Language(known.Id), (fragment, scopes) =>
            {
                foreach (var scope in scopes)
                {
                    Paint(kinds, offset, scope);
                }

                offset += fragment.Length;
            });
        }

        var tokens = new List<CodeToken>();
        for (var start = 0; start < kinds.Length;)
        {
            var end = start + 1;
            while (end < kinds.Length && kinds[end] == kinds[start])
            {
                end++;
            }

            tokens.Add(new CodeToken(start, end - start, kinds[start]));
            start = end;
        }

        return tokens;
    }

    /// <summary>What a ColorCode scope is drawn as; anything not named here is plain.</summary>
    internal static CodeTokenKind KindOf(string scope) => scope switch
    {
        ScopeName.Keyword or ScopeName.ControlKeyword or ScopeName.PreprocessorKeyword or ScopeName.PseudoKeyword
            or ScopeName.SqlSystemFunction or ScopeName.BuiltinFunction or ScopeName.BuiltinValue or ScopeName.Predefined
            or ScopeName.Intrinsic or ScopeName.PowerShellCommand or ScopeName.JsonConst => CodeTokenKind.Keyword,
        ScopeName.String or ScopeName.StringCSharpVerbatim or ScopeName.StringEscape or ScopeName.JsonString
            or ScopeName.HtmlAttributeValue or ScopeName.XmlAttributeValue or ScopeName.XmlAttributeQuotes
            or ScopeName.XmlCDataSection or ScopeName.CssPropertyValue or ScopeName.MarkdownCode => CodeTokenKind.String,
        ScopeName.Comment or ScopeName.HtmlComment or ScopeName.XmlComment or ScopeName.XmlDocComment or ScopeName.XmlDocTag => CodeTokenKind.Comment,
        ScopeName.Number or ScopeName.JsonNumber => CodeTokenKind.Number,
        ScopeName.ClassName or ScopeName.Type or ScopeName.TypeVariable or ScopeName.NameSpace or ScopeName.Constructor
            or ScopeName.PowerShellType or ScopeName.Attribute or ScopeName.PowerShellAttribute or ScopeName.PowerShellVariable => CodeTokenKind.Type,
        ScopeName.HtmlElementName or ScopeName.HtmlAttributeName or ScopeName.XmlName or ScopeName.XmlAttribute
            or ScopeName.CssSelector or ScopeName.CssPropertyName or ScopeName.JsonKey or ScopeName.MarkdownHeader
            or ScopeName.PowerShellParameter => CodeTokenKind.Markup,
        ScopeName.Operator or ScopeName.Delimiter or ScopeName.Brackets or ScopeName.HtmlOperator or ScopeName.HtmlTagDelimiter
            or ScopeName.XmlDelimiter or ScopeName.HtmlEntity or ScopeName.SpecialCharacter or ScopeName.Continuation
            or ScopeName.MarkdownListItem or ScopeName.PowerShellOperator => CodeTokenKind.Punctuation,
        _ => CodeTokenKind.Plain,
    };

    // A scope and, over it, its children: the innermost scope decides.
    private static void Paint(CodeTokenKind[] kinds, int offset, Scope scope)
    {
        var kind = KindOf(scope.Name);
        var start = Math.Max(0, offset + scope.Index);
        var end = Math.Min(kinds.Length, offset + scope.Index + scope.Length);
        if (kind != CodeTokenKind.Plain || scope.Children.Count == 0)
        {
            Array.Fill(kinds, kind, start, Math.Max(0, end - start));
        }

        foreach (var child in scope.Children)
        {
            Paint(kinds, offset, child);
        }
    }

    // Built on first use, so that a document without highlighting loads and compiles nothing.
    private static class Engine
    {
        private static readonly Repository Languages = new();

        public static LanguageParser Parser { get; } = new(
            new LanguageCompiler(new Dictionary<string, CompiledLanguage>(StringComparer.Ordinal), new ReaderWriterLockSlim()),
            Languages);

        public static ILanguage Language(string id) =>
            Languages.FindById(id) ?? throw new InvalidOperationException($"The highlighter has no language '{id}'.");
    }

    // ColorCode's own languages, which nested languages (`&c#` in ASPX-like rules) are looked up in, plus ours.
    private sealed class Repository : ILanguageRepository
    {
        private readonly Dictionary<string, ILanguage> own = OwnLanguages.All.ToDictionary(l => l.Id, StringComparer.Ordinal);

        public IEnumerable<ILanguage> All => ColorCode.Languages.All.Concat(this.own.Values);

        public ILanguage? FindById(string languageId) =>
            this.own.TryGetValue(languageId, out var language) ? language : ColorCode.Languages.FindById(languageId);

        public void Load(ILanguage language) => throw new NotSupportedException();
    }
}
