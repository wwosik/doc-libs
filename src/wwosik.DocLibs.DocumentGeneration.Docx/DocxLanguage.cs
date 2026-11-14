using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The document language for Word's spell checking: <c>w:lang</c> in the document defaults (<c>w:rPrDefault</c>) and
/// <c>w:themeFontLang</c> in the settings, on every template. Languages a template's own styles set are left alone,
/// and so are its East Asian and complex-script languages.
/// </summary>
internal static class DocxLanguage
{
    public static void Set(MainDocumentPart main, string tag)
    {
        var styles = (main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>()).Styles ??= new Styles();
        var defaults = styles.DocDefaults;
        if (defaults is null)
        {
            defaults = new DocDefaults();
            styles.PrependChild(defaults);
        }

        var runDefaults = defaults.RunPropertiesDefault;
        if (runDefaults is null)
        {
            runDefaults = new RunPropertiesDefault();
            defaults.PrependChild(runDefaults);
        }

        var properties = runDefaults.RunPropertiesBaseStyle ??= new RunPropertiesBaseStyle();
        var languages = properties.GetFirstChild<Languages>();
        if (languages is null)
        {
            languages = new Languages();

            // w:lang comes after the sizes and before what East Asian layout adds.
            if (properties.GetFirstChild<EastAsianLayout>() is { } after)
            {
                properties.InsertBefore(languages, after);
            }
            else
            {
                properties.Append(languages);
            }
        }

        languages.Val = tag;
        styles.Save();

        var settings = (main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>()).Settings;
        var language = (ThemeFontLanguages?)settings?.GetFirstChild<ThemeFontLanguages>()?.CloneNode(true) ?? new ThemeFontLanguages();
        language.Val = tag;
        DocxSettings.Set(main, language);
    }
}
