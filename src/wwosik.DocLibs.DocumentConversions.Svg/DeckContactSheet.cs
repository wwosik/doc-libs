using wwosik.DocLibs.DocumentGeneration;
using wwosik.DocLibs.DocumentGeneration.Pptx;

namespace wwosik.DocLibs.DocumentConversions.Svg;

/// <summary>
/// The contact sheet <see cref="DocumentTool"/> attaches to the decks it writes
/// (<see cref="DocumentToolOptions.Preview"/>): the deck's slides read with <see cref="DeckSvg"/>, painted with
/// <see cref="DeckPreview.Render"/> and laid out with <see cref="DeckPreview.ContactSheet"/>, four to a row, numbered.
/// At most <see cref="IDeckPreview.MaxSlides"/> slides, from the first. No browser, no network; the same deck gives the
/// same PNG.
/// <code>
/// var tool = new DocumentTool([PptxGenerator.ForOptions(options)], new() { Preview = new DeckContactSheet() });
/// </code>
/// </summary>
public sealed class DeckContactSheet : IDeckPreview
{
    private readonly IReadOnlyList<ReadOnlyMemory<byte>> fonts;
    private readonly ContactSheetOptions sheet;

    /// <summary>Creates the preview.</summary>
    /// <param name="fonts">
    /// TrueType files for families the decks name but do not embed, as in <see cref="DeckSvgOptions.Fonts"/>; brand fonts
    /// are embedded in generated decks by default, so this is rarely needed.
    /// </param>
    /// <param name="sheet">
    /// The layout; default <see cref="ContactSheetOptions"/> (four columns of 320 pixels, numbered). Its
    /// <see cref="ContactSheetOptions.LabelFont"/> defaults to Liberation Sans.
    /// </param>
    public DeckContactSheet(IReadOnlyList<ReadOnlyMemory<byte>>? fonts = null, ContactSheetOptions? sheet = null)
    {
        this.fonts = fonts ?? [];
        this.sheet = sheet ?? new ContactSheetOptions();
    }

    /// <inheritdoc/>
    /// <exception cref="DocumentConversionException">The deck cannot be read, or a slide cannot be painted.</exception>
    /// <exception cref="ArgumentException">The sheet would be larger than <see cref="SvgConverter.MaxPixels"/> a side.</exception>
    public byte[]? ContactSheet(ReadOnlyMemory<byte> pptx, GenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (pptx.IsEmpty)
        {
            return null;
        }

        using var stream = new MemoryStream(pptx.ToArray(), writable: false);
        var svgs = DeckSvg.Write(stream, new DeckSvgOptions
        {
            Fonts = this.fonts,
            Slides = result.Pages > IDeckPreview.MaxSlides ? [.. Enumerable.Range(1, IDeckPreview.MaxSlides)] : null,
        });
        var slides = svgs.Slides.Take(IDeckPreview.MaxSlides).ToList();
        if (slides.Count == 0)
        {
            return null;
        }

        // Painted at twice the thumbnail's width, so the sheet's downscaling keeps small text legible.
        var width = Math.Clamp(this.sheet.ThumbnailWidth * 2, 1, SvgConverter.MaxPixels);
        var previews = DeckPreview.Render(slides, new DeckPreviewOptions { Width = width, Fonts = svgs.Fonts });
        var options = this.sheet.LabelFont.IsEmpty ? this.sheet with { LabelFont = svgs.LabelFont } : this.sheet;
        return DeckPreview.ContactSheet(previews, options);
    }
}
