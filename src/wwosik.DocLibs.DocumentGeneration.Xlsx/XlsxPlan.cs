using System.Globalization;
using System.Text;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// One workbook resolved for writing, checking and previewing: brand, language, culture, marking, where every sheet's
/// rows go, each column's format and width, and every problem found, which <see cref="XlsxGenerator.Generate(XlsxWorkbook, Stream, XlsxOptions?)"/> throws.
/// </summary>
internal sealed class XlsxPlan
{
    /// <summary>The most problems a check reports.</summary>
    public const int MaxProblems = 500;

    /// <summary>The key of <see cref="Exception.Data"/> that holds the JSON path of a <see cref="DocumentConversionException"/> the plan throws.</summary>
    public const string PathKey = "wwosik.DocLibs.Xlsx.Path";

    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public const int MaxText = 32_767;
    public const int MaxSheetName = 31;
    private const double MinWidth = 6;
    private const double MaxWidth = 50;

    // Rows measured for a column's width: enough for its look, cheap for a million rows.
    private const int MeasuredRows = 10_000;

    private static readonly DateTime FirstDate = new(1900, 1, 1);

    // Where in the workbook's JSON form the plan is, for the problems and notes it finds there.
    private string at = "$";

    private readonly List<GenerationNote> problems = [];
    private readonly List<string> problemPaths = [];
    private readonly Dictionary<(string Code, string Message), string> notePaths = [];
    private readonly HashSet<int> missing = [];
    private readonly Dictionary<string, XlsxFormula> formulas = new(StringComparer.Ordinal);
    private readonly HashSet<string> unknownFunctions = new(StringComparer.Ordinal);

    private XlsxPlan(XlsxWorkbook workbook, XlsxOptions options)
    {
        this.Workbook = workbook;
        this.Options = options;
    }

    public XlsxWorkbook Workbook { get; }

    public XlsxOptions Options { get; }

    public GenerationNotes Notes { get; } = new();

    public IReadOnlyList<GenerationNote> Problems => this.problems;

    /// <summary>
    /// Where each of <see cref="Problems"/> is in the workbook's JSON form, such as <c>$.sheets[0].rows[2][1]</c>: the
    /// indexes of the model's lists, which are the JSON's; <c>$</c> for a problem found before the plan.
    /// </summary>
    public IReadOnlyList<string> ProblemPaths => this.problemPaths;

    public ResolvedBrand Brand { get; private set; } = null!;

    public GeneratedTexts Texts { get; private set; } = GeneratedTexts.English;

    public string LanguageTag { get; private set; } = "en";

    public CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    public Marking Marking { get; private set; } = Marking.None;

    /// <summary>The marking's text, empty without one.</summary>
    public string MarkingText => this.Marking.Text(this.Texts);

    /// <summary>Whether the info sheet is written, first.</summary>
    public bool Info { get; private set; }

    /// <summary>The info sheet's name: <see cref="GeneratedTexts.Info"/>, numbered if a sheet already has it.</summary>
    public string InfoName { get; private set; } = "Info";

    /// <summary>The date shown, <see langword="null"/> when <see cref="XlsxOptions.ShowDate"/> is off.</summary>
    public DateOnly? Date { get; private set; }

    public DateTimeOffset Now { get; private set; }

    public IReadOnlyList<SheetPlan> Sheets { get; private set; } = [];

    /// <summary>Whether a sheet has a formula other than its totals: the workbook then asks Excel to calculate on opening.</summary>
    public bool HasFormulas { get; private set; }

    /// <summary>The colours as the sheet shows them, readable on Excel's white.</summary>
    public SheetColors Colors { get; private set; } = null!;

