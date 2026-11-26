using System.Globalization;
using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>A font of the workbook, by name: Excel finds it on the reader's machine.</summary>
internal sealed record XlsxFont(string Name, double Size, bool Bold = false, bool Underline = false, string? Color = null);

/// <summary>A cell style: font, fill, border, number format and alignment.</summary>
internal sealed record XlsxStyle(XlsxFont Font, string? Fill = null, bool Border = false, string? NumberFormat = null, XlsxAlign? Align = null, bool Wrap = false, bool Top = false);

/// <summary>
/// The styles of one workbook (<c>xl/styles.xml</c>): every font, fill, number format and cell format in the order first
/// asked for, so that the same workbook gives the same part.
/// </summary>
internal sealed class XlsxStyles
{
    private const int FirstCustomFormat = 164;
    private const int TextFormat = 49;

    private readonly List<XlsxFont> fonts = [];
    private readonly List<string> fills = [];
    private readonly List<string> formats = [];
    private readonly List<XlsxStyle> styles = [];
    private readonly List<string> bands = [];
    private readonly string line;

    public XlsxStyles(XlsxFont normal, string line)
    {
        this.line = line;
        this.fonts.Add(normal);
        this.styles.Add(new XlsxStyle(normal));
    }

    /// <summary>The index of <paramref name="style"/> in <c>cellXfs</c>.</summary>
    public int Of(XlsxStyle style)
    {
        var index = this.styles.IndexOf(style);
        if (index < 0)
        {
            Add(this.fonts, style.Font);
            if (style.Fill is { } fill)
            {
                Add(this.fills, fill);
            }

            if (style.NumberFormat is { } code and not XlsxFormat.TextCode)
            {
                Add(this.formats, code);
            }

            this.styles.Add(style);
            index = this.styles.Count - 1;
        }

        return index;
    }

    /// <summary>The index of a differential format filling the cell with <paramref name="color"/>, for banding.</summary>
    public int Band(string color) => Add(this.bands, color);

    public string Xml()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (this.formats.Count > 0)
        {
            xml.Append(Invariant($"<numFmts count=\"{this.formats.Count}\">"));
            for (var i = 0; i < this.formats.Count; i++)
            {
                xml.Append(Invariant($"<numFmt numFmtId=\"{FirstCustomFormat + i}\" formatCode=\"{XlsxXml.Attribute(this.formats[i])}\"/>"));
            }

            xml.Append("</numFmts>");
        }

        xml.Append(Invariant($"<fonts count=\"{this.fonts.Count}\">"));
        foreach (var font in this.fonts)
        {
            xml.Append("<font>")
                .Append(font.Bold ? "<b/>" : string.Empty)
                .Append(font.Underline ? "<u/>" : string.Empty)
                .Append(Invariant($"<sz val=\"{font.Size}\"/>"))
                .Append(font.Color is { } color ? $"<color rgb=\"FF{color}\"/>" : string.Empty)
                .Append($"<name val=\"{XlsxXml.Attribute(font.Name)}\"/><family val=\"2\"/></font>");
        }

        xml.Append(Invariant($"</fonts><fills count=\"{this.fills.Count + 2}\">"))
            .Append("<fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
        foreach (var fill in this.fills)
        {
            xml.Append($"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF{fill}\"/><bgColor indexed=\"64\"/></patternFill></fill>");
        }

        var side = $" style=\"thin\"><color rgb=\"FF{this.line}\"/>";
        xml.Append("</fills><borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>")
            .Append($"<border><left{side}</left><right{side}</right><top{side}</top><bottom{side}</bottom><diagonal/></border></borders>")
            .Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>")
            .Append(Invariant($"<cellXfs count=\"{this.styles.Count}\">"));
        foreach (var style in this.styles)
        {
            var format = style.NumberFormat switch
            {
                null => 0,
                XlsxFormat.TextCode => TextFormat,
                var code => FirstCustomFormat + this.formats.IndexOf(code),
            };
            var fill = style.Fill is { } f ? this.fills.IndexOf(f) + 2 : 0;
            xml.Append(Invariant($"<xf numFmtId=\"{format}\" fontId=\"{this.fonts.IndexOf(style.Font)}\" fillId=\"{fill}\" borderId=\"{(style.Border ? 1 : 0)}\" xfId=\"0\""))
                .Append(format != 0 ? " applyNumberFormat=\"1\"" : string.Empty)
                .Append(" applyFont=\"1\"")
                .Append(fill != 0 ? " applyFill=\"1\"" : string.Empty)
                .Append(style.Border ? " applyBorder=\"1\"" : string.Empty);
            if (style.Align is not null || style.Wrap || style.Top)
            {
                xml.Append(" applyAlignment=\"1\"><alignment")
                    .Append(style.Align switch
                    {
                        XlsxAlign.Left => " horizontal=\"left\"",
                        XlsxAlign.Center => " horizontal=\"center\"",
                        XlsxAlign.Right => " horizontal=\"right\"",
                        _ => string.Empty,
                    })
                    .Append(style.Top ? " vertical=\"top\"" : string.Empty)
                    .Append(style.Wrap ? " wrapText=\"1\"" : string.Empty)
                    .Append("/></xf>");
            }
            else
            {
                xml.Append("/>");
            }
        }

        xml.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>")
            .Append(Invariant($"<dxfs count=\"{this.bands.Count}\">"));
        foreach (var band in this.bands)
        {
            xml.Append($"<dxf><fill><patternFill patternType=\"solid\"><bgColor rgb=\"FF{band}\"/></patternFill></fill></dxf>");
        }

        return xml.Append("</dxfs><tableStyles count=\"0\"/></styleSheet>").ToString();
    }

    private static int Add<T>(List<T> list, T item)
    {
        var index = list.IndexOf(item);
        if (index >= 0)
        {
            return index;
        }

        list.Add(item);
        return list.Count - 1;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
