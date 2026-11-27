using System.Text;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>The authoring guide of workbooks: the template <c>AuthoringGuide/Xlsx.md</c> filled for the options.</summary>
internal static class XlsxGuide
{
    /// <summary>The codes that stop a workbook from being written; every other code of workbooks is a note.</summary>
    public static readonly IReadOnlySet<string> Errors = new HashSet<string>(StringComparer.Ordinal)
    {
        NoteCodes.CellType, NoteCodes.SheetName, NoteCodes.ColumnInvalid, NoteCodes.RowTooWide, NoteCodes.ExcelLimit,
        NoteCodes.FormulaInvalid, NoteCodes.ChartInvalid,
    };

    /// <exception cref="ArgumentException">A brand is invalid, or two share a name.</exception>
    public static string Write(XlsxOptions options)
    {
        var registry = BrandRegistry.Of(options.Brand, options.Brands);
        return AuthoringGuides.Fill(
            AuthoringGuides.Template(typeof(XlsxGuide).Assembly, "Xlsx"),
            new Dictionary<string, string>
            {
                ["frontMatter"] = FrontMatter(options, registry.Expected),
                ["versions"] = AuthoringGuides.Versions(options.Audience),
                ["errors"] = Codes(error: true),
                ["notes"] = Codes(error: false),
            });
    }

    private static string Codes(bool error) => string.Join("\n", NoteCodes.All
        .Where(n => n.Formats.Contains(Format.Xlsx) && Errors.Contains(n.Code) == error)
        .Select(n => $"- `{n.Code}`: {n.Advice}"));

    private static string FrontMatter(XlsxOptions options, string templates)
    {
        var classification = (options.Marking?.Classification ?? Classification.Public).ToString().ToLowerInvariant();
        var text = new StringBuilder("""
            ## Front matter

            Settings go in a block at the very top, before anything else. One `key: value` per line. Every
            key is optional.

            ```markdown
            ---
            title: Quarterly figures
            lang: de
            ---
            ```

            | Key | Values |
            | --- | ------ |
            | `title` | The title on the info sheet, in the page header and the file's properties. Default: the first `#` heading. |
            | `subtitle` | A line under the title on the info sheet. |
            | `author` | The author on the info sheet and in the file's properties. |
            | `description` | What the workbook holds, when there is no `#` with paragraphs. |
            | `keywords` | Accepted; a workbook has no keywords. |
            | `date` | The date shown, such as `2026-03-09`. Default: today. |

            """.ReplaceLineEndings("\n"));
        text.Append(FormattableString.Invariant($"| `lang` | The language, such as `en`, `de` or `de-CH`: of the values in the tables, the number and date formats, and the texts the generator writes. Default `en`. |\n"));
        text.Append($"| `template` | The design: {templates.Replace("'", "`", StringComparison.Ordinal)}. |\n");
        text.Append($"| `info` | `true` or `false`: the info sheet. Default: {(options.Info is { } info ? $"`{(info ? "true" : "false")}`" : "when there is a title or a description")}. |\n");
        text.Append($"| `showDate` | `true` or `false`: the date on the info sheet and above every table. Default `{(options.ShowDate ? "true" : "false")}`. |\n");
        text.Append($"| `classification` | `public`, `internal` or `confidential`: a marking on the info sheet and in the printed page header. Default `{classification}`; a lower value has no effect. |\n");
        text.Append("| `draft` | `true` adds \"Draft\" to the marking. Default `false`. |\n");
        text.Append(options.Audience is { } fixedVersion
            ? FormattableString.Invariant($"| `audience` | No effect here: the host writes the `{fixedVersion.ToString().ToLowerInvariant()}` version. |\n")
            : "| `audience` | `customer`, `internal` or `trainer`: the version, see *Versions*. Default `customer`. |\n");
        text.Append($"| `{string.Join("`, `", XlsxMarkdown.OtherFormatKeys)}`, `typography`, `highlight` | Settings of decks and documents, accepted and ignored. |\n");
        text.Append("\nAn unknown key, a key given twice or an invalid value is an error that lists the valid keys.\n");
        return text.ToString();
    }
}
