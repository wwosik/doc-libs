using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// Sets elements of <c>word/settings.xml</c>, in the built-in template and in a caller's: each in its place in the
/// schema's order, replacing one that is there.
/// </summary>
internal static class DocxSettings
{
    // CT_Settings, in schema order, as far as the generator sets anything before or after.
    private static readonly string[] Order =
    [
        "writeProtection", "view", "zoom", "removePersonalInformation", "removeDateAndTime", "doNotDisplayPageBoundaries",
        "displayBackgroundShape", "printPostScriptOverText", "printFractionalCharacterWidth", "printFormsData",
        "embedTrueTypeFonts", "embedSystemFonts", "saveSubsetFonts", "saveFormsData", "mirrorMargins", "alignBordersAndEdges",
        "bordersDoNotSurroundHeader", "bordersDoNotSurroundFooter", "gutterAtTop", "hideSpellingErrors", "hideGrammaticalErrors",
        "activeWritingStyle", "proofState", "formsDesign", "attachedTemplate", "linkStyles", "stylePaneFormatFilter",
        "stylePaneSortMethod", "documentType", "mailMerge", "revisionView", "trackRevisions", "doNotTrackMoves",
        "doNotTrackFormatting", "documentProtection", "autoFormatOverride", "styleLockTheme", "styleLockQFSet", "defaultTabStop",
        "autoHyphenation", "consecutiveHyphenLimit", "hyphenationZone", "doNotHyphenateCaps", "showEnvelope", "summaryLength",
        "clickAndTypeStyle", "defaultTableStyle", "evenAndOddHeaders", "bookFoldRevPrinting", "bookFoldPrinting",
        "bookFoldPrintingSheets", "drawingGridHorizontalSpacing", "drawingGridVerticalSpacing",
        "displayHorizontalDrawingGridEvery", "displayVerticalDrawingGridEvery", "doNotUseMarginsForDrawingGridOrigin",
        "drawingGridHorizontalOrigin", "drawingGridVerticalOrigin", "doNotShadeFormData", "noPunctuationKerning",
        "characterSpacingControl", "printTwoOnOne", "strictFirstAndLastChars", "noLineBreaksAfter", "noLineBreaksBefore",
        "savePreviewPicture", "doNotValidateAgainstSchema", "saveInvalidXml", "ignoreMixedContent", "alwaysShowPlaceholderText",
        "doNotDemarcateInvalidXml", "saveXmlDataOnly", "useXSLTWhenSaving", "saveThroughXslt", "showXMLTags",
        "alwaysMergeEmptyNamespace", "updateFields", "hdrShapeDefaults", "footnotePr", "endnotePr", "compat", "docVars", "rsids",
        "mathPr", "attachedSchema", "themeFontLang", "clrSchemeMapping", "doNotIncludeSubdocsInStats",
        "doNotAutoCompressPictures", "forceUpgrade", "captions", "readModeInkLockDown", "smartTagType", "schemaLibrary",
        "shapeDefaults", "doNotEmbedSmartTags", "decimalSymbol", "listSeparator",
    ];

    /// <summary>Puts <paramref name="element"/> into the document's settings, creating the part if there is none.</summary>
    public static void Set(MainDocumentPart main, OpenXmlElement element)
    {
        var part = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
        var settings = part.Settings ??= new Settings();
        foreach (var existing in settings.ChildElements.Where(e => e.LocalName == element.LocalName && e.NamespaceUri == element.NamespaceUri).ToList())
        {
            existing.Remove();
        }

        var rank = Array.IndexOf(Order, element.LocalName);
        var before = settings.ChildElements.FirstOrDefault(e =>
            e.NamespaceUri == element.NamespaceUri && Array.IndexOf(Order, e.LocalName) is var other && other > rank);
        if (before is null)
        {
            // After every element of the schema; extensions of other namespaces (w14, w15) come last.
            var last = settings.ChildElements.LastOrDefault(e => e.NamespaceUri == element.NamespaceUri);
            if (last is null)
            {
                settings.PrependChild(element);
            }
            else
            {
                settings.InsertAfter(element, last);
            }
        }
        else
        {
            settings.InsertBefore(element, before);
        }

        settings.Save();
    }
}