    /// <summary>
    /// The plan of <paramref name="workbook"/>. Problems in the workbook are collected, not thrown.
    /// </summary>
    /// <exception cref="DocumentConversionException">The workbook names an unknown brand, or its language is not a language tag.</exception>
    /// <exception cref="ArgumentException">A brand is invalid, or two share a name.</exception>
    public static XlsxPlan Of(XlsxWorkbook workbook, XlsxOptions? options, bool measure = true)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        options ??= XlsxOptions.Default;
        var plan = new XlsxPlan(workbook, options);
        plan.Resolve(measure);
        return plan;
    }

    /// <summary>Generate's exception: the first ten problems.</summary>
    public DocumentConversionException Failure()
    {
        var text = new StringBuilder(this.problems.Count == 1
            ? "The workbook has a problem: "
            : string.Create(CultureInfo.InvariantCulture, $"The workbook has {(this.problems.Count >= MaxProblems ? "at least " : string.Empty)}{this.problems.Count} problems:"));
        if (this.problems.Count == 1)
        {
            text.Append(Located(this.problems[0]));
        }
        else
        {
            foreach (var problem in this.problems.Take(10))
            {
                text.Append("\n- ").Append(Located(problem));
            }

            if (this.problems.Count > 10)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n- … and {this.problems.Count - 10} more.");
            }
        }

        return new DocumentConversionException(Format.Xlsx, text.ToString());
    }

    /// <summary>
    /// Puts <paramref name="found"/>, problems found before the plan (in the Markdown a workbook was read from), before the
    /// plan's own, at most <see cref="MaxProblems"/> in all.
    /// </summary>
    public void Prepend(IReadOnlyList<GenerationNote> found)
    {
        this.problems.InsertRange(0, found);
        this.problemPaths.InsertRange(0, found.Select(_ => "$"));
        if (this.problems.Count > MaxProblems)
        {
            this.problems.RemoveRange(MaxProblems, this.problems.Count - MaxProblems);
            this.problemPaths.RemoveRange(MaxProblems, this.problemPaths.Count - MaxProblems);
        }
    }

    // A problem for the exception's message: with its Markdown line, when it has one.
    private static string Located(GenerationNote problem) => problem.Line > 0 ? problem.ToString() : problem.Message;

    /// <summary>The statistics of a check.</summary>
    public CheckStatistics Statistics()
    {
        var words = new[] { this.Workbook.Title, this.Workbook.Subtitle, this.Workbook.Description }.Sum(Words);
        foreach (var sheet in this.Sheets)
        {
            words += Words(sheet.Sheet.Title) + Words(sheet.Sheet.Description) + sheet.Sheet.Columns.Sum(c => Words(c?.Header));
            foreach (var row in sheet.Sheet.Rows)
            {
                words += row?.Sum(c => c is { Kind: XlsxCellKind.Text } ? Words(c.Text) : 0) ?? 0;
            }
        }

        return new CheckStatistics
        {
            Sheets = this.Sheets.Count + (this.Info ? 1 : 0) + this.Sheets.Sum(s => s.Charts.Count(c => c.SheetName is not null)),
            Tables = this.Sheets.Count(s => s.Columns.Count > 0),
            Diagrams = this.Sheets.Sum(s => s.Charts.Count),
            Words = words,
        };
    }

    /// <summary>The Excel name of the column at <paramref name="index"/> from 0: A, …, Z, AA.</summary>
    public static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    /// <summary>Excel's serial number of a date: days since 30 December 1899, with Excel's 29 February 1900 kept.</summary>
    public static double Serial(DateTime value)
    {
        var days = (value - new DateTime(1899, 12, 30)).TotalDays;
        return value < new DateTime(1900, 3, 1) ? days - 1 : days;
    }

    /// <summary>A sheet name in a reference: <c>'Sales 2026'</c>, an apostrophe doubled.</summary>
    public static string Quoted(string sheet) => "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>The parsed formula of <paramref name="source"/>, parsed once per workbook.</summary>
    public XlsxFormula Formula(string source)
    {
        if (!this.formulas.TryGetValue(source, out var formula))
        {
            this.formulas[source] = formula = XlsxFormula.Parse(source);
        }

        return formula;
    }

    // The JSON path of the sheet at `index`.
    private static string Path(int index) => string.Create(CultureInfo.InvariantCulture, $"$.sheets[{index}]");

    // `exception` with the JSON path of what it is about, for the JSON form's check.
    private static DocumentConversionException At(string path, DocumentConversionException exception)
    {
        exception.Data[PathKey] = path;
        return exception;
    }

    private static int Words(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : DocumentCheck.Words(text);

    private void Resolve(bool measure)
    {
        var options = this.Options;
        var workbook = this.Workbook;
        var registry = BrandRegistry.Of(options.Brand, options.Brands);
        var brand = workbook.Brand is { } name
            ? registry.Find(name.Trim()) ?? throw At("$.brand", new DocumentConversionException(Format.Xlsx, $"The workbook names the brand '{name}'; expected {registry.Expected}."))
            : options.Brand ?? DocumentGeneration.Brand.Neutral;
        this.Brand = ResolvedBrand.Resolve(brand);
        DocumentLanguage language;
        try
        {
            language = DocumentLanguage.Resolve(workbook.Language, "en", options.Texts, this.Notes, Format.Xlsx);
        }
        catch (DocumentConversionException ex)
        {
            throw At("$.language", ex);
        }

        this.Texts = language.Texts;
        this.LanguageTag = language.Tag;
        this.Culture = options.Culture ?? XlsxFormat.Culture(language.Tag);
        this.Marking = this.ResolveMarking();
        this.Now = options.TimeProvider.GetUtcNow();
        this.Date = options.ShowDate ? workbook.Date ?? DateOnly.FromDateTime(options.TimeProvider.GetLocalNow().DateTime) : null;
        this.Info = options.Info ?? (!string.IsNullOrWhiteSpace(workbook.Title) || !string.IsNullOrWhiteSpace(workbook.Description));
        this.Colors = SheetColors.Of(this.Brand);

        if (workbook.Sheets is null || workbook.Sheets.Count == 0)
        {
            if (!this.Info)
            {
                this.Problem(NoteCodes.ExcelLimit, "The workbook has no sheet, and Excel needs at least one. Add a sheet, or a title for the info sheet.", "$.sheets");
            }

            this.Sheets = [];
        }
        else
        {
            this.CheckNames(workbook.Sheets);
            this.Sheets = [.. workbook.Sheets.Select((s, i) => this.Sheet(s, i, measure))];
            this.CheckChartSheets();
        }

        if (this.Info)
        {
            this.InfoName = this.FreeName(this.Texts.Info);
        }

        this.NameChartSheets();
        if (this.Info)
        {
            this.at = "$.title";
            this.CheckCharacters(workbook.Title, true, "the workbook's title");
            this.at = "$.subtitle";
            this.CheckCharacters(workbook.Subtitle, true, "the workbook's subtitle");
            this.at = "$.description";
            this.CheckCharacters(workbook.Description, true, "the workbook's description");
            this.at = "$.author";
            this.CheckCharacters(workbook.Author, true, "the workbook's author");
            this.at = "$";
        }
    }

    private Marking ResolveMarking()
    {
        var host = this.Options.Marking ?? new XlsxMarking(Classification.Public);
        var own = this.Workbook.Marking;
        var classification = host.Classification;
        if (own is not null)
        {
            if (own.Classification < host.Classification)
            {
                this.at = "$.marking.classification";
                this.Note(
                    NoteCodes.ClassificationKept,
                    $"The workbook asks for the classification '{own.Classification.ToString().ToLowerInvariant()}', but the host marks it '{host.Classification.ToString().ToLowerInvariant()}', which is kept: the workbook can raise the classification, not lower it.");
            }

            this.at = "$";
            classification = own.Classification > classification ? own.Classification : classification;
        }

        return new Marking(classification, host.Draft || own?.Draft == true);
    }

    private string FreeName(string name)
    {
        var taken = this.TakenNames();
        var candidate = name.Length > MaxSheetName ? name[..MaxSheetName] : name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            var suffix = string.Create(CultureInfo.InvariantCulture, $" {n}");
            candidate = (name.Length + suffix.Length > MaxSheetName ? name[..(MaxSheetName - suffix.Length)] : name) + suffix;
        }

        return candidate;
    }

    // The names of the table sheets and of the chart sheets named so far, ignoring case.
    private HashSet<string> TakenNames()
    {
        var taken = this.Sheets.Select(s => s.Sheet.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(this.Sheets.SelectMany(s => s.Charts).Select(c => c.SheetName).OfType<string>());
        return taken;
    }

    // What breaks Excel's rules for a sheet name, each as the end of a message.
    private static IEnumerable<string> NameRules(string name)
    {
        if (name.Trim().Length == 0)
        {
            yield return "the name is empty. Give the sheet a name.";
        }
        else if (name.Length > MaxSheetName)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"the name has {name.Length} characters; Excel allows {MaxSheetName}.");
        }

        if (name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) is var bad and >= 0)
        {
            yield return $"the name holds '{name[bad]}'; Excel allows none of []:*?/\\ in a sheet name.";
        }

        if (name.StartsWith('\'') || name.EndsWith('\''))
        {
            yield return "the name starts or ends with an apostrophe, which Excel does not allow.";
        }

        if (name.Any(char.IsControl))
        {
            yield return "the name holds a control character.";
        }
    }

    private void CheckNames(IReadOnlyList<XlsxSheet> sheets)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sheets.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(sheets[i], nameof(XlsxWorkbook.Sheets));
            var name = sheets[i].Name ?? string.Empty;
            var where = string.Create(CultureInfo.InvariantCulture, $"Sheet {i + 1} ('{name}')");
            this.at = Path(i) + ".name";
            foreach (var rule in NameRules(name))
            {
                this.Problem(NoteCodes.SheetName, $"{where}: {rule}");
            }

            if (name.Length > 0 && seen.TryGetValue(name, out var first))
            {
                this.Problem(NoteCodes.SheetName, string.Create(CultureInfo.InvariantCulture, $"{where}: the name is the same as sheet {first + 1}'s, ignoring case; Excel needs unique names."));
            }
            else
            {
                seen[name] = i;
            }
        }

        this.at = "$";
    }

    private SheetPlan Sheet(XlsxSheet sheet, int index, bool measure)
    {
        var label = $"Sheet '{sheet.Name}'";
        var title = string.IsNullOrWhiteSpace(sheet.Title) ? null : sheet.Title;
        var subtitle = string.IsNullOrWhiteSpace(sheet.Description) ? null : sheet.Description;
        var above = (title is null ? 0 : 1) + (subtitle is null ? 0 : 1);
        var showsDate = above > 0 && this.Date is not null;
        if (showsDate)
        {
            above++;
        }

        // A blank row between the title block and the table.
        var headerRow = above == 0 ? 1 : above + 2;
        var columns = sheet.Columns ?? [];
        var rows = sheet.Rows ?? [];
        var path = Path(index);
        this.at = path + ".title";
        this.CheckCharacters(title, true, $"{label}, its title");
        this.CheckText(title, $"{label}, its title");
        this.at = path + ".description";
        this.CheckCharacters(subtitle, true, $"{label}, its description");
        this.CheckText(subtitle, $"{label}, its description");
        this.at = path + ".columns";

        if (columns.Count == 0)
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{label} has no columns. Give it at least one.");
        }

        if (columns.Count > MaxColumns)
        {
            this.Problem(NoteCodes.ExcelLimit, string.Create(CultureInfo.InvariantCulture, $"{label} has {columns.Count:N0} columns; Excel allows {MaxColumns:N0}."));
        }

        var hasTotals = rows.Count > 0 && columns.Any(c => c?.Total is not null);
        if (headerRow + rows.Count + (hasTotals ? 1 : 0) > MaxRows)
        {
            this.Problem(NoteCodes.ExcelLimit, string.Create(CultureInfo.InvariantCulture, $"{label} has {rows.Count:N0} rows below row {headerRow}; Excel allows {MaxRows:N0} rows in all. Split the data over sheets."), path + ".rows");
        }

        var lookup = Lookup(columns);
        var (types, currencies) = this.Types(columns, rows, lookup);
        var plans = new List<ColumnPlan>(columns.Count);
        for (var c = 0; c < columns.Count; c++)
        {
            this.at = string.Create(CultureInfo.InvariantCulture, $"{path}.columns[{c}]");
            plans.Add(this.Column(columns[c], c, types[c], currencies[c], label, headerRow, rows, measure));
        }

        for (var r = 0; r < rows.Count && this.problems.Count < MaxProblems; r++)
        {
            var row = rows[r] ?? [];
            var excelRow = headerRow + 1 + r;
            var rowPath = string.Create(CultureInfo.InvariantCulture, $"{path}.rows[{r}]");
            if (row.Count > columns.Count)
            {
                this.Problem(NoteCodes.RowTooWide, string.Create(CultureInfo.InvariantCulture, $"{label}, row {r + 1} ({ColumnName(columns.Count)}{excelRow}): the row has {row.Count} cells, but the sheet has {columns.Count} columns."), string.Create(CultureInfo.InvariantCulture, $"{rowPath}[{columns.Count}]"));
            }

            var hasFormula = false;
            for (var c = 0; c < Math.Min(row.Count, columns.Count); c++)
            {
                var cell = row[c] ?? XlsxCell.Empty;
                var where = string.Create(CultureInfo.InvariantCulture, $"{label}, row {r + 1}, column '{plans[c].Column.Header}' ({ColumnName(c)}{excelRow})");
                this.at = string.Create(CultureInfo.InvariantCulture, $"{rowPath}[{c}]");
                if (cell.Kind == XlsxCellKind.Formula)
                {
                    hasFormula = true;
                    this.at += ".formula";
                    this.FormulaCell(cell.Text!, lookup, where);
                }
                else
                {
                    this.Cell(cell, plans[c], where);
                }
            }

            if (hasFormula)
            {
                this.Circular(row, plans, lookup, label, r, excelRow, rowPath);
            }
        }

        var totals = hasTotals ? plans.Select(p => this.Total(p, rows)).ToList() : null;
        string? totalsLabel = null;
        var labelColumn = -1;
        if (totals is not null && plans.FindIndex(p => p.Column.Total is null) is >= 0 and var first)
        {
            labelColumn = first;
            totalsLabel = string.IsNullOrWhiteSpace(sheet.TotalsLabel) ? this.Texts.Total : sheet.TotalsLabel.Trim();
            this.at = path + ".totalsLabel";
            this.CheckText(totalsLabel, $"{label}, its totals label");
            this.CheckCharacters(totalsLabel, false, $"{label}, its totals label");
        }

        if (totals is not null && measure)
        {
            for (var c = 0; c < plans.Count; c++)
            {
                var shown = c == labelColumn ? totalsLabel : totals[c] is { Shown: { } cell } total ? total.Format.Display(cell) : null;
                if (plans[c].Column.Width is null && shown is not null)
                {
                    plans[c] = plans[c] with { Width = Math.Max(plans[c].Width, Math.Round(Math.Clamp(this.Characters(shown, true) + 1, MinWidth, MaxWidth), 2)) };
                }
            }
        }

        var charts = this.Charts(sheet, path, label, headerRow, plans, lookup);
        this.at = "$";
        return new SheetPlan(sheet, index, title, subtitle, showsDate, headerRow, plans)
        {
            Charts = charts,
            Lookup = lookup,
            Totals = totals,
            TotalsLabel = totalsLabel,
            LabelColumn = labelColumn,
        };
    }

    // The sheet's charts, checked: their columns, kind and placement; a chart sheet's name is checked across the workbook.
    private List<ChartPlan> Charts(XlsxSheet sheet, string sheetPath, string label, int headerRow, List<ColumnPlan> plans, Dictionary<string, int> lookup)
    {
        var charts = new List<ChartPlan>();
        var rows = sheet.Rows ?? [];
        var given = sheet.Charts ?? [];
        for (var n = 0; n < given.Count; n++)
        {
            var chart = given[n];
            ArgumentNullException.ThrowIfNull(chart, nameof(XlsxSheet.Charts));
            var where = string.Create(CultureInfo.InvariantCulture, $"{label}, chart {n + 1}");
            var path = string.Create(CultureInfo.InvariantCulture, $"{sheetPath}.charts[{n}]");
            this.at = path;
            var problems = this.problems.Count;
            int? Column(string? header, string role, string at)
            {
                this.at = at;
                if (string.IsNullOrWhiteSpace(header))
                {
                    this.Problem(NoteCodes.ChartInvalid, $"{where}: the {role} names no column. Give a column header.");
                    return null;
                }

                if (!lookup.TryGetValue(header.Trim(), out var c))
                {
                    var close = XlsxFormula.Closest(header.Trim(), lookup.Keys);
                    var hint = close.Count > 0 ? $" Did you mean '{string.Join("' or '", close)}'?" : $" The columns are '{string.Join("', '", lookup.Keys)}'.";
                    this.Problem(NoteCodes.ChartInvalid, $"{where}: the {role} '{header}' is not a column of the sheet.{hint}");
                    return null;
                }

                if (c < 0)
                {
                    this.Problem(NoteCodes.ChartInvalid, $"{where}: the {role} '{header}' is the header of more than one column. Give the columns different headers.");
                    return null;
                }

                return c;
            }

            var category = Column(chart.Category, "category", path + ".category");
            var series = new List<int>();
            var k = 0;
            foreach (var header in chart.Series ?? [])
            {
                if (Column(header, "series", string.Create(CultureInfo.InvariantCulture, $"{path}.series[{k++}]")) is not { } c)
                {
                    continue;
                }

                var type = plans[c].Format.Type;
                if (type is not (XlsxColumnType.Number or XlsxColumnType.Integer or XlsxColumnType.Currency or XlsxColumnType.Percent))
                {
                    this.Problem(NoteCodes.ChartInvalid, $"{where}: the series '{header}' is a {type} column; a series takes a Number, Integer, Currency or Percent column.");
                    continue;
                }

                series.Add(c);
            }

            this.at = path;
            if (chart.Series is null || chart.Series.Count == 0)
            {
                this.Problem(NoteCodes.ChartInvalid, $"{where}: the chart has no series. Name at least one numeric column in Series.", path + ".series");
            }

            if (rows.Count == 0)
            {
                this.Problem(NoteCodes.ChartInvalid, $"{where}: the sheet has no rows to draw. Add rows, or leave the chart out.");
            }

            if (!Enum.IsDefined(chart.Kind))
            {
                this.Problem(NoteCodes.ChartInvalid, $"{where}: the kind {chart.Kind} is not one of Column, Bar, Line, Area.", path + ".type");
            }

            if (!Enum.IsDefined(chart.Placement))
            {
                this.Problem(NoteCodes.ChartInvalid, $"{where}: the placement {chart.Placement} is not one of Beside, Sheet.", path + ".placement");
            }

            var title = string.IsNullOrWhiteSpace(chart.Title) ? null : chart.Title.Trim();
            var unit = string.IsNullOrWhiteSpace(chart.Unit) ? null : chart.Unit.Trim();
            var source = string.IsNullOrWhiteSpace(chart.Source) ? null : chart.Source.Trim();
            this.at = path + ".title";
            this.CheckCharacters(title, true, $"{where}, its title");
            this.at = path + ".unit";
            this.CheckCharacters(unit, true, $"{where}, its unit");
            this.at = path + ".source";
            this.CheckCharacters(source, true, $"{where}, its source");
            this.at = path + ".series";
            var palette = this.Brand.Diagram.Palette;
            if (series.Count > palette.Count)
            {
                this.Note(
                    NoteCodes.ChartColorsRepeat,
                    string.Create(CultureInfo.InvariantCulture, $"{where}: the chart has {series.Count} series, but the brand's diagram palette has {palette.Count} colours, so colours repeat. Split the chart, or use fewer series."));
            }

            if (this.problems.Count > problems || category is not { } at)
            {
                continue;
            }

            var sheetName = chart.Placement == XlsxChartPlacement.Sheet && !string.IsNullOrWhiteSpace(chart.SheetName) ? chart.SheetName : null;
            charts.Add(this.Chart(chart, n + 1, sheet.Name, headerRow, plans[at], [.. series.Select(c => plans[c])], rows, title, unit, source) with { SheetName = sheetName });
        }

        return charts;
    }

    // The chart as the writer writes it: its data from the rows, as the cache, and the cells it refers to.
    private ChartPlan Chart(XlsxChart chart, int number, string sheet, int headerRow, ColumnPlan category, IReadOnlyList<ColumnPlan> series, IReadOnlyList<IReadOnlyList<XlsxCell>> rows, string? title, string? unit, string? source)
    {
        XlsxCell At(IReadOnlyList<XlsxCell>? row, int column) => row is not null && column < row.Count ? row[column] ?? XlsxCell.Empty : XlsxCell.Empty;
        static double? Number(XlsxCell cell) => cell.Kind switch
        {
            XlsxCellKind.Number => cell.Number,
            XlsxCellKind.Date or XlsxCellKind.DateTime => Serial(cell.DateTime),
            _ => null,
        };

        var numeric = category.Format.IsNumeric || category.Format.Type is XlsxColumnType.Date or XlsxColumnType.DateTime;
        var labels = rows.Select(r => At(r, category.Index) is { Kind: not (XlsxCellKind.Empty or XlsxCellKind.Formula) } cell ? category.Format.Display(cell) : string.Empty).ToList();
        var spec = new ChartSpec(
            chart.Kind switch { XlsxChartKind.Line => ChartKind.Line, XlsxChartKind.Area => ChartKind.Area, _ => ChartKind.Bar },
            labels,
            [.. series.Select(s => new ChartSeries(s.Column.Header.Trim(), [.. rows.Select(r => Number(At(r, s.Index)))]))])
        {
            Stacked = chart.Stacked,
            Title = title,
            Unit = unit,
            Source = source,
            Colors = this.Brand.Diagram.Palette,
        };

        var first = headerRow + 1;
        var last = headerRow + rows.Count;
        string Range(int column) => string.Create(CultureInfo.InvariantCulture, $"${ColumnName(column)}${first}:${ColumnName(column)}${last}");
        var cells = new ChartCells(
            sheet,
            Range(category.Index),
            [.. series.Select(s => Range(s.Index))],
            [.. series.Select(s => string.Create(CultureInfo.InvariantCulture, $"${ColumnName(s.Index)}${headerRow}"))])
        {
            CategoryNumbers = numeric ? [.. rows.Select(r => Number(At(r, category.Index)))] : null,
            CategoryFormat = numeric ? category.Format.Code : null,
            ValueFormats = [.. series.Select(s => s.Format.Code)],
            Horizontal = chart.Kind == XlsxChartKind.Bar,
            Note = source is null ? null : $"{this.Texts.Source}: {source}",
        };
        return new ChartPlan(chart, number, spec, cells, null);
    }

    // The names a workbook's charts give their chart sheets: valid, and taken by no other sheet.
    private void CheckChartSheets()
    {
        var tables = this.Sheets.Select(s => s.Sheet.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in this.Sheets)
        {
            foreach (var chart in sheet.Charts)
            {
                if (chart.SheetName is not { } name)
                {
                    continue;
                }

                var where = string.Create(CultureInfo.InvariantCulture, $"Sheet '{sheet.Sheet.Name}', chart {chart.Number}");
                this.at = string.Create(CultureInfo.InvariantCulture, $"{Path(sheet.Index)}.charts[{chart.Number - 1}].sheetName");
                foreach (var rule in NameRules(name))
                {
                    this.Problem(NoteCodes.ChartInvalid, $"{where}: its chart sheet's name '{name}': {rule}");
                }

                if (tables.Contains(name) || !seen.Add(name))
                {
                    this.Problem(NoteCodes.ChartInvalid, $"{where}: its chart sheet's name '{name}' is already the name of another sheet, ignoring case. Give the chart sheet a name of its own.");
                }
            }
        }

        this.at = "$";
    }

    // Chart sheets without a name of their own: GeneratedTexts.Chart and a number, "Chart 1", the next free one.
    private void NameChartSheets()
    {
        var taken = this.TakenNames();
        if (this.Info)
        {
            taken.Add(this.InfoName);
        }

        var next = 1;
        var sheets = this.Sheets.ToList();
        for (var s = 0; s < sheets.Count; s++)
        {
            var charts = sheets[s].Charts.ToList();
            for (var c = 0; c < charts.Count; c++)
            {
                if (charts[c].Chart.Placement != XlsxChartPlacement.Sheet || charts[c].SheetName is not null)
                {
                    continue;
                }

                string name;
                do
                {
                    var suffix = string.Create(CultureInfo.InvariantCulture, $" {next++}");
                    var word = this.Texts.Chart;
                    name = (word.Length + suffix.Length > MaxSheetName ? word[..(MaxSheetName - suffix.Length)] : word) + suffix;
                }
                while (taken.Contains(name));

                taken.Add(name);
                charts[c] = charts[c] with { SheetName = name };
            }

            sheets[s] = sheets[s] with { Charts = charts };
        }

        this.Sheets = sheets;
    }

    // Each header, trimmed and ignoring case, to its column; -1 for a header two columns share.
    private static Dictionary<string, int> Lookup(IReadOnlyList<XlsxColumn?> columns)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < columns.Count; c++)
        {
            if (columns[c]?.Header?.Trim() is { Length: > 0 } header)
            {
                lookup[header] = lookup.ContainsKey(header) ? -1 : c;
            }
        }

        return lookup;
    }

    // The type of each column: as declared; without one Text, or for a column of formulas Number, or Currency when its
    // formulas refer only to currency columns of one currency (or it names a currency itself).
    private (XlsxColumnType[] Types, string?[] Currencies) Types(IReadOnlyList<XlsxColumn?> columns, IReadOnlyList<IReadOnlyList<XlsxCell>> rows, Dictionary<string, int> lookup)
    {
        var types = new XlsxColumnType[columns.Count];
        var currencies = new string?[columns.Count];
        var sources = new List<int>?[columns.Count];
        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            types[c] = column?.Type ?? XlsxColumnType.Text;
            currencies[c] = column?.Currency?.Trim().ToUpperInvariant();
            if (column is null || column.Type is not null)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<int>? referred = [];
            foreach (var row in rows)
            {
                if (row is not null && c < row.Count && row[c] is { Kind: XlsxCellKind.Formula, Text: { } text } && seen.Add(text))
                {
                    var formula = this.Formula(text);
                    if (formula.Headers.Count == 0 || formula.Error is not null)
                    {
                        referred = null;
                    }

                    foreach (var (name, _) in formula.Headers)
                    {
                        if (referred is not null && lookup.TryGetValue(name, out var d) && d >= 0 && d != c)
                        {
                            referred.Add(d);
                        }
                        else
                        {
                            referred = null;
                        }
                    }
                }
            }

            if (seen.Count == 0)
            {
                continue;
            }

            types[c] = currencies[c] is null ? XlsxColumnType.Number : XlsxColumnType.Currency;
            sources[c] = currencies[c] is null ? referred : null;
        }

        // Currency spreads from column to column; a column only ever becomes Currency, so this ends.
        for (var changed = true; changed;)
        {
            changed = false;
            for (var c = 0; c < columns.Count; c++)
            {
                if (sources[c] is { Count: > 0 } referred && types[c] != XlsxColumnType.Currency
                    && referred.All(d => types[d] == XlsxColumnType.Currency && currencies[d] == currencies[referred[0]]))
                {
                    types[c] = XlsxColumnType.Currency;
                    currencies[c] = currencies[referred[0]];
                    changed = true;
                }
            }
        }

        return (types, currencies);
    }

    // A formula cell: it parses, its [Header]s name columns, its names are parameters, its functions are known.
    private void FormulaCell(string source, Dictionary<string, int> lookup, string where)
    {
        this.HasFormulas = true;
        if (source.Length > XlsxFormula.MaxLength)
        {
            this.Problem(NoteCodes.ExcelLimit, string.Create(CultureInfo.InvariantCulture, $"{where}: the formula has {source.Length:N0} characters; Excel holds {XlsxFormula.MaxLength:N0}."));
            return;
        }

        var formula = this.Formula(source);
        var quoted = GenerationNotes.Quote(source);
        if (formula.Error is { } error)
        {
            this.Problem(NoteCodes.FormulaInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: the formula '{quoted}' cannot be read at character {error.Position}: {error.Message}."));
            return;
        }

        foreach (var (name, position) in formula.Headers)
        {
            if (!lookup.TryGetValue(name, out var column))
            {
                var close = XlsxFormula.Closest(name, lookup.Keys);
                var hint = close.Count > 0 ? $" Did you mean {string.Join(" or ", close.Select(h => "[" + h + "]"))}?" : $" The columns are {string.Join(", ", lookup.Keys.Select(h => "[" + h + "]"))}.";
                this.Problem(NoteCodes.FormulaInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: the formula '{quoted}' refers to [{name}] at character {position}, but the sheet has no column of that header.{hint}"));
            }
            else if (column < 0)
            {
                this.Problem(NoteCodes.FormulaInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: the formula '{quoted}' refers to [{name}] at character {position}, which is the header of more than one column. Give the columns different headers."));
            }
        }

        foreach (var (name, position) in formula.Names)
        {
            var hint = lookup.ContainsKey(name) ? $" To refer to the column, write [{name}]." : " Write text in double quotes, a column as [Header], or a cell as A1.";
            this.Problem(NoteCodes.FormulaInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: the formula '{quoted}' holds the name '{name}' at character {position}, which is neither a function, a reference nor TRUE or FALSE.{hint}"));
        }

        foreach (var (name, position) in formula.UnknownFunctions)
        {
            if (this.unknownFunctions.Add(name))
            {
                var close = XlsxFormula.Closest(name, XlsxFormula.Functions.Keys);
                var hint = close.Count > 0 ? $" Did you mean {string.Join(" or ", close)}?" : string.Empty;
                this.Note(
                    NoteCodes.FunctionUnknown,
                    string.Create(CultureInfo.InvariantCulture, $"{where}: the formula '{quoted}' calls {name} at character {position}, a function the generator does not know; it is written as it is, and Excel shows #NAME? unless it or an add-in knows it.{hint}"));
            }
        }
    }

    // A formula that needs its own value within its row, through [Header] or an A1 reference to the row: once per row.
    private void Circular(IReadOnlyList<XlsxCell> row, List<ColumnPlan> plans, Dictionary<string, int> lookup, string label, int index, int excelRow, string rowPath)
    {
        var count = Math.Min(row.Count, plans.Count);
        var edges = new List<(int Target, int Position)>?[count];
        for (var c = 0; c < count; c++)
        {
            if (row[c] is not { Kind: XlsxCellKind.Formula, Text: { Length: <= XlsxFormula.MaxLength } text } || this.Formula(text) is not { Error: null } formula)
            {
                continue;
            }

            var list = new List<(int, int)>();
            foreach (var (name, position) in formula.Headers)
            {
                if (lookup.TryGetValue(name, out var d) && d >= 0)
                {
                    list.Add((d, position));
                }
            }

            foreach (var (column, cellRow, position) in formula.Cells)
            {
                if ((cellRow ?? excelRow) == excelRow && column < plans.Count)
                {
                    list.Add((column, position));
                }
            }

            edges[c] = list;
        }

        // Depth first: 0 unseen, 1 on the path, 2 done.
        var state = new int[count];
        var path = new List<(int Column, int Position)>();
        bool Visit(int c)
        {
            state[c] = 1;
            foreach (var (target, position) in edges[c] ?? [])
            {
                if (target >= count || edges[target] is null)
                {
                    continue;
                }

                path.Add((c, position));
                if (state[target] == 1)
                {
                    var start = path.FindIndex(p => p.Column == target);
                    var cycle = path[start..];
                    var names = string.Join(" → ", cycle.Select(p => "[" + plans[p.Column].Column.Header + "]").Append("[" + plans[target].Column.Header + "]"));
                    var (from, at) = cycle[0];
                    this.Problem(NoteCodes.FormulaInvalid, string.Create(CultureInfo.InvariantCulture, $"{label}, row {index + 1}, column '{plans[from].Column.Header}' ({ColumnName(from)}{excelRow}): the formula refers to itself within its row at character {at}: {names}. Excel cannot compute a circular reference; take one of the references out."), string.Create(CultureInfo.InvariantCulture, $"{rowPath}[{from}].formula"));
                    return true;
                }

                if (state[target] == 0 && Visit(target))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            state[c] = 2;
            return false;
        }

        for (var c = 0; c < count; c++)
        {
            if (edges[c] is not null && state[c] == 0 && Visit(c))
            {
                return;
            }
        }
    }

    // A column's total: the SUBTOTAL function, its format, and its value when the column holds no formula (Count always).
    private TotalPlan? Total(ColumnPlan column, IReadOnlyList<IReadOnlyList<XlsxCell>> rows)
    {
        if (column.Column.Total is not { } total || !Enum.IsDefined(total) || (column.Format.Type is XlsxColumnType.Text or XlsxColumnType.Boolean && total != XlsxTotal.Count))
        {
            return null;
        }

        var values = new List<double>();
        var filled = 0;
        var formula = false;
        foreach (var row in rows)
        {
            if (row is null || column.Index >= row.Count || row[column.Index] is not { Kind: not XlsxCellKind.Empty } cell)
            {
                continue;
            }

            filled++;
            switch (cell.Kind)
            {
                case XlsxCellKind.Formula:
                    formula = true;
                    break;
                case XlsxCellKind.Number:
                    values.Add(cell.Number);
                    break;
                case XlsxCellKind.Date or XlsxCellKind.DateTime:
                    values.Add(Serial(cell.DateTime));
                    break;
            }
        }

        var format = total switch
        {
            XlsxTotal.Count => XlsxFormat.For(XlsxColumnType.Integer, null, null, this.Culture),
            XlsxTotal.Average when column.Format.Type == XlsxColumnType.Integer => XlsxFormat.For(XlsxColumnType.Number, null, null, this.Culture),
            _ => column.Format,
        };
        var function = total switch
        {
            XlsxTotal.Sum => 109,
            XlsxTotal.Average => 101,
            XlsxTotal.Count => 103,
            XlsxTotal.Min => 105,
            _ => 104,
        };
        if (formula && total != XlsxTotal.Count)
        {
            return new TotalPlan(function, format, null, null);
        }

        if (total == XlsxTotal.Average && values.Count == 0)
        {
            return new TotalPlan(function, format, null, "#DIV/0!");
        }

        var value = total switch
        {
            XlsxTotal.Sum => values.Sum(),
            XlsxTotal.Average => values.Average(),
            XlsxTotal.Count => filled,
            XlsxTotal.Min => values.Count == 0 ? 0 : values.Min(),
            _ => values.Count == 0 ? 0 : values.Max(),
        };
        return new TotalPlan(function, format, value, null);
    }

    private ColumnPlan Column(XlsxColumn? column, int index, XlsxColumnType type, string? inferred, string label, int headerRow, IReadOnlyList<IReadOnlyList<XlsxCell>> rows, bool measure)
    {
        column ??= new XlsxColumn(string.Empty);
        var where = string.Create(CultureInfo.InvariantCulture, $"{label}, column {index + 1} ('{column.Header}', {ColumnName(index)}{headerRow})");
        if (string.IsNullOrWhiteSpace(column.Header))
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{where}: the header is empty. Give the column a header.", this.at + ".header");
        }

        var at = this.at;
        this.at += ".header";
        this.CheckText(column.Header, where);
        this.CheckCharacters(column.Header, false, where);
        this.at = at;
        var decimals = column.Decimals;
        if (decimals is { } d)
        {
            if (type is not (XlsxColumnType.Number or XlsxColumnType.Currency or XlsxColumnType.Percent))
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: a {type} column has no decimals; only Number, Currency and Percent columns do.", this.at + ".decimals");
                decimals = null;
            }
            else if (d is < 0 or > 10)
            {
                this.Problem(NoteCodes.ColumnInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: {d} decimals; use 0 to 10."), this.at + ".decimals");
                decimals = null;
            }
        }

        var currency = column.Currency?.Trim() ?? (type == XlsxColumnType.Currency ? inferred : null);
        if (currency is not null)
        {
            if (type != XlsxColumnType.Currency)
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: the currency '{currency}' is set on a {type} column; only a Currency column has one.", this.at + ".currency");
                currency = null;
            }
            else if (column.Currency is null && !XlsxFormat.IsKnownCurrency(currency))
            {
                // Taken from a referred column, which reports it.
                currency = null;
            }
            else if (!XlsxFormat.IsKnownCurrency(currency))
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: '{currency}' is not an ISO 4217 currency code such as EUR or USD.", this.at + ".currency");
                currency = null;
            }
        }

        if (column.Width is { } width && (double.IsNaN(width) || width < 1 || width > 255))
        {
            this.Problem(NoteCodes.ColumnInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: the width {width} is outside 1–255 characters."), this.at + ".width");
        }

        if (column.Align is { } align && !Enum.IsDefined(align))
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{where}: the alignment {align} is not one of Left, Center, Right.", this.at + ".align");
        }

        if (!Enum.IsDefined(type))
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{where}: the type {type} is not a column type.", this.at + ".type");
        }

        if (column.Total is { } total)
        {
            if (!Enum.IsDefined(total))
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: the total {total} is not one of Sum, Average, Count, Min, Max.", this.at + ".total");
            }
            else if (type is XlsxColumnType.Text or XlsxColumnType.Boolean && total != XlsxTotal.Count)
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: a {type} column cannot have the total {total}; only Count. Give the column a numeric type, or count it.", this.at + ".total");
            }
        }

        var format = XlsxFormat.For(Enum.IsDefined(type) ? type : XlsxColumnType.Text, decimals, currency, this.Culture);
        var alignment = column.Align is { } a && Enum.IsDefined(a) ? a : type switch
        {
            XlsxColumnType.Text => XlsxAlign.Left,
            XlsxColumnType.Boolean => XlsxAlign.Center,
            _ => XlsxAlign.Right,
        };
        var columnWidth = column.Width is { } given and >= 1 and <= 255 ? given : measure ? this.Measure(column, format, index, rows) : MinWidth;
        return new ColumnPlan(column, index, format, alignment, columnWidth);
    }

    private void Cell(XlsxCell cell, ColumnPlan column, string where)
    {
        var type = column.Format.Type;
        switch (cell.Kind)
        {
            case XlsxCellKind.Empty or XlsxCellKind.Formula:
                return;
            case XlsxCellKind.Text:
                this.CheckText(cell.Text, where);
                this.CheckCharacters(cell.Text, false, where);
                if (type != XlsxColumnType.Text && !column.Column.KeepsText)
                {
                    this.Problem(NoteCodes.CellType, $"{where}: the text '{GenerationNotes.Quote(cell.Text!)}' is in a {type} column. Give a value of the column's type, or make it a Text column.");
                }

                return;
            case XlsxCellKind.Number:
                if (!double.IsFinite(cell.Number))
                {
                    this.Problem(NoteCodes.CellType, $"{where}: {cell.Number.ToString(CultureInfo.InvariantCulture)} is not a number Excel can hold.");
                }
                else if (type == XlsxColumnType.Integer && cell.Number != Math.Floor(cell.Number))
                {
                    this.Problem(NoteCodes.CellType, $"{where}: {cell} has decimals, but the column is Integer. Round it, or make it a Number column.");
                }
                else if (type is not (XlsxColumnType.Number or XlsxColumnType.Integer or XlsxColumnType.Currency or XlsxColumnType.Percent))
                {
                    this.Problem(NoteCodes.CellType, $"{where}: the number {cell} is in a {type} column. Give a value of the column's type, or change the column's type.");
                }

                return;
            case XlsxCellKind.Date:
            case XlsxCellKind.DateTime:
                if (cell.DateTime < FirstDate)
                {
                    this.Problem(NoteCodes.ExcelLimit, $"{where}: {cell} is before 1900, which Excel cannot hold as a date.");
                }
                else if (type == XlsxColumnType.Date && cell.DateTime.TimeOfDay != TimeSpan.Zero)
                {
                    this.Problem(NoteCodes.CellType, $"{where}: {cell} has a time, but the column is Date. Make it a DateTime column, or give the date alone.");
                }
                else if (type is not (XlsxColumnType.Date or XlsxColumnType.DateTime))
                {
                    this.Problem(NoteCodes.CellType, $"{where}: the date {cell} is in a {type} column. Give a value of the column's type, or make it a Date column.");
                }

                return;
            case XlsxCellKind.Boolean:
                if (type != XlsxColumnType.Boolean)
                {
                    this.Problem(NoteCodes.CellType, $"{where}: the value {cell} is in a {type} column. Give a value of the column's type, or make it a Boolean column.");
                }

                return;
        }
    }

    private void CheckText(string? text, string where)
    {
        if (text is { Length: > MaxText })
        {
            this.Problem(NoteCodes.ExcelLimit, string.Create(CultureInfo.InvariantCulture, $"{where}: the text has {text.Length:N0} characters; Excel holds {MaxText:N0} in a cell."));
        }
    }

    // Once per character, at its first place: Excel shows it in a substitute font.
    private void CheckCharacters(string? text, bool heading, string where)
    {
        if (string.IsNullOrEmpty(text) || this.Brand.Fonts is not { } fonts || this.missing.Count >= MaxProblems)
        {
            return;
        }

        var face = heading ? fonts.Bold : fonts.TableRegular;
        var family = heading ? fonts.Family : fonts.TableFamily;
        foreach (var rune in text.EnumerateRunes())
        {
            var c = rune.Value;
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || this.missing.Contains(c) || TrueTypeInfo.HasCharacter(face, c))
            {
                continue;
            }

            this.missing.Add(c);
            this.Note(
                NoteCodes.CharacterMissing,
                string.Create(CultureInfo.InvariantCulture, $"{where}: the font '{family}' lacks '{rune}' (U+{c:X4}), which Excel shows in a substitute font. Write it as a plain character, or leave it out."));
        }
    }

    // The width in characters of Excel's default font: the widest value or the header (bold, with the filter button), 6 to 50.
    private double Measure(XlsxColumn column, XlsxFormat format, int index, IReadOnlyList<IReadOnlyList<XlsxCell>> rows)
    {
        var widest = this.Characters(column.Header ?? string.Empty, true) + 2.5;
        foreach (var row in rows.Take(MeasuredRows))
        {
            if (row is not null && index < row.Count && row[index] is { Kind: XlsxCellKind.Formula })
            {
                // Its value is Excel's to compute: room for a sum in the thousands.
                widest = Math.Max(widest, this.Characters(format.Display(XlsxCell.FromNumber(123_456.78)), false));
            }
            else if (row is not null && index < row.Count && row[index] is { Kind: not XlsxCellKind.Empty } cell)
            {
                foreach (var line in format.Display(cell).Split('\n'))
                {
                    widest = Math.Max(widest, this.Characters(line, false));
                }
            }
        }

        return Math.Round(Math.Clamp(widest + 1, MinWidth, MaxWidth), 2);
    }

    // Text width in widths of the digit 0, Excel's unit of column width: in the brand's table face, else estimated.
    private double Characters(string text, bool bold)
    {
        if (this.Brand.Fonts is { } fonts && TrueTypeMetrics.For(bold ? fonts.TableBold : fonts.TableRegular) is { } metrics && metrics.Advance('0') is > 0 and var digit)
        {
            return metrics.Width(text) / digit;
        }

        return TextMetrics.Width(text, 1100, bold) / TextMetrics.Width("0", 1100, false);
    }

    /// <summary>The JSON path of the note of <paramref name="code"/> with <paramref name="message"/>, where the plan made it.</summary>
    public string? NotePath(string code, string message) => this.notePaths.GetValueOrDefault((code, message));

    // A problem at the JSON path `path`, or at the place the plan is at.
    private void Problem(string code, string message, string? path = null)
    {
        if (this.problems.Count < MaxProblems)
        {
            var note = new GenerationNote(code, 0, message);
            this.problems.Add(note);
            this.problemPaths.Add(path ?? this.at);
        }
    }

    // A note at the place the plan is at.
    private void Note(string code, string message, string? path = null)
    {
        this.notePaths.TryAdd((code, message), path ?? this.at);
        this.Notes.Add(code, 0, message);
    }
}

