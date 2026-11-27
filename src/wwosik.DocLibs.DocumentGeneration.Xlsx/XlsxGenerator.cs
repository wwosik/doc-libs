using System.Globalization;
using System.Net;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// Writes Excel workbooks (<c>.xlsx</c>) from an <see cref="XlsxWorkbook"/> in a brand's look: an info sheet first, then
/// one table per sheet with typed values in the culture's number formats, a frozen and filtered header row, banding that
/// survives sorting, totals that follow the filter, formulas, native charts on the tables' cells, and print setup. <see cref="Check(XlsxWorkbook, XlsxOptions?)"/> reports every problem at once; <see cref="Preview(XlsxWorkbook, XlsxOptions?, int)"/> shows the
/// workbook as plain HTML. The same from Markdown: each <c>##</c> a sheet, its first table the data.
/// </summary>
public static class XlsxGenerator
{
    /// <summary>
    /// Writes <paramref name="workbook"/> as an <c>.xlsx</c> to <paramref name="output"/>. Returns the notes for whoever
    /// made the workbook (a character the fonts lack, a language without texts) and in <see cref="GenerationResult.Pages"/>
    /// the number of sheets, the info sheet and chart sheets included. The same workbook, options and clock give the same bytes.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// The workbook has problems (a value that does not fit its column, an invalid sheet name or column, a row wider than
    /// the columns, a formula that does not parse, names an unknown column or refers to itself, a chart that names an unknown or text column, something past Excel's limits; the message lists the first ten, <see cref="Check(XlsxWorkbook, XlsxOptions?)"/> all of them), names
    /// an unknown brand, or its language is not a language tag.
    /// </exception>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static GenerationResult Generate(XlsxWorkbook workbook, Stream output, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(output);
        var plan = XlsxPlan.Of(workbook, options);
        if (plan.Problems.Count > 0)
        {
            throw plan.Failure();
        }

