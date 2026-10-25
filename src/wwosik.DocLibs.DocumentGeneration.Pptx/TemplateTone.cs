using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>Reads from a template whether its slides are light or dark (<see cref="SlideTone.Auto"/>).</summary>
internal static class TemplateTone
{
    /// <summary>
    /// Dark when the first master's title text is light (relative luminance above 0.5); else dark when its background
    /// is a dark solid colour or theme colour (below 0.25); else light. Images and gradients are not looked at.
    /// </summary>
    public static SlideTone Detect(PresentationPart presentation)
    {
        var master = presentation.SlideMasterParts.FirstOrDefault();
        if (master?.SlideMaster is not { } slideMaster)
        {
            return SlideTone.Light;
        }

        string? Resolve(OpenXmlColor color) => color switch
        {
            { Rgb: { } rgb } => rgb,
            { Scheme: { } scheme } => SchemeColor(master, slideMaster, scheme),
            _ => null,
        };

        var title = slideMaster.TextStyles?.TitleStyle?.Level1ParagraphProperties?.GetFirstChild<A.DefaultRunProperties>()?.GetFirstChild<A.SolidFill>();
        if (title is not null && Resolve(Read(title)) is { } titleColor)
        {
            return Luminance(titleColor) > 0.5 ? SlideTone.Dark : SlideTone.Light;
        }

        var background = slideMaster.CommonSlideData?.Background;
        var fill = background?.BackgroundProperties?.GetFirstChild<A.SolidFill>();
        var color = fill is not null ? Read(fill)
            : background?.BackgroundStyleReference is { } reference ? Read(reference)
            : default;
        return Resolve(color) is { } backgroundColor && Luminance(backgroundColor) < 0.25 ? SlideTone.Dark : SlideTone.Light;
    }

    private static OpenXmlColor Read(DocumentFormat.OpenXml.OpenXmlElement element) =>
        element.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value is { } rgb ? new OpenXmlColor(rgb, null)
        : element.GetFirstChild<A.SchemeColor>()?.Val?.InnerText is { } scheme ? new OpenXmlColor(null, scheme)
        : default;

    // A scheme colour through the master's colour map (bg1 → lt1 …) to the theme's colour scheme.
    private static string? SchemeColor(SlideMasterPart master, P.SlideMaster slideMaster, string name)
    {
        var map = slideMaster.ColorMap;
        var mapped = name switch
        {
            "bg1" => map?.Background1?.InnerText,
            "tx1" => map?.Text1?.InnerText,
            "bg2" => map?.Background2?.InnerText,
            "tx2" => map?.Text2?.InnerText,
            _ => name,
        } ?? name;
        var scheme = master.ThemePart?.Theme?.ThemeElements?.ColorScheme;
        A.Color2Type? color = mapped switch
        {
            "dk1" => scheme?.Dark1Color,
            "lt1" => scheme?.Light1Color,
            "dk2" => scheme?.Dark2Color,
            "lt2" => scheme?.Light2Color,
            "accent1" => scheme?.Accent1Color,
            "accent2" => scheme?.Accent2Color,
            "accent3" => scheme?.Accent3Color,
            "accent4" => scheme?.Accent4Color,
            "accent5" => scheme?.Accent5Color,
            "accent6" => scheme?.Accent6Color,
            _ => null,
        };
        return color?.RgbColorModelHex?.Val?.Value ?? color?.SystemColor?.LastColor?.Value;
    }

    // Relative luminance, as for contrast ratios.
    internal static double Luminance(string hex)
    {
        var rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel((rgb >> 16) & 0xFF)) + (0.7152 * Channel((rgb >> 8) & 0xFF)) + (0.0722 * Channel(rgb & 0xFF));
    }

    private readonly record struct OpenXmlColor(string? Rgb, string? Scheme);
}