/// <summary>Where one sheet's parts go: its title block, the header row, the columns.</summary>
internal sealed record SheetPlan(XlsxSheet Sheet, int Index, string? Title, string? Subtitle, bool ShowsDate, int HeaderRow, IReadOnlyList<ColumnPlan> Columns)
{
    public int FirstDataRow => this.HeaderRow + 1;

    public int LastRow => this.HeaderRow + (this.Sheet.Rows?.Count ?? 0);

    /// <summary>The sheet's charts that passed their checks, in order; those on chart sheets with their sheet's name.</summary>
    public IReadOnlyList<ChartPlan> Charts { get; init; } = [];

    /// <summary>Each header, trimmed and ignoring case, to its column from 0; -1 for a header two columns share.</summary>
    public IReadOnlyDictionary<string, int> Lookup { get; init; } = new Dictionary<string, int>();

    /// <summary>Each column's total, <see langword="null"/> where it has none; the list is <see langword="null"/> without a totals row.</summary>
    public IReadOnlyList<TotalPlan?>? Totals { get; init; }

    /// <summary>The label of the totals row, in <see cref="LabelColumn"/>; <see langword="null"/> when every column has a total.</summary>
    public string? TotalsLabel { get; init; }

    public int LabelColumn { get; init; } = -1;