        XlsxWriter.Write(plan, output);
        return plan.Notes.Result(plan.Statistics().Sheets);
    }

    /// <summary>
    /// Checks <paramref name="workbook"/> without writing it: every problem <see cref="Generate(XlsxWorkbook, Stream, XlsxOptions?)"/> would throw for, each
    /// naming its sheet, row, column and cell (<c>B7</c>), at most 500, then the notes <see cref="Generate(XlsxWorkbook, Stream, XlsxOptions?)"/> would return;
    /// and statistics: sheets, tables, charts (<see cref="CheckStatistics.Diagrams"/>) and words. With problems,
    /// <see cref="CheckResult.Error"/> is the message <see cref="Generate(XlsxWorkbook, Stream, XlsxOptions?)"/> would throw.
    /// </summary>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static CheckResult Check(XlsxWorkbook workbook, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        XlsxPlan plan;
        try
        {
            plan = XlsxPlan.Of(workbook, options, measure: false);
        }
        catch (DocumentConversionException ex)
        {
            return new CheckResult([], new CheckStatistics()) { Error = ex.Reason };
        }

        var notes = plan.Notes.Result(0).Notes;
        return new CheckResult([.. plan.Problems, .. notes.Take(Math.Max(0, XlsxPlan.MaxProblems - plan.Problems.Count))], plan.Statistics())
        {
            Error = plan.Problems.Count > 0 ? plan.Failure().Reason : null,
        };
    }

    /// <summary>
    /// The workbook as HTML for a host to embed: one <c>section</c> per sheet (the info sheet first, when there is one),
    /// each with a heading and a table of at most <paramref name="maxRows"/> rows. Values are shown as Excel shows them in
    /// the workbook's culture; numbers are right-aligned through the class <c>num</c> on their <c>td</c>. No styles, no
    /// scripts, and every text HTML-encoded. Problems that <see cref="Generate(XlsxWorkbook, Stream, XlsxOptions?)"/> throws for are not checked: a value is
    /// then shown as it is.
    /// </summary>
    /// <exception cref="DocumentConversionException">The workbook names an unknown brand, or its language is not a language tag.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRows"/> is negative.</exception>
    public static string Preview(XlsxWorkbook workbook, XlsxOptions? options = null, int maxRows = 200)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRows);
        var plan = XlsxPlan.Of(workbook, options, measure: false);
        var html = new StringBuilder();
        if (plan.Info)
        {
            html.Append("<section>\n<h2>").Append(Encode(workbook.Title ?? plan.InfoName)).Append("</h2>\n");
            foreach (var line in new[] { workbook.Subtitle, plan.MarkingText, workbook.Description })
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    html.Append("<p>").Append(Encode(line)).Append("</p>\n");
                }
            }

            var facts = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(workbook.Author))
            {
                facts.Add((plan.Texts.Author, workbook.Author));
            }

            if (plan.Date is { } date)
            {
                facts.Add((plan.Texts.Date, date.ToString(plan.Culture.DateTimeFormat.ShortDatePattern, plan.Culture)));
            }

            facts.Add((plan.Texts.Language, plan.LanguageTag));
            html.Append("<table>\n<tbody>\n");
            foreach (var (label, value) in facts)
            {
                html.Append("<tr><th>").Append(Encode(label)).Append("</th><td>").Append(Encode(value)).Append("</td></tr>\n");
            }

            html.Append("</tbody>\n</table>\n</section>\n");
        }

        foreach (var sheet in plan.Sheets)
        {
            html.Append("<section>\n<h2>").Append(Encode(sheet.Title ?? sheet.Sheet.Name)).Append("</h2>\n");
            if (sheet.Subtitle is { } subtitle)
            {
                html.Append("<p>").Append(Encode(subtitle)).Append("</p>\n");
            }

            html.Append("<table>\n<thead>\n<tr>");
            foreach (var column in sheet.Columns)
            {
                html.Append(column.Format.IsNumeric ? "<th class=\"num\">" : "<th>").Append(Encode(column.Column.Header ?? string.Empty)).Append("</th>");
            }

            html.Append("</tr>\n</thead>\n<tbody>\n");
            var rows = sheet.Sheet.Rows ?? [];
            var n = sheet.FirstDataRow;
            foreach (var row in rows.Take(maxRows))
            {
                html.Append("<tr>");
                foreach (var column in sheet.Columns)
                {
                    var cell = row is not null && column.Index < row.Count ? row[column.Index] ?? XlsxCell.Empty : XlsxCell.Empty;
                    html.Append(column.Format.IsNumeric ? "<td class=\"num\">" : "<td>");
                    if (cell.Kind == XlsxCellKind.Formula)
                    {
                        html.Append("<code>").Append(Encode(Formula(plan, sheet, cell.Text!, n))).Append("</code>");
                    }
                    else
                    {
                        html.Append(Encode(column.Format.Display(cell)));
                    }

                    html.Append("</td>");
                }

                html.Append("</tr>\n");
                n++;
            }

            html.Append("</tbody>\n");
            if (rows.Count > maxRows || sheet.Totals is not null)
            {
                html.Append("<tfoot>\n");
                if (rows.Count > maxRows)
                {
                    html.Append(string.Create(CultureInfo.InvariantCulture, $"<tr><td colspan=\"{Math.Max(1, sheet.Columns.Count)}\">… +{rows.Count - maxRows}</td></tr>\n"));
                }

                if (sheet.Totals is { } totals)
                {
                    html.Append("<tr>");
                    foreach (var column in sheet.Columns)
                    {
                        var total = totals[column.Index];
                        html.Append((total?.Format ?? (column.Index == sheet.LabelColumn ? null : column.Format))?.IsNumeric == true ? "<td class=\"num\">" : "<td>");
                        if (total is { Shown: { } shown })
                        {
                            html.Append(Encode(total.Format.Display(shown)));
                        }
                        else if (total is { Error: { } error })
                        {
                            html.Append(Encode(error));
                        }
                        else if (total is not null)
                        {
                            var letter = XlsxPlan.ColumnName(column.Index);
                            html.Append(string.Create(CultureInfo.InvariantCulture, $"<code>=SUBTOTAL({total.Function},{letter}{sheet.FirstDataRow}:{letter}{sheet.LastRow})</code>"));
                        }
                        else if (column.Index == sheet.LabelColumn && sheet.TotalsLabel is { } label)
                        {
                            html.Append(Encode(label));
                        }

                        html.Append("</td>");
                    }

                    html.Append("</tr>\n");
                }

                html.Append("</tfoot>\n");
            }

            html.Append("</table>\n");
            foreach (var chart in sheet.Charts.Where(c => c.SheetName is null))
            {
                html.Append("<p>").Append(Encode(Chart(plan, chart))).Append("</p>\n");
            }

            html.Append("</section>\n");
            foreach (var chart in sheet.Charts.Where(c => c.SheetName is not null))
            {
                html.Append("<section>\n<h2>").Append(Encode(chart.SheetName!)).Append("</h2>\n<p>").Append(Encode(Chart(plan, chart))).Append("</p>\n</section>\n");
            }
        }

        return html.ToString();
    }

    /// <summary>
    /// Writes the workbook <paramref name="document"/> describes as an <c>.xlsx</c>: a leading <c>#</c> is the title and
    /// its paragraphs the info sheet's description, each <c>##</c> a sheet whose first table is the data, a <c>chart</c>
    /// block a chart (see <see cref="AuthoringGuide"/>). Front matter overrides the options. Returns the notes, among them
    /// <c>column-mixed</c>, <c>sheet-split</c> and <c>content-ignored</c> with their lines, and the number of sheets.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// The front matter is invalid or names an unknown brand, the language is not a language tag, or the workbook has
    /// problems (an unknown header attribute, a value that does not fit a declared type, a chart block that is malformed,
    /// and everything <see cref="Generate(XlsxWorkbook, Stream, XlsxOptions?)"/> throws for); the message lists the first ten with their lines.
    /// </exception>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static GenerationResult Generate(MarkdownDocument document, Stream output, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);
        var plan = Plan(document, options, measure: true);
        if (plan.Problems.Count > 0)
        {
            throw plan.Failure();
        }

        XlsxWriter.Write(plan, output);
        return plan.Notes.Result(plan.Statistics().Sheets);
    }

    /// <summary>
    /// Checks the workbook <paramref name="document"/> describes without writing it: every problem, with its Markdown line
    /// where it has one, then the notes, and the statistics, as <see cref="Check(XlsxWorkbook, XlsxOptions?)"/> does. An
    /// error in the front matter is <see cref="CheckResult.Error"/>, not thrown.
    /// </summary>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static CheckResult Check(MarkdownDocument document, XlsxOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        XlsxPlan plan;
        try
        {
            plan = Plan(document, options, measure: false);
        }
        catch (DocumentConversionException ex)
        {
            return new CheckResult([], new CheckStatistics()) { Error = ex.Reason };
        }

        var notes = plan.Notes.Result(0).Notes;
        return new CheckResult([.. plan.Problems, .. notes.Take(Math.Max(0, XlsxPlan.MaxProblems - plan.Problems.Count))], plan.Statistics())
        {
            Error = plan.Problems.Count > 0 ? plan.Failure().Reason : null,
        };
    }

    /// <summary>The workbook <paramref name="document"/> describes as HTML, as <see cref="Preview(XlsxWorkbook, XlsxOptions?, int)"/> shows it.</summary>
    /// <exception cref="DocumentConversionException">The front matter is invalid or names an unknown brand, or the language is not a language tag.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRows"/> is negative.</exception>
    public static string Preview(MarkdownDocument document, XlsxOptions? options = null, int maxRows = 200)
    {
        ArgumentNullException.ThrowIfNull(document);
        var read = XlsxMarkdown.Read(document, options ?? XlsxOptions.Default);
        return Preview(read.Workbook, read.Options, maxRows);
    }

    /// <summary>
    /// This generator bound to <paramref name="options"/>, as an <see cref="IMarkdownGenerator"/> (format <c>xlsx</c>) for
    /// <see cref="DocumentTool"/>: its <c>Generate</c>, <c>Check</c> and <c>AuthoringGuide</c> are this class's Markdown
    /// methods with these options. The options are not checked until one of them is called.
    /// </summary>
    public static IMarkdownGenerator ForOptions(XlsxOptions? options = null) => new Bound(options ?? XlsxOptions.Default);

    /// <summary>
    /// The authoring guide for whoever writes the Markdown of a workbook (often an AI agent): the structure, header-cell
    /// attributes, type inference, totals, formulas, charts, front matter, versions and notes. Markdown starting at
    /// <c>##</c>, in English. Pass the same options as to <see cref="Generate(MarkdownDocument, Stream, XlsxOptions?)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A brand is invalid, or two of <see cref="XlsxOptions.Brands"/> share a name.</exception>
    public static string AuthoringGuide(XlsxOptions? options = null) => XlsxGuide.Write(options ?? XlsxOptions.Default);

    // The plan of a Markdown workbook: the Markdown's problems first, its notes with the plan's.
    private static XlsxPlan Plan(MarkdownDocument document, XlsxOptions? options, bool measure)
    {
        var read = XlsxMarkdown.Read(document, options ?? XlsxOptions.Default);
        var plan = XlsxPlan.Of(read.Workbook, read.Options, measure);
        plan.Prepend(read.Problems);
        foreach (var note in read.Notes.Result(0).Notes)
        {
            plan.Notes.Add(note.Code, note.Line, note.Message);
        }

        return plan;
    }

    // A chart as one line: "Chart – Column – Orders by month – Plan, Actual". Charts are not drawn.
    private static string Chart(XlsxPlan plan, ChartPlan chart) =>
        string.Join(" – ", new[] { plan.Texts.Chart, chart.Chart.Kind.ToString(), chart.Spec.Title, string.Join(", ", chart.Spec.Series.Select(s => s.Name)) }.Where(t => !string.IsNullOrEmpty(t)));

    // A formula as Excel shows it in the formula bar, for one row; as given when it does not parse.
    private static string Formula(XlsxPlan plan, SheetPlan sheet, string source, int row)
    {
        var formula = plan.Formula(source);
        return "=" + (formula.Error is null ? formula.Write(row, sheet.Letter, prefixes: false) : source);
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal);

    private sealed class Bound(XlsxOptions options) : IMarkdownGenerator
    {
        public string Format => "xlsx";

        public string Title => "Excel workbook";

        public string FileExtension => ".xlsx";

        public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public string AuthoringGuide() => XlsxGenerator.AuthoringGuide(options);

        public GenerationResult Generate(MarkdownDocument document, Stream output) => XlsxGenerator.Generate(document, output, options);

        public CheckResult Check(MarkdownDocument document) => XlsxGenerator.Check(document, options);

        public IMarkdownGenerator ForAudience(Audience audience) => options.Audience is null ? new Bound(options with { Audience = audience }) : this;

        public override string ToString() => $"{this.Title} ({this.Format})";
    }
}
