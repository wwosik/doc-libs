using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using SdkType = DocumentFormat.OpenXml.SpreadsheetDocumentType;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// Writes a checked <see cref="XlsxPlan"/> as an <c>.xlsx</c>: the parts as XML text into a package the Open XML SDK
/// builds with fixed relationship ids, then the package copied entry by entry with one fixed time, so that the same
/// workbook and clock give the same bytes.
/// </summary>
internal sealed class XlsxWriter
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WorkbookType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const double RowHeight = 15;
    private const long EmuPerPoint = 12700;

    // Margins in inches, as the PDF generator's pages: 20 mm at the sides, 1 in at the top, 60 pt at the bottom.
    private const string Margins = "<pageMargins left=\"0.7874\" right=\"0.7874\" top=\"1\" bottom=\"0.8333\" header=\"0.3937\" footer=\"0.3937\"/>";

    private static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly XlsxPlan plan;
    private readonly XlsxStyles styles;
    private readonly List<string> strings = [];
    private readonly Dictionary<string, int> stringIndex = new(StringComparer.Ordinal);
    private readonly string textFont;
    private readonly string tableFont;
    private readonly (byte[] Data, ImageInfo Info)? logo;
    private ImagePart? image;

    private XlsxWriter(XlsxPlan plan)
    {
        this.plan = plan;
        this.textFont = plan.Brand.FontFamily;
        this.tableFont = plan.Brand.TableFontFamily ?? plan.Brand.FontFamily;
        this.styles = new XlsxStyles(new XlsxFont(this.tableFont, 11), plan.Colors.Line);
        this.logo = plan.Brand.Logo is { } data && ImageInfo.Read(data) is { Width: > 0, Height: > 0 } info ? (data, info) : null;
    }

    public static void Write(XlsxPlan plan, Stream output)
    {
        using var buffer = new MemoryStream();
        new XlsxWriter(plan).Package(buffer);
        buffer.Position = 0;
        Normalize(buffer, output);
    }

    private void Package(Stream buffer)
    {
        using var document = SpreadsheetDocument.Create(buffer, SdkType.Workbook);
        var workbook = document.AddNewPart<WorkbookPart>(WorkbookType, "rId1");
        var names = new List<string>();
        var sheets = new StringBuilder();
        var defined = new StringBuilder();
        var index = 0;
        if (this.plan.Info)
        {
            var part = workbook.AddNewPart<WorksheetPart>(Id(index + 1));
            Feed(part, this.InfoSheet(part, index));
            names.Add(this.plan.InfoName);
            index++;
        }

        foreach (var sheet in this.plan.Sheets)
        {
            var part = workbook.AddNewPart<WorksheetPart>(Id(index + 1));
            Feed(part, this.TableSheet(part, sheet, index));
            names.Add(sheet.Sheet.Name);
            var reference = XlsxPlan.Quoted(sheet.Sheet.Name);
            var last = XlsxPlan.ColumnName(Math.Max(sheet.Columns.Count, 1) - 1);
            defined.Append(Invariant($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{index}\" hidden=\"1\">"))
                .Append(XlsxXml.Text(Invariant($"{reference}!$A${sheet.HeaderRow}:${last}${Math.Max(sheet.LastRow, sheet.HeaderRow)}")))
                .Append("</definedName>");
            defined.Append(Invariant($"<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"{index}\">"))
                .Append(XlsxXml.Text(Invariant($"{reference}!${sheet.HeaderRow}:${sheet.HeaderRow}")))
                .Append("</definedName>");
            index++;
        }

        for (var i = 0; i < names.Count; i++)
        {
            sheets.Append(Invariant($"<sheet name=\"{XlsxXml.Attribute(names[i])}\" sheetId=\"{i + 1}\" r:id=\"{Id(i + 1)}\"/>"));
        }

        var stylesPart = workbook.AddNewPart<WorkbookStylesPart>(Id(names.Count + 1));
        var stringsPart = workbook.AddNewPart<SharedStringTablePart>(Id(names.Count + 2));
        Feed(workbook, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Relationships}\"><bookViews><workbookView activeTab=\"0\"/></bookViews>"
            + $"<sheets>{sheets}</sheets>{(defined.Length > 0 ? $"<definedNames>{defined}</definedNames>" : string.Empty)}"
            + (this.plan.HasFormulas ? "<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/>" : "<calcPr calcId=\"191029\"/>") + "</workbook>");
        Feed(stylesPart, this.styles.Xml());
        Feed(stringsPart, this.SharedStrings());

        var core = document.AddNewPart<CoreFilePropertiesPart>("rId2");
        CoreProperties.Write(core, this.plan.Workbook.Title, this.plan.Workbook.Author, this.plan.LanguageTag, this.plan.Now);
        CoreProperties.Describe(core, this.plan.Workbook.Subtitle, this.plan.Workbook.Description);
    }

    // The info sheet: logo, marking, title, subtitle, description; author, date, language; the contents with a link to each sheet.
    private string InfoSheet(WorksheetPart part, int index)
    {
        var workbook = this.plan.Workbook;
        var texts = this.plan.Texts;
        var colors = this.plan.Colors;
        var rows = new Rows();
        var merges = new List<string>();
        var links = new StringBuilder();
        var r = 1;
        string? drawing = null;
        if (this.logo is { } logo)
        {
            var height = 36.0;
            var width = Math.Min(180, height * logo.Info.Width / logo.Info.Height);
            height = width * logo.Info.Height / logo.Info.Width;
            drawing = this.Logo(part, logo, width, height);
            rows.Height(r, Math.Max(RowHeight, height + 12));
            r++;
        }

        if (this.plan.MarkingText is { Length: > 0 } marking)
        {
            rows.Add(r++, this.Text("A", r - 1, marking, new XlsxStyle(new XlsxFont(this.textFont, 11, Bold: true, Color: colors.Muted), NumberFormat: XlsxFormat.TextCode)));
        }

        if (!string.IsNullOrWhiteSpace(workbook.Title))
        {
            rows.Height(r, 30);
            rows.Add(r++, this.Text("A", r - 1, workbook.Title, new XlsxStyle(new XlsxFont(this.textFont, 20, Bold: true, Color: colors.Title), NumberFormat: XlsxFormat.TextCode)));
        }

        if (!string.IsNullOrWhiteSpace(workbook.Subtitle))
        {
            rows.Height(r, 21);
            rows.Add(r++, this.Text("A", r - 1, workbook.Subtitle, new XlsxStyle(new XlsxFont(this.textFont, 14), NumberFormat: XlsxFormat.TextCode)));
        }

        const double labelWidth = 22;
        const double valueWidth = 90;
        if (!string.IsNullOrWhiteSpace(workbook.Description))
        {
            r++;
            var width = (labelWidth + valueWidth) * TextMetrics.Width("0", 1100, false);
            var lines = workbook.Description.Split('\n').Sum(line => TextMetrics.Lines([(line, false)], 1100, width));
            rows.Height(r, lines * RowHeight);
            var style = new XlsxStyle(new XlsxFont(this.textFont, 11), NumberFormat: XlsxFormat.TextCode, Wrap: true, Top: true);
            rows.Add(r, this.Text("A", r, workbook.Description, style));
            rows.Add(r, Invariant($"<c r=\"B{r}\" s=\"{this.styles.Of(style)}\"/>"));
            merges.Add(Invariant($"A{r}:B{r}"));
            r++;
        }

        r++;
        var label = new XlsxStyle(new XlsxFont(this.textFont, 11, Bold: true), NumberFormat: XlsxFormat.TextCode);
        var value = new XlsxStyle(new XlsxFont(this.textFont, 11), NumberFormat: XlsxFormat.TextCode, Align: XlsxAlign.Left);
        var facts = r;
        if (!string.IsNullOrWhiteSpace(workbook.Author))
        {
            rows.Add(r, this.Text("A", r, texts.Author, label));
            rows.Add(r, this.Text("B", r, workbook.Author, value));
            r++;
        }

        if (this.plan.Date is { } date)
        {
            var format = XlsxFormat.For(XlsxColumnType.Date, null, null, this.plan.Culture);
            rows.Add(r, this.Text("A", r, texts.Date, label));
            rows.Add(r, this.Number("B", r, XlsxPlan.Serial(date.ToDateTime(TimeOnly.MinValue)), value with { NumberFormat = format.Code }));
            r++;
        }

        rows.Add(r, this.Text("A", r, texts.Language, label));
        rows.Add(r, this.Text("B", r, this.plan.LanguageTag, value));
        r += 2;

        if (this.plan.Sheets.Count > 0)
        {
            rows.Add(r, this.Text("A", r, texts.Contents, new XlsxStyle(new XlsxFont(this.textFont, 12, Bold: true, Color: colors.Title), NumberFormat: XlsxFormat.TextCode)));
            r++;
            var link = new XlsxStyle(new XlsxFont(this.textFont, 11, Underline: true, Color: colors.Link), NumberFormat: XlsxFormat.TextCode);
            foreach (var sheet in this.plan.Sheets)
            {
                var name = sheet.Sheet.Name;
                rows.Add(r, this.Text("A", r, name, link));
                if ((sheet.Title ?? sheet.Subtitle) is { } about)
                {
                    rows.Add(r, this.Text("B", r, about, value));
                }

                links.Append(Invariant($"<hyperlink ref=\"A{r}\" location=\"{XlsxXml.Attribute(XlsxPlan.Quoted(name) + "!A1")}\" display=\"{XlsxXml.Attribute(name)}\"/>"));
                r++;
            }
        }

        var columns = Invariant($"<cols><col min=\"1\" max=\"1\" width=\"{labelWidth}\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"{valueWidth}\" customWidth=\"1\"/></cols>");
        return this.Worksheet(
            part,
            index,
            Invariant($"A1:B{Math.Max(1, r - 1)}"),
            null,
            columns,
            rows,
            string.Concat(
                merges.Count > 0 ? Invariant($"<mergeCells count=\"{merges.Count}\">{string.Concat(merges.Select(m => $"<mergeCell ref=\"{m}\"/>"))}</mergeCells>") : string.Empty,
                links.Length > 0 ? $"<hyperlinks>{links}</hyperlinks>" : string.Empty),
            Invariant($"A1:B{Math.Max(facts, r - 1)}"),
            drawing);
    }

    // A table sheet: its title block, the header row (frozen, filtered), the rows with lines and banding.
    private string TableSheet(WorksheetPart part, SheetPlan sheet, int index)
    {
        var colors = this.plan.Colors;
        var rows = new Rows();
        var r = 1;
        if (sheet.Title is { } title)
        {
            rows.Height(r, 24);
            rows.Add(r, this.Text("A", r, title, new XlsxStyle(new XlsxFont(this.textFont, 16, Bold: true, Color: colors.Title), NumberFormat: XlsxFormat.TextCode)));
            r++;
        }

        if (sheet.Subtitle is { } subtitle)
        {
            rows.Height(r, 18);
            rows.Add(r, this.Text("A", r, subtitle, new XlsxStyle(new XlsxFont(this.textFont, 12), NumberFormat: XlsxFormat.TextCode)));
            r++;
        }

        if (sheet.ShowsDate && this.plan.Date is { } date)
        {
            var format = XlsxFormat.For(XlsxColumnType.Date, null, null, this.plan.Culture);
            rows.Add(r, this.Number("A", r, XlsxPlan.Serial(date.ToDateTime(TimeOnly.MinValue)), new XlsxStyle(new XlsxFont(this.textFont, 10, Color: colors.Muted), NumberFormat: format.Code, Align: XlsxAlign.Left)));
            r++;
        }

        var h = sheet.HeaderRow;
        var count = Math.Max(sheet.Columns.Count, 1);
        var last = XlsxPlan.ColumnName(count - 1);
        var headerFont = new XlsxFont(this.tableFont, 11, Bold: true, Color: SheetColors.White);
        var cellFont = new XlsxFont(this.tableFont, 11);
        var headers = sheet.Columns.Select(c => new XlsxStyle(headerFont, colors.Header, true, XlsxFormat.TextCode, c.Align)).Select(this.styles.Of).ToList();
        var cells = sheet.Columns.Select(c => new XlsxStyle(cellFont, null, true, c.Format.Code, c.Align)).Select(this.styles.Of).ToList();
        foreach (var column in sheet.Columns)
        {
            rows.Add(h, this.Cell(XlsxPlan.ColumnName(column.Index) + h.ToString(CultureInfo.InvariantCulture), XlsxCell.FromText(column.Column.Header), headers[column.Index]));
        }

        var data = sheet.Sheet.Rows ?? [];
        for (var i = 0; i < data.Count; i++)
        {
            var row = data[i] ?? [];
            var n = h + 1 + i;
            var xml = new StringBuilder();
            foreach (var column in sheet.Columns)
            {
                var cell = column.Index < row.Count ? row[column.Index] ?? XlsxCell.Empty : XlsxCell.Empty;
                var reference = XlsxPlan.ColumnName(column.Index) + n.ToString(CultureInfo.InvariantCulture);
                xml.Append(cell.Kind == XlsxCellKind.Formula
                    ? Invariant($"<c r=\"{reference}\" s=\"{cells[column.Index]}\"><f>{XlsxXml.Text(this.plan.Formula(cell.Text!).Write(n, sheet.Letter))}</f></c>")
                    : this.Cell(reference, cell, cells[column.Index]));
            }

            rows.Add(n, xml.ToString());
        }

        if (sheet.Totals is { } totals && sheet.TotalsRow is { } t)
        {
            // Like the header, lighter: bold black text on a light shade of the header's colour.
            var totalFont = new XlsxFont(this.tableFont, 11, Bold: true);
            var xml = new StringBuilder();
            foreach (var column in sheet.Columns)
            {
                var letter = XlsxPlan.ColumnName(column.Index);
                var reference = letter + t.ToString(CultureInfo.InvariantCulture);
                var total = totals[column.Index];
                var style = this.styles.Of(new XlsxStyle(totalFont, colors.Totals, true, total?.Format.Code ?? (column.Index == sheet.LabelColumn ? XlsxFormat.TextCode : column.Format.Code), column.Align));
                if (total is not null)
                {
                    var value = total switch
                    {
                        { Value: { } v } => Invariant($"<v>{v.ToString("R", CultureInfo.InvariantCulture)}</v>"),
                        { Error: { } e } => $"<v>{e}</v>",
                        _ => string.Empty,
                    };
                    xml.Append(Invariant($"<c r=\"{reference}\" s=\"{style}\"{(total.Error is null ? string.Empty : " t=\"e\"")}><f>SUBTOTAL({total.Function},{letter}{sheet.FirstDataRow}:{letter}{sheet.LastRow})</f>{value}</c>"));
                }
                else if (column.Index == sheet.LabelColumn && sheet.TotalsLabel is { } label)
                {
                    xml.Append(this.Cell(reference, XlsxCell.FromText(label), style));
                }
                else
                {
                    xml.Append(Invariant($"<c r=\"{reference}\" s=\"{style}\"/>"));
                }
            }

            rows.Add(t, xml.ToString());
        }

        var lastRow = Math.Max(sheet.LastRow, h);
        var widths = string.Concat(sheet.Columns.Select(c => Invariant($"<col min=\"{c.Index + 1}\" max=\"{c.Index + 1}\" width=\"{c.Width}\" customWidth=\"1\"/>")));

        // Text keeps its form: no "number stored as text" warning on the title block, the header row and text columns.
        var asText = new List<string> { Invariant($"A{h}:{last}{h}") };
        if (h > 1)
        {
            asText.Insert(0, Invariant($"A1:A{h - 1}"));
        }

        if (data.Count > 0)
        {
            asText.AddRange(sheet.Columns.Where(c => c.Format.Type == XlsxColumnType.Text)
                .Select(c => Invariant($"{XlsxPlan.ColumnName(c.Index)}{h + 1}:{XlsxPlan.ColumnName(c.Index)}{lastRow}")));
        }

        var after = new StringBuilder(Invariant($"<autoFilter ref=\"A{h}:{last}{lastRow}\"/>"));
        if (data.Count > 0)
        {
            after.Append(Invariant($"<conditionalFormatting sqref=\"A{h + 1}:{last}{lastRow}\"><cfRule type=\"expression\" dxfId=\"{this.styles.Band(colors.Band)}\" priority=\"1\">"))
                .Append(Invariant($"<formula>MOD(ROW()-{h},2)=0</formula></cfRule></conditionalFormatting>"));
        }

        return this.Worksheet(
            part,
            index,
            Invariant($"A1:{last}{sheet.TotalsRow ?? lastRow}"),
            h,
            sheet.Columns.Count > 0 ? $"<cols>{widths}</cols>" : string.Empty,
            rows,
            after.ToString(),
            string.Join(' ', asText),
            null);
    }

    private string Worksheet(WorksheetPart part, int index, string dimension, int? frozen, string columns, Rows rows, string afterData, string asText, string? drawing)
    {
        var view = new StringBuilder(Invariant($"<sheetView showGridLines=\"0\"{(index == 0 ? " tabSelected=\"1\"" : string.Empty)} workbookViewId=\"0\""));
        if (frozen is { } h)
        {
            view.Append(Invariant($"><pane ySplit=\"{h}\" topLeftCell=\"A{h + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>"))
                .Append(Invariant($"<selection pane=\"bottomLeft\" activeCell=\"A{h + 1}\" sqref=\"A{h + 1}\"/></sheetView>"));
        }
        else
        {
            view.Append("/>");
        }

        var paper = this.plan.Options.PageSize switch
        {
            XlsxPageSize.A3 => 8,
            XlsxPageSize.Letter => 1,
            _ => 9,
        };
        string? header = null;
        if (this.logo is { } logo)
        {
            var height = 18.0;
            var width = Math.Min(120, height * logo.Info.Width / logo.Info.Height);
            height = width * logo.Info.Height / logo.Info.Width;
            header = this.HeaderPicture(part, index, logo, width, height);
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{Relationships}\">"
            + "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>"
            + $"<dimension ref=\"{dimension}\"/><sheetViews>{view}</sheetViews>"
            + Invariant($"<sheetFormatPr defaultRowHeight=\"{RowHeight}\"/>")
            + columns
            + $"<sheetData>{rows.Xml()}</sheetData>"
            + afterData
            + Margins
            + Invariant($"<pageSetup paperSize=\"{paper}\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>")
            + $"<headerFooter><oddHeader>{XlsxXml.Text(this.Header(header is not null))}</oddHeader><oddFooter>{XlsxXml.Text(this.Footer())}</oddFooter></headerFooter>"
            + $"<ignoredErrors><ignoredError sqref=\"{asText}\" numberStoredAsText=\"1\"/></ignoredErrors>"
            + (drawing is null ? string.Empty : $"<drawing r:id=\"{drawing}\"/>")
            + (header is null ? string.Empty : $"<legacyDrawingHF r:id=\"{header}\"/>")
            + "</worksheet>";
    }

    // The printed page header: the title at the left, the marking in the centre, the logo at the right; at most 255 characters.
    private string Header(bool picture)
    {
        var marking = this.plan.MarkingText is { Length: > 0 } text ? $"&C&\"{this.textFont},Bold\"&9{Codes(text)}" : string.Empty;
        var right = picture ? "&R&G" : string.Empty;
        var start = $"&L&\"{this.textFont},Regular\"&9";
        var title = Codes(this.plan.Workbook.Title ?? string.Empty);
        var room = 255 - start.Length - marking.Length - right.Length;
        if (title.Length > room)
        {
            title = room > 1 ? title[..(room - 1)].TrimEnd('&') + "…" : string.Empty;
        }

        return start + title + marking + right;
    }

    // The printed page footer: the brand's footer text at the left, "Page n of m" at the right.
    private string Footer()
    {
        var pages = string.Format(CultureInfo.InvariantCulture, Codes(this.plan.Texts.PageOfFormat), "&P", "&N");
        var left = this.plan.Brand.FooterText is { Length: > 0 } footer ? $"&L&9{Codes(footer.Length > 100 ? footer[..100] : footer)}" : string.Empty;
        return left + "&R&9" + pages;
    }

    private string Logo(WorksheetPart part, (byte[] Data, ImageInfo Info) logo, double width, double height)
    {
        var drawing = part.AddNewPart<DrawingsPart>("rId2");
        this.Image(drawing, logo);

        var (cx, cy) = ((long)Math.Round(width * EmuPerPoint), (long)Math.Round(height * EmuPerPoint));
        Feed(drawing, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" "
            + $"xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"{Relationships}\">"
            + Invariant($"<xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>{6 * EmuPerPoint}</xdr:rowOff></xdr:from>")
            + Invariant($"<xdr:ext cx=\"{cx}\" cy=\"{cy}\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"Logo\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>")
            + "<xdr:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>"
            + Invariant($"<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>")
            + "</xdr:pic><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>");
        return "rId2";
    }

    // The logo as a header picture (&G): a VML drawing with one picture shape named for the right header, RH.
    private string HeaderPicture(WorksheetPart part, int index, (byte[] Data, ImageInfo Info) logo, double width, double height)
    {
        var vml = part.AddNewPart<VmlDrawingPart>("rId1");
        this.Image(vml, logo);

        Feed(vml, "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">"
            + Invariant($"<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"{index + 1}\"/></o:shapelayout>")
            + "<v:shapetype id=\"_x0000_t75\" coordsize=\"21600,21600\" o:spt=\"75\" o:preferrelative=\"t\" path=\"m@4@5l@4@11@9@11@9@5xe\" filled=\"f\" stroked=\"f\">"
            + "<v:stroke joinstyle=\"miter\"/><v:formulas><v:f eqn=\"if lineDrawn pixelLineWidth 0\"/><v:f eqn=\"sum @0 1 0\"/><v:f eqn=\"sum 0 0 @1\"/>"
            + "<v:f eqn=\"prod @2 1 2\"/><v:f eqn=\"prod @3 21600 pixelWidth\"/><v:f eqn=\"prod @3 21600 pixelHeight\"/><v:f eqn=\"sum @0 0 1\"/>"
            + "<v:f eqn=\"prod @6 1 2\"/><v:f eqn=\"prod @7 21600 pixelWidth\"/><v:f eqn=\"sum @8 21600 0\"/><v:f eqn=\"prod @7 21600 pixelHeight\"/>"
            + "<v:f eqn=\"sum @10 21600 0\"/></v:formulas><v:path o:extrusionok=\"f\" gradientshapeok=\"t\" o:connecttype=\"rect\"/>"
            + "<o:lock v:ext=\"edit\" aspectratio=\"t\"/></v:shapetype>"
            + Invariant($"<v:shape id=\"RH\" o:spid=\"_x0000_s{1025 + (index * 1024)}\" type=\"#_x0000_t75\" style=\"position:absolute;margin-left:0;margin-top:0;width:{Math.Round(width, 2)}pt;height:{Math.Round(height, 2)}pt;z-index:1\">")
            + "<v:imagedata o:relid=\"rId1\" o:title=\"Logo\"/><o:lock v:ext=\"edit\" rotation=\"t\"/></v:shape></xml>");
        return "rId1";
    }

    // The logo's one image part, related from every drawing that shows it as rId1.
    private void Image(OpenXmlPart owner, (byte[] Data, ImageInfo Info) logo)
    {
        if (this.image is { } shared)
        {
            owner.AddPart(shared, "rId1");
            return;
        }

        this.image = owner switch
        {
            DrawingsPart drawing => drawing.AddImagePart(ImageType(logo.Info), "rId1"),
            VmlDrawingPart vml => vml.AddImagePart(ImageType(logo.Info), "rId1"),
            _ => throw new InvalidOperationException(),
        };
        using var data = new MemoryStream(logo.Data);
        this.image.FeedData(data);
    }

    private string Cell(string reference, XlsxCell cell, int style) => cell.Kind switch
    {
        XlsxCellKind.Text => Invariant($"<c r=\"{reference}\" s=\"{style}\" t=\"s\"><v>{this.String(cell.Text!)}</v></c>"),
        XlsxCellKind.Number => Invariant($"<c r=\"{reference}\" s=\"{style}\"><v>{cell.Number.ToString("R", CultureInfo.InvariantCulture)}</v></c>"),
        XlsxCellKind.Date or XlsxCellKind.DateTime => Invariant($"<c r=\"{reference}\" s=\"{style}\"><v>{XlsxPlan.Serial(cell.DateTime).ToString("R", CultureInfo.InvariantCulture)}</v></c>"),
        XlsxCellKind.Boolean => Invariant($"<c r=\"{reference}\" s=\"{style}\" t=\"b\"><v>{(cell.Boolean ? 1 : 0)}</v></c>"),
        _ => Invariant($"<c r=\"{reference}\" s=\"{style}\"/>"),
    };

    private string Text(string column, int row, string text, XlsxStyle style) =>
        this.Cell(column + row.ToString(CultureInfo.InvariantCulture), XlsxCell.FromText(text), this.styles.Of(style));

    private string Number(string column, int row, double value, XlsxStyle style) =>
        Invariant($"<c r=\"{column}{row}\" s=\"{this.styles.Of(style)}\"><v>{value.ToString("R", CultureInfo.InvariantCulture)}</v></c>");

    private int String(string text)
    {
        if (!this.stringIndex.TryGetValue(text, out var index))
        {
            index = this.strings.Count;
            this.strings.Add(text);
            this.stringIndex[text] = index;
        }

        return index;
    }

    private string SharedStrings()
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append(Invariant($"<sst xmlns=\"{Main}\" count=\"{this.strings.Count}\" uniqueCount=\"{this.strings.Count}\">"));
        foreach (var text in this.strings)
        {
            xml.Append("<si><t xml:space=\"preserve\">").Append(XlsxXml.Text(text)).Append("</t></si>");
        }

        return xml.Append("</sst>").ToString();
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(data);
    }

    private static PartTypeInfo ImageType(ImageInfo info) => info.Format switch
    {
        ImageFormat.Jpeg => ImagePartType.Jpeg,
        ImageFormat.Gif => ImagePartType.Gif,
        _ => ImagePartType.Png,
    };

    // Text in a header or footer: & is a code there, so a literal one is doubled.
    private static string Codes(string text) => text.Replace("&", "&&", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Id(int n) => "rId" + n.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // The package again, entry by entry in the same order, every entry with the same time.
    private static void Normalize(Stream package, Stream output)
    {
        using var result = new MemoryStream();
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = EntryTime;
                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }
        }

        result.Position = 0;
        result.CopyTo(output);
    }

    /// <summary>The rows of a sheet, each its cells' XML in column order, and the rows given a height of their own.</summary>
    private sealed class Rows
    {
        private readonly SortedDictionary<int, StringBuilder> cells = [];
        private readonly Dictionary<int, double> heights = [];

        public void Add(int row, string xml)
        {
            if (!this.cells.TryGetValue(row, out var text))
            {
                this.cells[row] = text = new StringBuilder();
            }

            text.Append(xml);
        }

        public void Height(int row, double height)
        {
            this.heights[row] = height;
            if (!this.cells.ContainsKey(row))
            {
                this.cells[row] = new StringBuilder();
            }
        }

        public string Xml()
        {
            var xml = new StringBuilder();
            foreach (var (row, text) in this.cells)
            {
                xml.Append(Invariant($"<row r=\"{row}\""));
                if (this.heights.TryGetValue(row, out var height))
                {
                    xml.Append(Invariant($" ht=\"{Math.Round(height, 2)}\" customHeight=\"1\""));
                }

                xml.Append('>').Append(text).Append("</row>");
            }

            return xml.ToString();
        }
    }
}
