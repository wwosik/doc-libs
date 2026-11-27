using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentGeneration.Charts;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using MarkdownDocument = wwosik.DocLibs.DocumentConversions.MarkdownDocument;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// Reads a workbook from Markdown: a leading <c>#</c> is the title and its paragraphs the description, each <c>##</c> a
/// sheet whose first table is the data, a <c>chart</c> block a chart; header cells carry attributes
/// (<c>{.currency total=sum}</c>), and columns without a type take it from their values. What the Markdown gets wrong is
/// collected as problems with their lines; only a malformed front matter, an unknown brand or language throw.
/// </summary>
internal static partial class XlsxMarkdown
{
    public const string InfoKey = "info";
    public const string ShowDateKey = "showDate";
    public const string SubtitleKey = "subtitle";
    public const string DateKey = "date";

    /// <summary>The front matter keys of workbooks, besides the ones every generator shares.</summary>
    public static readonly IReadOnlyList<string> Keys = [InfoKey, ShowDateKey, SubtitleKey, DateKey];

    /// <summary>Keys of decks and documents, accepted so that one Markdown serves every format, and ignored.</summary>
    public static readonly IReadOnlyList<string> OtherFormatKeys = ["showTagline", "embedFonts", "showFooter", "transition", "build", "numberChapters", "listBullets", "tone"];

    /// <summary>The type attributes of a header cell, by class name.</summary>
    public static readonly IReadOnlyDictionary<string, XlsxColumnType> Types = new Dictionary<string, XlsxColumnType>(StringComparer.Ordinal)
    {
        ["text"] = XlsxColumnType.Text,
        ["number"] = XlsxColumnType.Number,
        ["integer"] = XlsxColumnType.Integer,
        ["currency"] = XlsxColumnType.Currency,
        ["percent"] = XlsxColumnType.Percent,
        ["date"] = XlsxColumnType.Date,
        ["datetime"] = XlsxColumnType.DateTime,
        ["boolean"] = XlsxColumnType.Boolean,
    };

    /// <summary>The settings of a header cell.</summary>
    public static readonly IReadOnlyList<string> Settings = ["decimals", "total", "width"];

    /// <summary>The totals <c>total=</c> takes.</summary>
    public static readonly IReadOnlyDictionary<string, XlsxTotal> Totals = new Dictionary<string, XlsxTotal>(StringComparer.OrdinalIgnoreCase)
    {
        ["sum"] = XlsxTotal.Sum,
        ["average"] = XlsxTotal.Average,
        ["count"] = XlsxTotal.Count,
        ["min"] = XlsxTotal.Min,
        ["max"] = XlsxTotal.Max,
    };

    /// <summary>The settings of a <c>chart</c> block in a workbook.</summary>
    public static readonly IReadOnlyList<string> ChartSettings = ["type", "title", "unit", "stacked", "source", "decimal", "category", "series", "placement", "sheet"];

