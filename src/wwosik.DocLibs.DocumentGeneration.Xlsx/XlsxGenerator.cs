using System.Globalization;
using System.Net;
using System.Text;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// Writes Excel workbooks (<c>.xlsx</c>) from an <see cref="XlsxWorkbook"/> in a brand's look: an info sheet first, then
/// one table per sheet with typed values in the culture's number formats, a frozen and filtered header row, banding that
/// survives sorting, and print setup. <see cref="Check"/> reports every problem at once; <see cref="Preview"/> shows the
/// workbook as plain HTML.
/// </summary>
public static class XlsxGenerator
{
    /// <summary>
    /// Writes <paramref name="workbook"/> as an <c>.xlsx</c> to <paramref name="output"/>. Returns the notes for whoever
    /// made the workbook (a character the fonts lack, a language without texts) and in <see cref="GenerationResult.Pages"/>
    /// the number of sheets, the info sheet included. The same workbook, options and clock give the same bytes.
    /// </summary>
    /// <exception cref="DocumentConversionException">
    /// The workbook has problems (a value that does not fit its column, an invalid sheet name or column, a row wider than
    /// the columns, something past Excel's limits; the message lists the first ten, <see cref="Check"/> all of them), names
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
        return plan.Notes.Result(plan.Sheets.Count + (plan.Info ? 1 : 0));
    }

    /// <summary>
    /// Checks <paramref name="workbook"/> without writing it: every problem <see cref="Generate"/> would throw for, each
    /// naming its sheet, row, column and cell (<c>B7</c>), at most 500, then the notes <see cref="Generate"/> would return;
    /// and statistics: sheets, tables, charts (<see cref="CheckStatistics.Diagrams"/>) and words. With problems,
    /// <see cref="CheckResult.Error"/> is the message <see cref="Generate"/> would throw.
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
    /// scripts, and every text HTML-encoded. Problems that <see cref="Generate"/> throws for are not checked: a value is
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
            foreach (var row in rows.Take(maxRows))
            {
                html.Append("<tr>");
                foreach (var column in sheet.Columns)
                {
                    var cell = row is not null && column.Index < row.Count ? row[column.Index] ?? XlsxCell.Empty : XlsxCell.Empty;
                    html.Append(column.Format.IsNumeric ? "<td class=\"num\">" : "<td>").Append(Encode(column.Format.Display(cell))).Append("</td>");
                }

                html.Append("</tr>\n");
            }

            html.Append("</tbody>\n");
            if (rows.Count > maxRows)
            {
                html.Append(string.Create(CultureInfo.InvariantCulture, $"<tfoot>\n<tr><td colspan=\"{Math.Max(1, sheet.Columns.Count)}\">… +{rows.Count - maxRows}</td></tr>\n</tfoot>\n"));
            }

            html.Append("</table>\n</section>\n");
        }

        return html.ToString();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal);
}
