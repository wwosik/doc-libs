namespace wwosik.DocLibs.DocumentConversions;

/// <summary>A document format as recognized by <see cref="DocumentFormatDetector"/>.</summary>
public enum DocumentFormat
{
    /// <summary>Not recognized.</summary>
    Unknown,

    /// <summary>PDF.</summary>
    Pdf,

    /// <summary>Word document, Office Open XML (<c>.docx</c>, <c>.docm</c>, <c>.dotx</c>).</summary>
    Docx,

    /// <summary>PowerPoint presentation, Office Open XML (<c>.pptx</c>).</summary>
    Pptx,

    /// <summary>Excel workbook, Office Open XML (<c>.xlsx</c>, <c>.xlsm</c>).</summary>
    Xlsx,

    /// <summary>A ZIP archive that is not an Office Open XML document.</summary>
    Zip,

    /// <summary>An OLE compound file whose kind the file name does not reveal.</summary>
    Ole,

    /// <summary>Word 97–2003 document (OLE, <c>.doc</c>).</summary>
    Doc,

    /// <summary>Excel 97–2003 workbook (OLE, <c>.xls</c>).</summary>
    Xls,

    /// <summary>PowerPoint 97–2003 presentation (OLE, <c>.ppt</c>).</summary>
    Ppt,

    /// <summary>Outlook message (OLE, <c>.msg</c>).</summary>
    Msg,

    /// <summary>Rich Text Format.</summary>
    Rtf,

    /// <summary>Plain text: no NUL byte in the first 8 KB.</summary>
    Text,

    /// <summary>SVG, a vector image; reported by image converters, never by detection (it is text).</summary>
    Svg,
}
