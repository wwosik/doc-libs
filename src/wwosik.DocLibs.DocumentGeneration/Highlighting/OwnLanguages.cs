using ColorCode;
using ColorCode.Common;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The highlighter's own ColorCode language definitions: JSON (ColorCode's own string pattern can take exponential time
/// on an unclosed string), YAML and shell, which ColorCode lacks. Each rule is a regular expression without nested
/// quantifiers over the same characters, so a line takes linear time whatever it holds; ColorCode joins them into one
/// expression in which the earliest match wins. Their ids carry a prefix, so they never meet a language a host loads.
/// </summary>
internal static class OwnLanguages
{
    public const string JsonId = "doclibs-json";
    public const string YamlId = "doclibs-yaml";
    public const string BashId = "doclibs-bash";

    // A double-quoted string with backslash escapes, on one line.
    private const string Quoted = @"""(?:[^""\\\n]|\\.)*""";

    public static IReadOnlyList<ILanguage> All { get; } =
    [
        new Language(JsonId, "JSON",
        [
            Rule(@"//.*$", ScopeName.Comment),
            Rule(@"/\*[\s\S]*?\*/", ScopeName.Comment),
            new LanguageRule($@"({Quoted})(?=\s*:)", new Dictionary<int, string> { [1] = ScopeName.JsonKey }),
            Rule(Quoted, ScopeName.String),
            Rule(@"(?<![\w.])-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?(?![\w.])", ScopeName.Number),
            Rule(@"\b(?:true|false|null)\b", ScopeName.Keyword),
        ]),
        new Language(YamlId, "YAML",
        [
            Rule(@"(?:^|(?<=\s))#.*$", ScopeName.Comment),
            Rule(@"^(?:---|\.\.\.)(?=[ \t]*$)", ScopeName.Delimiter),
            new LanguageRule(
                @"^[ \t]*(?:-[ \t]+)?((?:[A-Za-z0-9_$][^:#\n]*?)|(?:""[^""\n]*"")|(?:'[^'\n]*'))[ \t]*:(?=[ \t]|$)",
                new Dictionary<int, string> { [1] = ScopeName.JsonKey }),
            Rule(Quoted, ScopeName.String),
            Rule(@"'(?:[^'\n]|'')*'", ScopeName.String),
            Rule(@"(?<![\w.-])[-+]?(?:0x[0-9A-Fa-f]+|[0-9]+(?:\.[0-9]+)?(?:[eE][-+]?[0-9]+)?)(?![\w.-])", ScopeName.Number),
            Rule(@"(?<![\w-])(?:true|false|null|True|False|Null|TRUE|FALSE|NULL|~)(?![\w-])", ScopeName.Keyword),
            Rule(@"(?<![\w])[&*][A-Za-z0-9_-]+|!![A-Za-z]+", ScopeName.Type),
            Rule(@"^[ \t]*-(?=[ \t]|$)", ScopeName.Delimiter),
        ]),
        new Language(BashId, "Bash",
        [
            Rule(@"(?:^|(?<=\s))#.*$", ScopeName.Comment),
            Rule(Quoted, ScopeName.String),
            Rule(@"'[^']*'", ScopeName.String),
            Rule(@"\$\{[^}\n]*\}|\$[A-Za-z_][A-Za-z0-9_]*|\$[0-9#?@*$!-]", ScopeName.Type),
            Rule(
                @"(?<![\w$.-])(?:if|then|else|elif|fi|for|while|until|do|done|case|esac|in|function|select|return|break|continue|"
                + @"local|export|readonly|declare|unset|shift|exit|source|alias|set|trap|echo|printf|cd|read|eval|exec|test)(?![\w.-])",
                ScopeName.Keyword),
            Rule(@"(?<![\w.$-])[0-9]+(?![\w.])", ScopeName.Number),
        ]),
    ];

    private static LanguageRule Rule(string pattern, string scope) => new(pattern, new Dictionary<int, string> { [0] = scope });

    private sealed class Language(string id, string name, IList<LanguageRule> rules) : ILanguage
    {
        public string Id => id;

        public string FirstLinePattern => null!;

        public string Name => name;

        public IList<LanguageRule> Rules => rules;

        public string CssClassName => id;

        public bool HasAlias(string lang) => false;
    }
}