    /// <summary>The totals row, right under the table, or <see langword="null"/>.</summary>
    public int? TotalsRow => this.Totals is null ? null : this.LastRow + 1;

    /// <summary>The column letter of the header <paramref name="header"/>, for <see cref="XlsxFormula.Write"/>.</summary>
    public string? Letter(string header) => this.Lookup.TryGetValue(header, out var c) && c >= 0 ? XlsxPlan.ColumnName(c) : null;
}

/// <summary>
/// A column's total: the <c>SUBTOTAL</c> function number, the format, and the cached value, or the error value Excel
/// gives; neither for a total over formulas, which Excel computes.
/// </summary>
internal sealed record TotalPlan(int Function, XlsxFormat Format, double? Value, string? Error)
{
    /// <summary>The value as a cell to show, a date for a date format; <see langword="null"/> without one.</summary>
    public XlsxCell? Shown => this.Value is not { } value ? null
        : this.Format.Type is XlsxColumnType.Date or XlsxColumnType.DateTime && value is >= 1 and < 2_958_466 ? XlsxCell.FromDateTime(DateTime.FromOADate(value))
        : XlsxCell.FromNumber(value);
}

/// <summary>
/// A chart of a sheet: its number on the sheet from 1, what it draws (the values as the cache), the cells it refers to,
/// and the name of its chart sheet, <see langword="null"/> beside the table.
/// </summary>
internal sealed record ChartPlan(XlsxChart Chart, int Number, ChartSpec Spec, ChartCells Cells, string? SheetName);

/// <summary>One column's format, alignment and width.</summary>
internal sealed record ColumnPlan(XlsxColumn Column, int Index, XlsxFormat Format, XlsxAlign Align, double Width);

/// <summary>
/// The brand's colours as a sheet shows them: each darkened, where needed, until text keeps a 4.5:1 contrast on Excel's
/// white sheet (and white header text on the header fill), and a band light enough for dark text.
/// </summary>
internal sealed record SheetColors(string Title, string Muted, string Link, string Header, string Band, string Line)
{
    /// <summary>The fill of the totals row: the header's colour, lighter, with black text readable on it.</summary>
    public string Totals => ResolvedBrand.Readable(ResolvedBrand.Mix(this.Header, 0.3), Text, true, 7);

    public const string White = "FFFFFF";
    public const string Text = "000000";

    public static SheetColors Of(ResolvedBrand brand) => new(
        ResolvedBrand.Readable(brand.Primary, White, false),
        ResolvedBrand.Readable(brand.Footer, White, false),
        ResolvedBrand.Readable(brand.Link, White, false),
        ResolvedBrand.Readable(brand.TableHeader, White, false),
        ResolvedBrand.Readable(brand.TableBand, Text, true),
        brand.TableLine);
}
