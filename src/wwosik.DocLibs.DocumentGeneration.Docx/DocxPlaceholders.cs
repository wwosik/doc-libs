using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace wwosik.DocLibs.DocumentGeneration.Docx;

/// <summary>
/// The title and author in the headers and footers of a template, before Word refreshes them: content controls bound to
/// the core properties (<c>dc:title</c>, <c>dc:creator</c>) and simple <c>TITLE</c> / <c>AUTHOR</c> fields get the
/// document's values as their text. Fields of either kind make Word update the document's fields on opening.
/// </summary>
internal static class DocxPlaceholders
{
    /// <summary>Fills the placeholders; returns whether a <c>TITLE</c> or <c>AUTHOR</c> field asks for <c>w:updateFields</c>.</summary>
    public static bool Fill(MainDocumentPart main, string? title, string? author)
    {
        var roots = main.HeaderParts.Select(h => (OpenXmlElement?)h.Header).Concat(main.FooterParts.Select(f => (OpenXmlElement?)f.Footer)).OfType<OpenXmlPartRootElement>().ToList();
        var fields = false;
        foreach (var root in roots)
        {
            var changed = false;
            foreach (var control in root.Descendants<SdtElement>().ToList())
            {
                if (control.SdtProperties?.GetFirstChild<DataBinding>() is not { } binding
                    || !string.Equals(binding.StoreItemId?.Value, DocxTemplateBuilder.CorePropertiesStore, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = binding.XPath?.Value switch
                {
                    { } path when path.EndsWith(":title[1]", StringComparison.Ordinal) => title,
                    { } path when path.EndsWith(":creator[1]", StringComparison.Ordinal) => author,
                    _ => null,
                };
                if (value is not null && SetText(control, value))
                {
                    control.SdtProperties!.GetFirstChild<ShowingPlaceholder>()?.Remove();
                    changed = true;
                }
            }

            foreach (var field in root.Descendants<SimpleField>())
            {
                var value = Instruction(field.Instruction?.Value) switch
                {
                    "TITLE" => title,
                    "AUTHOR" => author,
                    _ => null,
                };
                if (value is not null)
                {
                    fields = true;
                    field.RemoveAllChildren<Run>();
                    field.Append(DocxWriter.Run(value));
                    changed = true;
                }
            }

            fields |= root.Descendants<FieldCode>().Any(c => Instruction(c.Text) is "TITLE" or "AUTHOR");
            if (changed)
            {
                root.Save();
            }
        }

        return fields;
    }

    // The field's name: its first word, upper case.
    private static string? Instruction(string? instruction) =>
        instruction?.Trim().Split(' ', 2)[0].ToUpperInvariant();

    // The content's first text gets the value, every other text is emptied; runs keep their formatting.
    private static bool SetText(SdtElement control, string value)
    {
        var content = control.ChildElements.FirstOrDefault(e => e.LocalName == "sdtContent");
        if (content is null)
        {
            return false;
        }

        var texts = content.Descendants<Text>().ToList();
        if (texts.Count == 0)
        {
            var run = DocxWriter.Run(value);
            if (content.Descendants<Paragraph>().FirstOrDefault() is { } paragraph)
            {
                paragraph.Append(run);
            }
            else if (content is SdtContentBlock)
            {
                content.Append(new Paragraph(run));
            }
            else
            {
                content.Append(run);
            }

            return true;
        }

        texts[0].Text = value;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (var text in texts.Skip(1))
        {
            text.Remove();
        }

        return true;
    }
}
