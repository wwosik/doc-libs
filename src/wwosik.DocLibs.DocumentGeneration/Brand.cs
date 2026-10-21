namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// What a built-in template takes from a brand: colours, footer text and images. Colours are RGB hex without
/// <c>#</c>. <see cref="ThemeColors"/> is the full colour scheme in the order of a PowerPoint theme
/// (dark 1, light 1, dark 2, light 2, accent 1–6, hyperlink, followed hyperlink); the rest are used directly.
/// </summary>
internal sealed record Brand(
    string Name,
    IReadOnlyList<string> ThemeColors,
    string Primary,
    string Link,
    string Footer,
    IReadOnlyList<string> Gradient,
    string TableHeader,
    string TableBand,
    string TableLine,
    string? FooterText,
    byte[]? Logo,
    byte[]? WhiteLogo,
    byte[]? Artwork,
    byte[]? Claim)
{
    /// <summary>White, Arial, dark titles, blue accents; no images and no footer text.</summary>
    public static Brand Neutral { get; } = new(
        "Neutral",
        ["000000", "FFFFFF", "1F2937", "F3F4F6", "1D4ED8", "0F766E", "B45309", "7C3AED", "BE123C", "4B5563", "1D4ED8", "6D28D9"],
        Primary: "1F2937",
        Link: "1D4ED8",
        Footer: "6B7280",
        Gradient: ["1F2937", "1D4ED8"],
        TableHeader: "1D4ED8",
        TableBand: "F3F4F6",
        TableLine: "D1D5DB",
        FooterText: null,
        Logo: null,
        WhiteLogo: null,
        Artwork: null,
        Claim: null);
}
