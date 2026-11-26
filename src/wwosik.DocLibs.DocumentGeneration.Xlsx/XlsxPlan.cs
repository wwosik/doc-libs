using System.Globalization;
using System.Text;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// One workbook resolved for writing, checking and previewing: brand, language, culture, marking, where every sheet's
/// rows go, each column's format and width, and every problem found, which <see cref="XlsxGenerator.Generate"/> throws.
/// </summary>
internal sealed class XlsxPlan
{
    /// <summary>The most problems a check reports.</summary>
    public const int MaxProblems = 500;

    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public const int MaxText = 32_767;
    public const int MaxSheetName = 31;
    private const double MinWidth = 6;
    private const double MaxWidth = 50;

    // Rows measured for a column's width: enough for its look, cheap for a million rows.
    private const int MeasuredRows = 10_000;

    private static readonly DateTime FirstDate = new(1900, 1, 1);

    private readonly List<GenerationNote> problems = [];
    private readonly HashSet<int> missing = [];

    private XlsxPlan(XlsxWorkbook workbook, XlsxOptions options)
    {
        this.Workbook = workbook;
        this.Options = options;
    }

    public XlsxWorkbook Workbook { get; }

    public XlsxOptions Options { get; }

    public GenerationNotes Notes { get; } = new();

    public IReadOnlyList<GenerationNote> Problems => this.problems;

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
            text.Append(this.problems[0].Message);
        }
        else
        {
            foreach (var problem in this.problems.Take(10))
            {
                text.Append("\n- ").Append(problem.Message);
            }

            if (this.problems.Count > 10)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n- … and {this.problems.Count - 10} more.");
            }
        }

        return new DocumentConversionException(Format.Xlsx, text.ToString());
    }

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
            Sheets = this.Sheets.Count + (this.Info ? 1 : 0),
            Tables = this.Sheets.Count(s => s.Columns.Count > 0),
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

    private static int Words(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : DocumentCheck.Words(text);

    private void Resolve(bool measure)
    {
        var options = this.Options;
        var workbook = this.Workbook;
        var registry = BrandRegistry.Of(options.Brand, options.Brands);
        var brand = workbook.Brand is { } name
            ? registry.Find(name.Trim()) ?? throw new DocumentConversionException(Format.Xlsx, $"The workbook names the brand '{name}'; expected {registry.Expected}.")
            : options.Brand ?? DocumentGeneration.Brand.Neutral;
        this.Brand = ResolvedBrand.Resolve(brand);
        var language = DocumentLanguage.Resolve(workbook.Language, "en", options.Texts, this.Notes, Format.Xlsx);
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
                this.Problem(NoteCodes.ExcelLimit, "The workbook has no sheet, and Excel needs at least one. Add a sheet, or a title for the info sheet.");
            }

            this.Sheets = [];
        }
        else
        {
            this.CheckNames(workbook.Sheets);
            this.Sheets = [.. workbook.Sheets.Select((s, i) => this.Sheet(s, i, measure))];
        }

        if (this.Info)
        {
            this.InfoName = this.FreeName(this.Texts.Info);
            this.CheckCharacters(workbook.Title, true, "the workbook's title");
            this.CheckCharacters(workbook.Subtitle, true, "the workbook's subtitle");
            this.CheckCharacters(workbook.Description, true, "the workbook's description");
            this.CheckCharacters(workbook.Author, true, "the workbook's author");
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
                this.Notes.Add(
                    NoteCodes.ClassificationKept,
                    0,
                    $"The workbook asks for the classification '{own.Classification.ToString().ToLowerInvariant()}', but the host marks it '{host.Classification.ToString().ToLowerInvariant()}', which is kept: the workbook can raise the classification, not lower it.");
            }

            classification = own.Classification > classification ? own.Classification : classification;
        }

        return new Marking(classification, host.Draft || own?.Draft == true);
    }

    private string FreeName(string name)
    {
        var taken = this.Sheets.Select(s => s.Sheet.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = name.Length > MaxSheetName ? name[..MaxSheetName] : name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            var suffix = string.Create(CultureInfo.InvariantCulture, $" {n}");
            candidate = (name.Length + suffix.Length > MaxSheetName ? name[..(MaxSheetName - suffix.Length)] : name) + suffix;
        }

        return candidate;
    }

    private void CheckNames(IReadOnlyList<XlsxSheet> sheets)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sheets.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(sheets[i], nameof(XlsxWorkbook.Sheets));
            var name = sheets[i].Name ?? string.Empty;
            var where = string.Create(CultureInfo.InvariantCulture, $"Sheet {i + 1} ('{name}')");
            if (name.Trim().Length == 0)
            {
                this.Problem(NoteCodes.SheetName, $"{where}: the name is empty. Give the sheet a name.");
            }
            else if (name.Length > MaxSheetName)
            {
                this.Problem(NoteCodes.SheetName, string.Create(CultureInfo.InvariantCulture, $"{where}: the name has {name.Length} characters; Excel allows {MaxSheetName}."));
            }

            if (name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) is var bad and >= 0)
            {
                this.Problem(NoteCodes.SheetName, $"{where}: the name holds '{name[bad]}'; Excel allows none of []:*?/\\ in a sheet name.");
            }

            if (name.StartsWith('\'') || name.EndsWith('\''))
            {
                this.Problem(NoteCodes.SheetName, $"{where}: the name starts or ends with an apostrophe, which Excel does not allow.");
            }

            if (name.Any(char.IsControl))
            {
                this.Problem(NoteCodes.SheetName, $"{where}: the name holds a control character.");
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
        this.CheckCharacters(title, true, $"{label}, its title");
        this.CheckCharacters(subtitle, true, $"{label}, its description");
        this.CheckText(title, $"{label}, its title");
        this.CheckText(subtitle, $"{label}, its description");

        if (columns.Count == 0)
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{label} has no columns. Give it at least one.");
        }

        if (columns.Count > MaxColumns)
        {
            this.Problem(NoteCodes.ExcelLimit, string.Create(CultureInfo.InvariantCulture, $"{label} has {columns.Count:N0} columns; Excel allows {MaxColumns:N0}."));
        }

        if (headerRow + rows.Count > MaxRows)
        {
            this.Problem(NoteCodes.ExcelLimit, string.Create(CultureInfo.InvariantCulture, $"{label} has {rows.Count:N0} rows below row {headerRow}; Excel allows {MaxRows:N0} rows in all. Split the data over sheets."));
        }

        var plans = new List<ColumnPlan>(columns.Count);
        for (var c = 0; c < columns.Count; c++)
        {
            plans.Add(this.Column(columns[c], c, label, headerRow, rows, measure));
        }

        for (var r = 0; r < rows.Count && this.problems.Count < MaxProblems; r++)
        {
            var row = rows[r] ?? [];
            var excelRow = headerRow + 1 + r;
            if (row.Count > columns.Count)
            {
                this.Problem(NoteCodes.RowTooWide, string.Create(CultureInfo.InvariantCulture, $"{label}, row {r + 1} ({ColumnName(columns.Count)}{excelRow}): the row has {row.Count} cells, but the sheet has {columns.Count} columns."));
            }

            for (var c = 0; c < Math.Min(row.Count, columns.Count); c++)
            {
                this.Cell(row[c] ?? XlsxCell.Empty, plans[c], string.Create(CultureInfo.InvariantCulture, $"{label}, row {r + 1}, column '{plans[c].Column.Header}' ({ColumnName(c)}{excelRow})"));
            }
        }

        return new SheetPlan(sheet, index, title, subtitle, showsDate, headerRow, plans);
    }

    private ColumnPlan Column(XlsxColumn? column, int index, string label, int headerRow, IReadOnlyList<IReadOnlyList<XlsxCell>> rows, bool measure)
    {
        column ??= new XlsxColumn(string.Empty);
        var where = string.Create(CultureInfo.InvariantCulture, $"{label}, column {index + 1} ('{column.Header}', {ColumnName(index)}{headerRow})");
        if (string.IsNullOrWhiteSpace(column.Header))
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{where}: the header is empty. Give the column a header.");
        }

        this.CheckText(column.Header, where);
        this.CheckCharacters(column.Header, false, where);
        var decimals = column.Decimals;
        if (decimals is { } d)
        {
            if (column.Type is not (XlsxColumnType.Number or XlsxColumnType.Currency or XlsxColumnType.Percent))
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: a {column.Type} column has no decimals; only Number, Currency and Percent columns do.");
                decimals = null;
            }
            else if (d is < 0 or > 10)
            {
                this.Problem(NoteCodes.ColumnInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: {d} decimals; use 0 to 10."));
                decimals = null;
            }
        }

        var currency = column.Currency?.Trim();
        if (currency is not null)
        {
            if (column.Type != XlsxColumnType.Currency)
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: the currency '{currency}' is set on a {column.Type} column; only a Currency column has one.");
                currency = null;
            }
            else if (!XlsxFormat.IsKnownCurrency(currency))
            {
                this.Problem(NoteCodes.ColumnInvalid, $"{where}: '{currency}' is not an ISO 4217 currency code such as EUR or USD.");
                currency = null;
            }
        }

        if (column.Width is { } width && (double.IsNaN(width) || width < 1 || width > 255))
        {
            this.Problem(NoteCodes.ColumnInvalid, string.Create(CultureInfo.InvariantCulture, $"{where}: the width {width} is outside 1–255 characters."));
        }

        if (column.Align is { } align && !Enum.IsDefined(align))
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{where}: the alignment {align} is not one of Left, Center, Right.");
        }

        if (!Enum.IsDefined(column.Type))
        {
            this.Problem(NoteCodes.ColumnInvalid, $"{where}: the type {column.Type} is not a column type.");
        }

        var format = XlsxFormat.For(Enum.IsDefined(column.Type) ? column.Type : XlsxColumnType.Text, decimals, currency, this.Culture);
        var alignment = column.Align is { } a && Enum.IsDefined(a) ? a : column.Type switch
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
            case XlsxCellKind.Empty:
                return;
            case XlsxCellKind.Text:
                this.CheckText(cell.Text, where);
                this.CheckCharacters(cell.Text, false, where);
                if (type != XlsxColumnType.Text)
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
            this.Notes.Add(
                NoteCodes.CharacterMissing,
                0,
                string.Create(CultureInfo.InvariantCulture, $"{where}: the font '{family}' lacks '{rune}' (U+{c:X4}), which Excel shows in a substitute font. Write it as a plain character, or leave it out."));
        }
    }

    // The width in characters of Excel's default font: the widest value or the header (bold, with the filter button), 6 to 50.
    private double Measure(XlsxColumn column, XlsxFormat format, int index, IReadOnlyList<IReadOnlyList<XlsxCell>> rows)
    {
        var widest = this.Characters(column.Header ?? string.Empty, true) + 2.5;
        foreach (var row in rows.Take(MeasuredRows))
        {
            if (row is not null && index < row.Count && row[index] is { Kind: not XlsxCellKind.Empty } cell)
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

    private void Problem(string code, string message)
    {
        if (this.problems.Count < MaxProblems)
        {
            var note = new GenerationNote(code, 0, message);
            this.problems.Add(note);
        }
    }
}

/// <summary>Where one sheet's parts go: its title block, the header row, the columns.</summary>
internal sealed record SheetPlan(XlsxSheet Sheet, int Index, string? Title, string? Subtitle, bool ShowsDate, int HeaderRow, IReadOnlyList<ColumnPlan> Columns)
{
    public int FirstDataRow => this.HeaderRow + 1;

    public int LastRow => this.HeaderRow + (this.Sheet.Rows?.Count ?? 0);
}

/// <summary>One column's format, alignment and width.</summary>
internal sealed record ColumnPlan(XlsxColumn Column, int Index, XlsxFormat Format, XlsxAlign Align, double Width);

/// <summary>
/// The brand's colours as a sheet shows them: each darkened, where needed, until text keeps a 4.5:1 contrast on Excel's
/// white sheet (and white header text on the header fill), and a band light enough for dark text.
/// </summary>
internal sealed record SheetColors(string Title, string Muted, string Link, string Header, string Band, string Line)
{
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
