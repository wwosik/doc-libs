using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The numbering of lists and headings (<c>word/numbering.xml</c>). The built-in template has one multilevel list
/// linked to Heading 1–3 (<c>1</c>, <c>1.1</c>, <c>1.1.1</c>, <see cref="HeadingNumId"/>). Lists use the numbering of
/// the template's list styles; where a list style has none, the definitions of the built-in design are added the first
/// time a list needs them: bullets <c>•</c>, <c>–</c>, <c>▪</c> and numbers <c>1.</c>, <c>a.</c>, <c>i.</c>, three levels
/// of 18 pt each. Every ordered list gets a <c>w:num</c> of its own with a <c>w:startOverride</c>, so it restarts.
/// </summary>
internal sealed class DocxNumbering
{
    /// <summary>The <c>w:numId</c> of the headings' multilevel list in the built-in template.</summary>
    public const int HeadingNumId = 1;

    /// <summary>The indent of each list level, in twentieths of a point: 18 pt, as in the PDF.</summary>
    public const int LevelIndent = 360;

    private readonly MainDocumentPart main;
    private readonly DocxStyles styles;
    private Numbering? numbering;
    private int? bullet;
    private int? orderedAbstract;
    private bool changed;

    public DocxNumbering(MainDocumentPart main, DocxStyles styles)
    {
        this.main = main;
        this.styles = styles;
    }

    /// <summary>The built-in template's <c>numbering.xml</c>: the multilevel list of Heading 1–3 (style ids of the built-in template).</summary>
    public static string BuiltInXml(string namespaces) =>
        $"<w:numbering {namespaces}><w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>"
        + string.Concat(Enumerable.Range(0, 3).Select(level =>
            $"<w:lvl w:ilvl=\"{N(level)}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:pStyle w:val=\"Heading{N(level + 1)}\"/>"
            + $"<w:lvlText w:val=\"{string.Join(".", Enumerable.Range(1, level + 1).Select(l => "%" + N(l)))}\"/><w:lvlJc w:val=\"left\"/>"
            + $"<w:pPr><w:ind w:left=\"{N(567 + (level * 142))}\" w:hanging=\"{N(567 + (level * 142))}\"/></w:pPr></w:lvl>"))
        + $"</w:abstractNum><w:num w:numId=\"{N(HeadingNumId)}\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";

    /// <summary>The <c>w:numId</c> of bulleted lists.</summary>
    public int Bullet()
    {
        if (this.bullet is { } known)
        {
            return known;
        }

        var id = this.StyleNumId(DocxRoles.ListBullet) ?? this.AddNum(this.AddAbstract(Bullets), null);
        this.bullet = id;
        return id;
    }

    /// <summary>A new <c>w:numId</c> for an ordered list at <paramref name="level"/> (0–2) that starts at <paramref name="start"/>.</summary>
    public int Ordered(int level, int start)
    {
        this.orderedAbstract ??= this.StyleNumId(DocxRoles.ListNumber) is { } styleNum && this.AbstractOf(styleNum) is { } styleAbstract
            ? styleAbstract
            : this.AddAbstract(Numbers);
        return this.AddNum(this.orderedAbstract.Value, (level, start));
    }

    /// <summary>The left indent of <paramref name="level"/> (0–2) of the list <paramref name="numId"/>, for its paragraphs without a marker.</summary>
    public int Indent(int numId, int level)
    {
        var abstractId = this.AbstractOf(numId);
        var lvl = this.Part().Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId)
            ?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
        return int.TryParse(lvl?.PreviousParagraphProperties?.Indentation?.Left?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
            ? left
            : LevelIndent * (level + 1);
    }

    /// <summary>Whether the style of <paramref name="role"/> numbers its paragraphs itself.</summary>
    public bool StyleNumbers(StyleRole role) => this.StyleNumId(role) is not null;

    /// <summary>Saves the numbering if lists added to it.</summary>
    public void Save()
    {
        if (this.changed)
        {
            this.numbering!.Save();
        }
    }

    private int? StyleNumId(StyleRole role) =>
        this.styles.Style(role).StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value is { } id && id > 0 ? id : null;

    private int? AbstractOf(int numId) =>
        this.Part().Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId)?.AbstractNumId?.Val?.Value;

    private Numbering Part()
    {
        if (this.numbering is null)
        {
            var part = this.main.NumberingDefinitionsPart ?? this.main.AddNewPart<NumberingDefinitionsPart>();
            this.numbering = part.Numbering ??= new Numbering();
        }

        return this.numbering;
    }

    // An abstract definition from its levels' XML, under a free id, after the existing ones (they precede every w:num).
    private int AddAbstract(Func<int, string> levels)
    {
        var part = this.Part();
        var id = part.Elements<AbstractNum>().Select(a => a.AbstractNumberId?.Value ?? 0).DefaultIfEmpty(-1).Max() + 1;
        var element = new AbstractNum(
            $"<w:abstractNum xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" w:abstractNumId=\"{N(id)}\">"
            + "<w:multiLevelType w:val=\"hybridMultilevel\"/>" + string.Concat(Enumerable.Range(0, 3).Select(levels)) + "</w:abstractNum>");
        if (part.Elements<AbstractNum>().LastOrDefault() is { } last)
        {
            part.InsertAfter(element, last);
        }
        else
        {
            part.PrependChild(element);
        }

        this.changed = true;
        return id;
    }

    private int AddNum(int abstractId, (int Level, int Start)? start)
    {
        var part = this.Part();
        var id = part.Elements<NumberingInstance>().Select(n => n.NumberID?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;
        var num = new NumberingInstance(new AbstractNumId { Val = abstractId }) { NumberID = id };
        if (start is { } restart)
        {
            num.Append(new LevelOverride(new StartOverrideNumberingValue { Val = restart.Start }) { LevelIndex = restart.Level });
        }

        // After the last w:num, before w:numIdMacAtCleanup if there is one.
        if (part.Elements<NumberingInstance>().LastOrDefault() is { } last)
        {
            part.InsertAfter(num, last);
        }
        else if (part.Elements<AbstractNum>().LastOrDefault() is { } lastAbstract)
        {
            part.InsertAfter(num, lastAbstract);
        }
        else
        {
            part.PrependChild(num);
        }

        this.changed = true;
        return id;
    }

    private static string Bullets(int level) => Level(level, "bullet", level switch { 0 => "•", 1 => "–", _ => "▪" });

    private static string Numbers(int level) => Level(level, level switch { 0 => "decimal", 1 => "lowerLetter", _ => "lowerRoman" }, "%" + N(level + 1) + ".");

    private static string Level(int level, string format, string text) =>
        $"<w:lvl w:ilvl=\"{N(level)}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{format}\"/><w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/>"
        + $"<w:pPr><w:ind w:left=\"{N(LevelIndent * (level + 1))}\" w:hanging=\"{N(LevelIndent)}\"/></w:pPr></w:lvl>";

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