    /// <summary>The kinds <c>type:</c> takes in a workbook's <c>chart</c> block: <c>bar</c> is upright, as in the other formats.</summary>
    public static readonly IReadOnlyDictionary<string, XlsxChartKind> ChartTypes = new Dictionary<string, XlsxChartKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["bar"] = XlsxChartKind.Column,
        ["horizontal-bar"] = XlsxChartKind.Bar,
        ["line"] = XlsxChartKind.Line,
        ["area"] = XlsxChartKind.Area,
    };

    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    /// <summary>The workbook <paramref name="document"/> describes, the options with its front matter applied, the problems and notes found.</summary>
    /// <exception cref="DocumentConversionException">The front matter is malformed, names an unknown brand or key, or the language is not a language tag.</exception>
    /// <exception cref="ArgumentException">A brand is invalid, or two share a name.</exception>
    public static Result Read(MarkdownDocument document, XlsxOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var frontMatter = FrontMatter.Read(document.Markdown, Format.Xlsx);
        var registry = BrandRegistry.Of(options.Brand, options.Brands);
        string? brandName = null;
        options = frontMatter.Apply(
            options,
            (current, name) =>
            {
                if (registry.Find(name.Trim()) is not { } found)
                {
                    return null;
                }

                brandName = found.Name;
                return current;
            },
            registry.Expected,
            _ => brandName ?? (options.Brand ?? Brand.Neutral).Name,
            _ => [.. Keys, .. OtherFormatKeys],
            (current, key, value) => key switch
            {
                InfoKey => current with { Info = frontMatter.Bool(key, value) },
                ShowDateKey => current with { ShowDate = frontMatter.Bool(key, value) },
                "showTagline" or "embedFonts" or "showFooter" or "numberChapters" => frontMatter.Bool(key, value) ? current : current,
                _ => current,
            });

        var notes = new GenerationNotes();
        var language = DocumentLanguage.Resolve(frontMatter.Language, "en", options.Texts, new GenerationNotes(), Format.Xlsx);
        var culture = options.Culture ?? XlsxFormat.Culture(language.Tag);
        var audience = AudienceFilter.Resolve(frontMatter, options.Audience);
        var marking = Marking.Resolve(frontMatter, options.Marking?.Classification ?? Classification.Public, options.Marking?.Draft ?? false, notes, Format.Xlsx, audience);
        DateOnly? date = null;
        if (frontMatter.Value(DateKey) is { } dateText)
        {
            date = DateOnly.TryParseExact(dateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new DocumentConversionException(Format.Xlsx, $"The front matter key 'date' has the value '{dateText}'; expected a date such as '2026-03-09'.");
        }

        var reader = new Reader(frontMatter.Body, frontMatter.BodyLine, new XlsxValues(culture, language.Tag), culture, language.Texts, notes);
        var markdown = Markdig.Markdown.Parse(frontMatter.Body, Pipeline);
        AudienceFilter.Apply(markdown, audience, Format.Xlsx, reader.LineOf, language.Texts, notes);
        reader.Walk(markdown);

        var workbook = new XlsxWorkbook
        {
            Title = frontMatter.Title ?? reader.Title ?? document.Metadata.GetValueOrDefault(MetadataKeys.Title),
            Subtitle = frontMatter.Value(SubtitleKey),
            Description = reader.Description ?? frontMatter.Description,
            Author = frontMatter.Author ?? document.Metadata.GetValueOrDefault(MetadataKeys.Author),
            Date = date,
            Language = language.Tag,
            Marking = marking.Classification == Classification.Public && !marking.Draft ? null : new XlsxMarking(marking.Classification, marking.Draft),
            Brand = brandName,
            Sheets = reader.Sheets,
        };
        return new Result(workbook, options, reader.Problems, notes);
    }

    private static MarkdownPipeline BuildPipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough | Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Marked)
            .UseAlertBlocks()
            .UseDocumentSyntax();
        builder.BlockParsers.AddIfNotAlready<CustomContainerParser>();
        return builder.Build();
    }

    // The trailing `{…}` of a header cell: classes, `.currency=EUR`, and `key=value` or `key="value"`.
    [GeneratedRegex(@"\s*\{((?:\s*(?:\.[A-Za-z][\w-]*(?:=(?:""[^""]*""|[^\s""}]+))?|[A-Za-z][\w-]*=(?:""[^""]*""|[^\s""}]+)))+)\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributesPattern();

    [GeneratedRegex(@"(?<dot>\.)?(?<name>[A-Za-z][\w-]*)(?:=(?:""(?<value>[^""]*)""|(?<value>[^\s""}]+)))?", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>What <see cref="Read"/> found.</summary>
    internal sealed record Result(XlsxWorkbook Workbook, XlsxOptions Options, IReadOnlyList<GenerationNote> Problems, GenerationNotes Notes);

    // One sheet being read: its name, the line it starts at, its description, its table and charts.
    private sealed class Part(string name, int line)
    {
        public string Name { get; } = name;

        public int Line { get; } = line;

        public List<string> Description { get; } = [];

        public string? Title { get; set; }

        public List<XlsxColumn>? Columns { get; set; }

        public List<IReadOnlyList<XlsxCell>> Rows { get; } = [];

        public List<XlsxChart> Charts { get; } = [];

        public XlsxSheet Sheet() => new()
        {
            Name = this.Name,
            Title = this.Title,
            Description = this.Description.Count == 0 ? null : string.Join("\n", this.Description),
            Columns = this.Columns ?? [],
            Rows = this.Rows,
            Charts = this.Charts,
        };
    }

    private sealed class Reader(string source, int bodyLine, XlsxValues values, CultureInfo culture, GeneratedTexts texts, GenerationNotes notes)
    {
        private readonly List<GenerationNote> problems = [];
        private readonly List<Part> parts = [];
        private readonly List<(string Text, int Line)> pending = [];

        // The section being read: the `##` heading's name (or the generated "Table" before the first), its parts so far.
        private string section = texts.Table;
        private int sectionLine;
        private int sectionParts;
        private Part? current;

        public string? Title { get; private set; }

        public string? Description { get; private set; }

        public IReadOnlyList<GenerationNote> Problems => this.problems;

        public IReadOnlyList<XlsxSheet> Sheets => [.. this.parts.Select(p => p.Sheet())];

        public int LineOf(MarkdownObject block) => block.Line + 1 + bodyLine;

        public void Walk(Markdig.Syntax.MarkdownDocument markdown)
        {
            var blocks = markdown.Where(b => b is not (FootnoteGroup or LinkReferenceDefinitionGroup)).ToList();
            var start = 0;
            if (blocks.FirstOrDefault() is HeadingBlock { Level: 1 } title)
            {
                this.Title = HeadingText(title);
                var description = new List<string>();
                for (start = 1; start < blocks.Count && blocks[start] is ParagraphBlock paragraph && !IsImages(paragraph) && Captions.Kind(paragraph) is null; start++)
                {
                    description.Add(Text(paragraph.Inline));
                }

                this.Description = description.Count == 0 ? null : string.Join("\n", description);
            }

            for (var i = start; i < blocks.Count; i++)
            {
                var block = blocks[i];
                switch (block)
                {
                    case HeadingBlock { Level: 2 } heading:
                        this.EndSection();
                        this.section = HeadingText(heading);
                        this.sectionLine = this.LineOf(heading);
                        this.sectionParts = 0;
                        this.current = null;
                        break;
                    case Table table:
                        this.Table(table);
                        if (i + 1 < blocks.Count && blocks[i + 1] is ParagraphBlock caption && Captions.Kind(caption) == CaptionKind.Table)
                        {
                            Captions.TakePrefix(caption, CaptionKind.Table);
                            this.current!.Title = Text(caption.Inline);
                            i++;
                        }

                        break;
                    case ParagraphBlock paragraph when Captions.Kind(paragraph) is { } kind:
                        notes.Add(NoteCodes.CaptionIgnored, this.LineOf(paragraph), Captions.Ignored(kind));
                        break;
                    case ParagraphBlock paragraph when !IsImages(paragraph):
                        this.pending.Add((Text(paragraph.Inline), this.LineOf(paragraph)));
                        break;
                    case FencedCodeBlock code when string.Equals(code.Info?.Trim(), "chart", StringComparison.OrdinalIgnoreCase):
                        this.Chart(code);
                        break;
                    default:
                        this.Ignored(block);
                        break;
                }
            }

            this.EndSection();
        }

        // A `##` without a table is no sheet; paragraphs after the last table belong to none.
        private void EndSection()
        {
            if (this.sectionParts == 0 && this.sectionLine > 0)
            {
                notes.Add(NoteCodes.ContentIgnored, this.sectionLine, $"The section '## {this.section}' has no table, so it is no sheet and its text is left out. Add a table under it, or remove it.");
            }
            else
            {
                foreach (var (text, line) in this.pending)
                {
                    notes.Add(NoteCodes.ContentIgnored, line, $"The paragraph '{GenerationNotes.Quote(text)}' follows the last table of its section, so it belongs to no sheet and is left out. Put it above the table, where it is the sheet's description.");
                }
            }

            this.pending.Clear();
        }

        private void Ignored(Block block)
        {
            var what = block switch
            {
                HeadingBlock { Level: 1 } => "A '#' heading after the first block",
                HeadingBlock heading => $"The '{new string('#', heading.Level)}' heading '{GenerationNotes.Quote(HeadingText(heading))}'",
                ListBlock => "A list",
                QuoteBlock => "A quote or alert",
                FencedCodeBlock { Info: { Length: > 0 } info } => $"A '{info.Trim()}' code block",
                CodeBlock => "A code block",
                ParagraphBlock => "An image",
                ThematicBreakBlock => "A rule",
                HtmlBlock => "HTML",
                CustomContainer custom => $"A '::: {custom.Info}' container",
                _ => "A block",
            };
            notes.Add(NoteCodes.ContentIgnored, this.LineOf(block), $"{what} is left out: a workbook holds tables, charts and the paragraphs that describe them.");
        }

        // A part for a table under the current section: its first, or "Name 2" and so on.
        private Part NewPart(int line)
        {
            this.sectionParts++;
            var name = this.sectionParts == 1 ? this.section : string.Create(CultureInfo.InvariantCulture, $"{this.section} {this.sectionParts}");
            if (this.sectionParts > 1)
            {
                notes.Add(NoteCodes.SheetSplit, line, $"A further table under '## {this.section}' goes to the sheet '{name}': a sheet holds one table. Give it a '##' heading of its own to name the sheet.");
            }

            var part = new Part(name, line);
            part.Description.AddRange(this.pending.Select(p => p.Text));
            this.pending.Clear();
            this.parts.Add(part);
            this.current = part;
            return part;
        }

        private void Table(Table table)
        {
            var part = this.NewPart(this.LineOf(table));
            var rows = table.OfType<TableRow>().ToList();
            var header = rows.FirstOrDefault(r => r.IsHeader) ?? rows.FirstOrDefault();
            if (header is null)
            {
                part.Columns = [];
                return;
            }

            var columns = new List<Declared>();
            for (var c = 0; c < header.Count; c++)
            {
                var align = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
                columns.Add(this.Header(part.Name, (TableCell)header[c], align));
            }

            var raw = rows.Where(r => r != header).Select(r => (Line: this.LineOf(r), Cells: r.OfType<TableCell>().Select(this.CellText).ToList())).ToList();
            var built = new List<XlsxColumn>();
            var cells = raw.Select(r => new XlsxCell[r.Cells.Count]).ToList();
            for (var c = 0; c < columns.Count; c++)
            {
                built.Add(this.Column(part.Name, columns[c], c, raw, cells));
            }

            // Cells past the header's columns stay text; the plan reports the row as too wide.
            for (var r = 0; r < raw.Count; r++)
            {
                for (var c = columns.Count; c < raw[r].Cells.Count; c++)
                {
                    cells[r][c] = Cell(raw[r].Cells[c]) ?? XlsxCell.FromText(raw[r].Cells[c].Text);
                }
            }

            part.Columns = built;
            part.Rows.AddRange(cells);
        }

        // A cell's text: its inline Markdown reduced to text; a formula as written in the source.
        private (string Text, bool Formula) CellText(TableCell cell)
        {
            var paragraph = cell.OfType<ParagraphBlock>().FirstOrDefault();
            var text = Text(paragraph?.Inline).Trim();
            if (!text.StartsWith('=') || paragraph is null)
            {
                return (text, false);
            }

            var span = paragraph.Span;
            var written = span.Start >= 0 && span.End < source.Length ? source[span.Start..(span.End + 1)] : text;
            return (written.Replace("\\|", "|", StringComparison.Ordinal).Trim(), true);
        }

        private static XlsxCell? Cell((string Text, bool Formula) cell) =>
            cell.Text.Length == 0 ? XlsxCell.Empty
            : cell.Formula && cell.Text.TrimStart('=').Trim().Length > 0 ? XlsxCell.Formula(cell.Text)
            : null;

        // A header cell: its text and attributes.
        private Declared Header(string sheet, TableCell cell, TableColumnAlign? align)
        {
            var line = this.LineOf(cell);
            var text = Text(cell.OfType<ParagraphBlock>().FirstOrDefault()?.Inline).Trim();
            var declared = new Declared { Align = align switch { TableColumnAlign.Left => XlsxAlign.Left, TableColumnAlign.Center => XlsxAlign.Center, TableColumnAlign.Right => XlsxAlign.Right, _ => null }, Line = line };
            var match = AttributesPattern().Match(text);
            if (!match.Success)
            {
                declared.Header = text;
                return declared;
            }

            declared.Header = text[..match.Index].Trim();
            var where = $"Sheet '{sheet}', column '{declared.Header}'";
            foreach (Match token in TokenPattern().Matches(match.Groups[1].Value))
            {
                var name = token.Groups["name"].Value;
                var value = token.Groups["value"].Success ? token.Groups["value"].Value.Trim() : null;
                if (token.Groups["dot"].Success)
                {
                    if (!Types.TryGetValue(name, out var type))
                    {
                        this.Unknown(where, "." + name, line);
                        continue;
                    }

                    if (declared.Type is not null)
                    {
                        this.Problem(NoteCodes.ColumnInvalid, line, $"{where}: the attributes name two types, .{declared.TypeName} and .{name}. A column has one type.");
                        continue;
                    }

                    if (value is not null && type != XlsxColumnType.Currency)
                    {
                        this.Problem(NoteCodes.ColumnInvalid, line, $"{where}: '.{name}={value}' gives a value to a type that takes none; only '.currency=EUR' names a currency.");
                    }

                    declared.Type = type;
                    declared.TypeName = name;
                    declared.Currency = value?.ToUpperInvariant();
                    continue;
                }

                if (value is null || !Settings.Contains(name))
                {
                    this.Unknown(where, name, line);
                    continue;
                }

                switch (name)
                {
                    case "decimals":
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var decimals))
                        {
                            declared.Decimals = decimals;
                        }
                        else
                        {
                            this.Problem(NoteCodes.ColumnInvalid, line, $"{where}: decimals={value} is not a whole number from 0 to 10.");
                        }

                        break;
                    case "total":
                        if (Totals.TryGetValue(value, out var total))
                        {
                            declared.Total = total;
                        }
                        else
                        {
                            this.Problem(NoteCodes.ColumnInvalid, line, $"{where}: total={value} is not one of {string.Join(", ", Totals.Keys)}.");
                        }

                        break;
                    default:
                        if (double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var width))
                        {
                            declared.Width = width;
                        }
                        else
                        {
                            this.Problem(NoteCodes.ColumnInvalid, line, $"{where}: width={value} is not a number of characters, such as width=20.");
                        }

                        break;
                }
            }

            return declared;
        }

        private void Unknown(string where, string name, int line)
        {
            var known = Types.Keys.Select(t => "." + t).Concat(Settings).ToList();
            var close = XlsxFormula.Closest(name, known);
            var hint = close.Count > 0 ? $" Did you mean '{string.Join("' or '", close)}'?" : string.Empty;
            this.Problem(NoteCodes.ColumnInvalid, line, $"{where}: the attribute '{name}' is unknown.{hint} Valid attributes: {string.Join(", ", Types.Keys.Select(t => "." + t))}, .currency=EUR, decimals=N, total=sum|average|count|min|max, width=N.");
        }

        // A column: declared, or inferred from its values; its cells filled into `cells`.
        private XlsxColumn Column(string sheet, Declared declared, int index, List<(int Line, List<(string Text, bool Formula)> Cells)> raw, List<XlsxCell[]> cells)
        {
            var where = $"Sheet '{sheet}', column '{declared.Header}'";
            var texts = new List<(int Row, string Text)>();
            for (var r = 0; r < raw.Count; r++)
            {
                if (index >= raw[r].Cells.Count)
                {
                    continue;
                }

                if (Cell(raw[r].Cells[index]) is { } fixedCell)
                {
                    cells[r][index] = fixedCell;
                }
                else
                {
                    texts.Add((r, raw[r].Cells[index].Text));
                }
            }

            var type = declared.Type;
            var currency = declared.Currency;
            var keepsText = false;
            if (type is null && texts.Count > 0)
            {
                (type, currency) = this.Infer(texts.Select(t => t.Text).ToList());
            }

            var decimals = declared.Decimals;
            var mismatched = new List<(int Row, string Text)>();
            foreach (var (row, text) in texts)
            {
                if (values.As(text, type ?? XlsxColumnType.Text, currency ?? (type == XlsxColumnType.Currency ? values.CultureCurrency : null)) is { } cell)
                {
                    cells[row][index] = cell;
                }
                else
                {
                    cells[row][index] = XlsxCell.FromText(text);
                    mismatched.Add((row, text));
                }
            }

            if (decimals is null && type is XlsxColumnType.Number or XlsxColumnType.Percent && texts.Count > mismatched.Count)
            {
                decimals = Math.Min(10, texts.Except(mismatched).Select(t => values.Parse(t.Text).Decimals).DefaultIfEmpty(0).Max());
            }

            if (mismatched.Count > 0)
            {
                keepsText = true;
                var (row, text) = mismatched[0];
                if (declared.Type is null)
                {
                    notes.Add(
                        NoteCodes.ColumnMixed,
                        raw[row].Line,
                        string.Create(CultureInfo.InvariantCulture, $"{where}: {texts.Count - mismatched.Count} of {texts.Count} values are {type}, so the column is; {mismatched.Count} {(mismatched.Count == 1 ? "stays" : "stay")} as written, such as '{GenerationNotes.Quote(text)}' on line {raw[row].Line}. Write them as {type} values in {culture.Name}'s way, or give the column a type such as {{.text}}."));
                }
                else
                {
                    foreach (var (r, t) in mismatched)
                    {
                        this.Problem(NoteCodes.CellType, raw[r].Line, $"{where}: '{GenerationNotes.Quote(t)}' is not a {type} value as {culture.Name} writes it. Fix the value, or change the column's type.");
                    }
                }
            }

            return new XlsxColumn(declared.Header, type)
            {
                Total = declared.Total,
                Decimals = decimals,
                Currency = type == XlsxColumnType.Currency ? currency : declared.Currency,
                Width = declared.Width,
                Align = declared.Align,
                KeepsText = keepsText,
            };
        }

        // The type at least two thirds of the values share, or Text; a currency column takes its values' code.
        private (XlsxColumnType? Type, string? Currency) Infer(List<string> texts)
        {
            var parsed = texts.Select(values.Parse).ToList();
            var candidates = new List<(XlsxColumnType Type, string? Currency)>
            {
                (XlsxColumnType.Boolean, null), (XlsxColumnType.Date, null), (XlsxColumnType.DateTime, null), (XlsxColumnType.Percent, null),
            };
            candidates.AddRange(parsed.Where(p => p.Kind == ValueKind.Currency).Select(p => p.Currency).Distinct().Order(StringComparer.Ordinal).Select(c => (XlsxColumnType.Currency, c)));
            candidates.Add((XlsxColumnType.Number, null));
            bool Fits(ParsedValue value, XlsxColumnType type, string? currency) => (type, value.Kind) switch
            {
                (XlsxColumnType.Boolean, ValueKind.Boolean) => true,
                (XlsxColumnType.Date, ValueKind.Date) => true,
                (XlsxColumnType.Date, ValueKind.DateTime) => value.DateTime.TimeOfDay == TimeSpan.Zero,
                (XlsxColumnType.DateTime, ValueKind.Date or ValueKind.DateTime) => true,
                (XlsxColumnType.Percent, ValueKind.Percent) => true,
                (XlsxColumnType.Currency, ValueKind.Number) => true,
                (XlsxColumnType.Currency, ValueKind.Currency) => value.Currency == currency,
                (XlsxColumnType.Number, ValueKind.Number) => true,
                _ => false,
            };

            // DateTime needs a time somewhere, Currency an amount with its code; else the narrower type wins ties.
            var best = candidates
                .Where(c => c.Type != XlsxColumnType.DateTime || parsed.Any(p => p.Kind == ValueKind.DateTime))
                .Select(c => (c.Type, c.Currency, Count: parsed.Count(p => Fits(p, c.Type, c.Currency))))
                .OrderByDescending(c => c.Count)
                .First();
            if (best.Count == 0 || best.Count * 3 < texts.Count * 2)
            {
                return (XlsxColumnType.Text, null);
            }

            if (best.Type == XlsxColumnType.Number && parsed.Where(p => p.Kind == ValueKind.Number).All(p => p.Whole))
            {
                return (XlsxColumnType.Integer, null);
            }

            return (best.Type, best.Currency);
        }

        private void Chart(FencedCodeBlock code)
        {
            var line = this.LineOf(code);
            var where = string.Create(CultureInfo.InvariantCulture, $"The chart block at line {line}");
            var lines = code.Lines.ToString().Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            var separator = Array.FindIndex(lines, l => l.Trim() == "---");
            var settings = separator < 0 ? lines : lines[..separator];
            var data = separator < 0 ? [] : lines[(separator + 1)..].Where(l => l.Trim().Length > 0).ToList();
            var chart = new XlsxChart { Category = string.Empty };
            string? decimalSetting = null;
            string? category = null;
            List<string>? series = null;
            var failed = false;
            foreach (var setting in settings.Select(s => (s.IndexOf(" #", StringComparison.Ordinal) is var hash and >= 0 ? s[..hash] : s).Trim()).Where(s => s.Length > 0))
            {
                var colon = setting.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    this.Problem(NoteCodes.ChartInvalid, line, $"{where}: '{setting}' is not a setting; write 'key: value', and the data, if any, below a line '---'.");
                    failed = true;
                    continue;
                }

                var (key, value) = (setting[..colon].Trim(), setting[(colon + 1)..].Trim());
                switch (key.ToLowerInvariant())
                {
                    case "type" when ChartTypes.TryGetValue(value, out var kind):
                        chart = chart with { Kind = kind };
                        break;
                    case "type":
                        this.Problem(NoteCodes.ChartInvalid, line, $"{where}: the type '{value}' is not one of {string.Join(", ", ChartTypes.Keys)}.");
                        failed = true;
                        break;
                    case "stacked" when value.ToLowerInvariant() is "true" or "false":
                        chart = chart with { Stacked = value.Equals("true", StringComparison.OrdinalIgnoreCase) };
                        break;
                    case "placement" when value.ToLowerInvariant() is "beside" or "sheet":
                        chart = chart with { Placement = value.Equals("sheet", StringComparison.OrdinalIgnoreCase) ? XlsxChartPlacement.Sheet : XlsxChartPlacement.Beside };
                        break;
                    case "stacked" or "placement":
                        this.Problem(NoteCodes.ChartInvalid, line, $"{where}: {key} is {(key.Equals("stacked", StringComparison.OrdinalIgnoreCase) ? "true or false" : "beside or sheet")}, not '{value}'.");
                        failed = true;
                        break;
                    case "title":
                        chart = chart with { Title = value.Length > 0 ? value : null };
                        break;
                    case "unit":
                        chart = chart with { Unit = value.Length > 0 ? value : null };
                        break;
                    case "source":
                        chart = chart with { Source = value.Length > 0 ? value : null };
                        break;
                    case "sheet":
                        chart = chart with { SheetName = value.Length > 0 ? value : null, Placement = XlsxChartPlacement.Sheet };
                        break;
                    case "decimal":
                        decimalSetting = value;
                        break;
                    case "category":
                        category = value;
                        break;
                    case "series":
                        series = [.. value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
                        break;
                    default:
                        var close = XlsxFormula.Closest(key, ChartSettings);
                        this.Problem(NoteCodes.ChartInvalid, line, $"{where}: the setting '{key}' is unknown.{(close.Count > 0 ? $" Did you mean '{string.Join("' or '", close)}'?" : string.Empty)} Valid settings: {string.Join(", ", ChartSettings)}.");
                        failed = true;
                        break;
                }
            }

            if (failed)
            {
                return;
            }

            if (separator < 0)
            {
                // The column form: the chart belongs to the table above it.
                if (this.current is not { } part)
                {
                    this.Problem(NoteCodes.ChartInvalid, line, $"{where} names columns, but follows no table under its '##'. Put it right after its table, or give it its data as CSV below a line '---'.");
                    return;
                }

                part.Charts.Add(chart with { Category = category ?? string.Empty, Series = series ?? [] });
                return;
            }

            if (category is not null || series is not null)
            {
                this.Problem(NoteCodes.ChartInvalid, line, $"{where} has its own data, whose header row names the category and the series. Leave out 'category' and 'series', or the data.");
                return;
            }

            ChartSpec spec;
            try
            {
                spec = ChartParser.Parse((decimalSetting is null ? string.Empty : "decimal: " + decimalSetting + "\n") + "---\n" + string.Join("\n", data), 0);
            }
            catch (ArgumentException ex)
            {
                this.Problem(NoteCodes.ChartInvalid, line, $"{where}: {ex.Message}");
                return;
            }

            // The data form: its data is a table of its own on the chart's sheet, the chart beside it.
            var header = Cells(data[0]);
            var table = this.NewPart(line);
            table.Columns =
            [
                new XlsxColumn(header[0], XlsxColumnType.Text),
                .. spec.Series.Select(s => new XlsxColumn(s.Name, XlsxColumnType.Number) { Decimals = Math.Min(10, s.Values.Select(Decimals).DefaultIfEmpty(0).Max()) }),
            ];
            for (var r = 0; r < spec.Categories.Count; r++)
            {
                table.Rows.Add([XlsxCell.FromText(spec.Categories[r]), .. spec.Series.Select(s => s.Values[r] is { } v ? XlsxCell.FromNumber(v) : XlsxCell.Empty)]);
            }

            table.Charts.Add(chart with { Category = header[0], Series = [.. spec.Series.Select(s => s.Name)] });
        }

        private static int Decimals(double? value)
        {
            if (value is not { } number)
            {
                return 0;
            }

            var text = number.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('E', StringComparison.Ordinal) || text.IndexOf('.', StringComparison.Ordinal) is not (>= 0 and var dot) ? 0 : text.Length - dot - 1;
        }

        // The header row of a chart's CSV, split as the chart parser splits it.
        private static List<string> Cells(string row)
        {
            var quoted = false;
            var separator = ',';
            foreach (var c in row)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == ';' && !quoted)
                {
                    separator = ';';
                    break;
                }
            }

            var cells = new List<string>();
            var cell = new StringBuilder();
            quoted = false;
            foreach (var c in row)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == separator && !quoted)
                {
                    cells.Add(cell.ToString().Trim());
                    cell.Clear();
                }
                else
                {
                    cell.Append(c);
                }
            }

            cells.Add(cell.ToString().Trim());
            return cells;
        }

        private void Problem(string code, int line, string message)
        {
            if (this.problems.Count < XlsxPlan.MaxProblems)
            {
                this.problems.Add(new GenerationNote(code, line, message));
            }
        }

        // A heading's text, its attributes (checked as in the other formats) taken off.
        private static string HeadingText(HeadingBlock heading)
        {
            var text = Text(heading.Inline).Trim();
            return (heading.Level <= 2 ? HeadingAttributes.Split(text, Format.Xlsx) : HeadingAttributes.SplitDeep(text, Format.Xlsx)).Text.Trim();
        }

        private static bool IsImages(ParagraphBlock paragraph) =>
            paragraph.Inline is { } inline && inline.Any(i => i is LinkInline { IsImage: true })
            && inline.All(i => i is LinkInline { IsImage: true } || (i is LiteralInline literal && literal.Content.IsEmptyOrWhitespace()) || i is LineBreakInline);
    }

    // What a header cell declares.
    private sealed class Declared
    {
        public string Header { get; set; } = string.Empty;

        public int Line { get; set; }

        public XlsxColumnType? Type { get; set; }

        public string? TypeName { get; set; }

        public string? Currency { get; set; }

        public int? Decimals { get; set; }

        public XlsxTotal? Total { get; set; }

        public double? Width { get; set; }

        public XlsxAlign? Align { get; set; }
    }

    /// <summary>The text of inline Markdown, its formatting dropped: a link is its text, code its content, a break a space.</summary>
    internal static string Text(ContainerInline? inline)
    {
        var text = new StringBuilder();
        void Append(Inline? node)
        {
            for (; node is not null; node = node.NextSibling)
            {
                switch (node)
                {
                    case LiteralInline literal:
                        text.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        text.Append(code.Content);
                        break;
                    case LineBreakInline:
                        text.Append(' ');
                        break;
                    case HtmlEntityInline entity:
                        text.Append(entity.Transcoded.ToString());
                        break;
                    case AutolinkInline auto:
                        text.Append(auto.Url);
                        break;
                    case HtmlInline html:
                        text.Append(html.Tag);
                        break;
                    case ContainerInline container:
                        Append(container.FirstChild);
                        break;
                }
            }
        }

        Append(inline?.FirstChild);
        return text.ToString();
    }
}
