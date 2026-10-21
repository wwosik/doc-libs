# 00001 Client brands for the generators

> Status: verify — implemented 2026-10-21

## Summary

Let a client or project bring its own design — colours, logo, footer text, cover images, fonts,
and optionally its own PowerPoint template — to both generators, without anything
client-specific ever living in this repo. A brand is a public `Brand` record that callers build
in code, or load from a **brand pack** (a folder, zip, embedded resources or any other source,
holding `brand.json`, the images and the fonts). Front matter can switch between the brands the
caller registered. The first nuget.org release comes after this plan, so the first public API
already has brands.

## Context

- Both generators already render from an internal `Brand` record
  (`src/wwosik.DocLibs.DocumentGeneration/Brand.cs`): theme colours, primary / link / footer
  colours, the section-divider gradient, table colours, footer text, and four images (logo,
  white logo, cover artwork, cover claim). The PPTX master (`TemplateBuilder`) and the PDF layout
  (`LayoutEngine`) draw each image and the footer text only when the brand has it.
- Only `Brand.Neutral` exists, and it has no images and no footer text, so those drawing paths
  currently have no test coverage.
- Templates are chosen through one options record per template: `NeutralTemplateOptions` and
  `CustomTemplateOptions(stream)` for PPTX, `NeutralPdfTemplateOptions` for PDF. Front matter
  `template: <name>` switches among the built-in ones (`PptxTemplateOptions.Create`,
  `PdfTemplateOptions.Create`); see [docs/wwosik.DocLibs.DocumentGeneration.md](../../docs/wwosik.DocLibs.DocumentGeneration.md#front-matter).
- `CustomTemplateOptions` already lets a PPTX use a client's `.potx`, but the PDF generator has
  no equivalent, and a client's `.potx` often suits Markdown less well than the built-in master
  (hand-made body styles, untyped layouts).
- **Fonts:** the PDF generator embeds one internal `FontSet` (Liberation Sans and Mono, six
  faces: `Regular`, `Bold`, `Italic`, `BoldItalic`, `Mono`, `MonoBold`; `Fonts.cs`), parsed with
  PdfPig's TrueType parser. PdfPig writes TrueType outlines only. The PPTX master's theme fonts
  are fixed to Arial (`TemplateBuilder`, `fontScheme`).
- Nothing has been published to nuget.org yet, so public names can still change freely.
- Hard rule: this repo stays client-neutral. Client brands live in the client's own code or
  package; tests here use a made-up brand.

## Proposed approach

### 1. Make `Brand` public

In `wwosik.DocLibs.DocumentGeneration`, replace the internal positional record with a public,
init-only one. Rename the client-flavoured terms on the way (`Claim` → `Tagline`, `Artwork` →
`CoverArtwork`, `WhiteLogo` → `LogoOnDark`).

```csharp
public sealed record Brand
{
    public required string Name { get; init; }          // front-matter name: [a-z0-9-]+, case-insensitive
    public BrandColors Colors { get; init; } = BrandColors.Neutral;
    public BrandFonts? Fonts { get; init; }              // null: Liberation in PDF, Arial in PPTX
    public string? FooterText { get; init; }
    public BrandImage? Logo { get; init; }               // content pages and cover
    public BrandImage? LogoOnDark { get; init; }         // PPTX section dividers (gradient background)
    public BrandImage? CoverArtwork { get; init; }
    public BrandImage? Tagline { get; init; }            // image, bottom right on the cover
    public PptxBrandTemplate? Pptx { get; init; }        // optional .potx, see step 5

    public static Brand Neutral { get; }
}

public sealed record BrandColors
{
    public required string Primary { get; init; }        // "#RRGGBB" or "RRGGBB"
    public string? Link { get; init; }                   // default: Primary
    public string? Footer { get; init; }                 // default: grey 6B7280
    public string? TableHeader { get; init; }            // default: Primary
    public string? TableBand { get; init; }              // default: Primary at 10 % on white
    public string? TableLine { get; init; }              // default: grey D1D5DB
    public IReadOnlyList<string>? Gradient { get; init; }// default: [Primary, Link]
    public IReadOnlyList<string>? Theme { get; init; }   // 12 PowerPoint theme colours; default derived
    public static BrandColors Neutral { get; }
}

public sealed record BrandImage(ReadOnlyMemory<byte> Content);   // PNG or JPEG, checked by header
```

- **Only `Primary` is required**; everything else has a documented default derived from it, so
  a minimal brand is a name, a colour and a logo.
- **Validation** happens once, when a generator is called with the brand: a malformed colour, a
  theme list that is not 12 long, an image that is not PNG or JPEG, an invalid font (step 3) or
  an invalid name throws `ArgumentException` naming the property. (Validation at generation, not
  construction, keeps `init` objects composable with `with`.)
- **Equality:** `BrandImage` and `BrandFont` compare by content, so options records holding a
  brand stay comparable, as the front-matter tests require.

### 2. One built-in template per generator, parameterized by a brand

The per-brand options records are **replaced** (decided — nothing is published yet):

| Today | New |
| ----- | --- |
| `NeutralTemplateOptions { ShowDate }` | `BrandTemplateOptions { Brand = Brand.Neutral, ShowDate = true, ShowTagline = true }` |
| `CustomTemplateOptions(stream) { Layouts }` | unchanged |
| `NeutralPdfTemplateOptions { ShowDate }` | `BrandPdfTemplateOptions { Brand = Brand.Neutral, ShowDate = true, ShowTagline = true }` |

`PptxOptions.Template` and `PdfOptions.Template` default to the neutral brand, as today.

### 3. Brand fonts

```csharp
public sealed record BrandFonts
{
    public required BrandFont Regular { get; init; }
    public required BrandFont Bold { get; init; }
    public required BrandFont Italic { get; init; }
    public required BrandFont BoldItalic { get; init; }
    public BrandFont? Mono { get; init; }                // null: Liberation Mono for code
    public BrandFont? MonoBold { get; init; }            // null: Mono, else Liberation Mono Bold
    public string? Family { get; init; }                 // default: read from Regular's name table
}

public sealed record BrandFont(ReadOnlyMemory<byte> Content);    // TrueType outlines only
```

- **The four text faces are required**, mono is optional (decided). A missing text face is a
  validation error; nothing is synthesized (no fake bold or slant).
- **TrueType outlines only** (decided): `.ttf`, and `.otf` with TrueType (`glyf`) outlines. An
  OpenType font with CFF outlines is rejected, with a message saying to convert it to TrueType —
  PdfPig cannot embed CFF.
- **PDF:** the internal `FontSet` is built from the brand's fonts instead of Liberation, with the
  same subsetting, the same 255-character copies, and the same glyph rules: a character the
  brand's face lacks throws, unless `PdfOptions.ReplaceUnsupported` replaces it. The
  `FontSet.Liberation` resources stay as the default and as the mono fallback.
- **PPTX:** the master's theme `majorFont` and `minorFont` take the brand's family name (decided:
  from the PDF fonts, no separate field). The font is **not embedded**; PowerPoint substitutes
  if it is not installed. `Family` overrides the name read from the `name` table (typographic
  family, nameID 16, else family, nameID 1) of `Regular`. Without brand fonts: Arial, as today.
  The code font stays `PptxOptions.CodeFont`.
- **Licensing is the caller's responsibility** (decided): the library does not check the
  `OS/2 fsType` embedding flags. The docs warn that embedding a font in a PDF needs a licence
  that allows it, and that restricted fonts must not be used.

### 4. Registered brands and front matter

- `PptxOptions.Brands` and `PdfOptions.Brands` (`IReadOnlyList<Brand>`, default empty) list the
  brands front matter may pick. `neutral` is always available.
- The key stays **`template`** (decided): `template: acme` selects the registered brand named
  `acme`, `template: neutral` the neutral one. The caller's `ShowDate` / `ShowTagline` carry over
  when the brand is the one already chosen, and reset to defaults otherwise — the same rule as
  today.
- An unknown name throws `DocumentConversionException`, listing the registered names.
- **Front matter can only choose among brands the caller registered.** It never names a file or
  a path, so untrusted Markdown cannot make the library read anything.
- New front-matter key `showTagline`; `showDate` stays.

### 5. A brand may carry its own PowerPoint template

```csharp
public sealed record PptxBrandTemplate(ReadOnlyMemory<byte> Content)   // a .pptx or .potx
{
    public string? TitleLayout { get; init; }
    public string? ContentLayout { get; init; }
    public string? TitleOnlyLayout { get; init; }
    public string? SectionHeaderLayout { get; init; }
}
```

When a brand has one, `BrandTemplateOptions` builds decks on it (exactly as
`CustomTemplateOptions` does today, the layout names mapped to `PptxLayoutNames`) while the PDF
generator keeps using the brand's colours, images and fonts. One brand thus covers both
generators even for clients whose decks must use the corporate `.potx`. The record lives in the
shared package next to `Brand`, which cannot reference the PPTX package — hence its own layout
properties instead of `PptxLayoutNames`.

### 6. Brand packs

A brand pack holds `brand.json` and the files it names:

```
brand.json
logo.png
logo-on-dark.png
cover-artwork.png
tagline.png
fonts/Sans-Regular.ttf, fonts/Sans-Bold.ttf, fonts/Sans-Italic.ttf, fonts/Sans-BoldItalic.ttf
fonts/Mono-Regular.ttf  (optional)
template.potx           (optional)
```

```json
{
  "name": "acme",
  "footerText": "ACME Corporation",
  "colors": {
    "primary": "#0B5FFF",
    "link": "#0B5FFF",
    "table": { "header": "#0B5FFF", "band": "#E7EFFF", "line": "#C9D3E0" },
    "gradient": ["#0B5FFF", "#3FC1FF"]
  },
  "images": {
    "logo": "logo.png",
    "logoOnDark": "logo-on-dark.png",
    "coverArtwork": "cover-artwork.png",
    "tagline": "tagline.png"
  },
  "fonts": {
    "regular": "fonts/Sans-Regular.ttf",
    "bold": "fonts/Sans-Bold.ttf",
    "italic": "fonts/Sans-Italic.ttf",
    "boldItalic": "fonts/Sans-BoldItalic.ttf",
    "mono": "fonts/Mono-Regular.ttf"
  },
  "pptx": {
    "template": "template.potx",
    "layouts": { "title": "Title Slide", "content": "Title and Content" }
  }
}
```

API, in `wwosik.DocLibs.DocumentGeneration` — all four sources (decided); the first three are
thin wrappers around the callback:

```csharp
public static class BrandPack
{
    public static Brand Load(Func<string, Stream?> openFile);             // any source: blob storage, database, …
    public static Brand Load(string directory);
    public static Brand Load(Stream zip);
    public static Brand Load(Assembly assembly, string resourcePrefix);   // embedded resources
}
```

- **JSON** read with `System.Text.Json` source generation, so the package stays AOT-compatible.
  Unknown keys throw (catches typos), like front matter.
- **Every path is relative to the pack**; `..`, absolute paths and links outside it throw.
- **Errors** throw `InvalidDataException` naming the pack file and the JSON key.
- The directory overload is the library's only file-system read outside `OfficeConverter`, and
  happens only when the caller asks for it.

### 7. How a client ships its brand

Documented in `docs/`, with a made-up client:

- **In code** — the app builds a `Brand` and passes it. Simplest for one app.
- **As a private NuGet package** — e.g. `Acme.DocLibs.Brand`, referencing
  `wwosik.DocLibs.DocumentGeneration`, embedding a brand pack and exposing
  `public static Brand Acme { get; } = BrandPack.Load(typeof(AcmeBrand).Assembly, "Brand.");`.
  Recommended for a brand shared by several apps; it stays in the client's own feed and repo.
- **As files** — a brand pack folder or zip deployed next to an app or kept in a document repo,
  for people who maintain the design without writing code.

### 8. Tests, samples, docs

- A made-up brand `sample` built by `tools/sample-generator` (simple coloured PNGs for logo,
  logo on dark, artwork and tagline) with **Liberation Serif** as its fonts (OFL, like the
  bundled Sans — different enough to prove the fonts are switched), committed as a brand pack
  under the generator tests' `Samples/brand-sample/`, with `OFL.txt` beside the fonts.
- Tests: the drawing paths that are untested today (footer text, logos, artwork, tagline, show /
  hide), colour defaults, validation errors (colours, images, missing text face, CFF font),
  brand fonts embedded in the PDF and named in the PPTX theme, family name read and overridden,
  glyphs missing from a brand font throwing or being replaced, `BrandPack` from callback /
  directory / zip / embedded resources, path escape attempts, front matter choosing a registered
  brand and rejecting an unknown one, a brand with its own `.potx`.
- Round-trip expectations `*-sample.roundtrip.expected.md` next to the neutral ones.
- Docs: a *Brands* chapter in `docs/wwosik.DocLibs.DocumentGeneration.md` (including the font
  licensing warning), the template tables and front-matter keys in the PPTX and PDF docs, the
  *Fonts* chapter of the PDF docs, and the *Your own design* section of `README.md`.

## Acceptance criteria

- [ ] `Brand`, `BrandColors`, `BrandImage`, `BrandFonts`, `BrandFont`, `PptxBrandTemplate`,
      `BrandPack`, `BrandTemplateOptions` and `BrandPdfTemplateOptions` are public and
      documented; `NeutralTemplateOptions` and `NeutralPdfTemplateOptions` are gone; nothing in
      the repo names a real client.
- [ ] A brand defined with only `Name`, `Colors.Primary` and `Logo` produces a valid deck and a
      PDF/A-2b document (veraPDF passes) showing that colour and logo.
- [ ] Footer text, logo on dark, cover artwork and tagline appear where documented in both
      generators, and `ShowTagline = false` / `ShowDate = false` leave them out.
- [ ] A brand with fonts produces PDFs that embed only those faces (plus Liberation Mono when it
      has no mono) and pass veraPDF, and decks whose theme names the brand's family.
- [ ] A missing text face or a CFF-outlined font is rejected with a message naming it.
- [ ] Front matter `template: <name>` selects a registered brand; an unregistered name throws,
      listing the registered ones; front matter cannot cause any file to be read.
- [ ] `BrandPack.Load` works from a callback, a directory, a zip and embedded resources, and
      rejects unknown keys, missing files, wrong image or font formats and paths escaping the
      pack, naming the key.
- [ ] A brand with a `template.potx` builds decks on that template; its PDFs use its colours,
      images and fonts.
- [ ] `wwosik.DocLibs.DocumentGeneration` and `.Pdf` stay `IsAotCompatible` without warnings.
- [ ] All tests pass on Linux; the docs describe exactly what was built.

## Out of scope

- A tool that drafts a brand pack from a client's existing `.potx` — a later plan, once the
  brand pack format has settled.
- Checking font licences (`OS/2 fsType`) — documented as the caller's responsibility.
- Converting CFF-outlined fonts to TrueType.

## Open questions

1. Replace `NeutralTemplateOptions` / `NeutralPdfTemplateOptions` with
   `BrandTemplateOptions` / `BrandPdfTemplateOptions`, or keep the neutral records alongside as
   shorthands?

> Replace them.

2. Should the front-matter key stay `template`, or become `brand`?

> Stays `template`.

3. Brand pack sources: all four overloads (directory, zip, embedded resources, callback), or
   fewer for a start?

> All four.

4. Should a brand also choose the PDF fonts?

> Yes, in this plan.

5. Should the PPTX built-in master take a theme font from the brand?

> Yes — the family name of the brand's PDF fonts, no separate field.

6. Is a helper tool wanted that drafts a brand pack from a client's existing `.potx`?

> Later, in a separate plan.

7. First nuget.org release: before this plan, or only after it?

> After this plan.

8. Which faces of a brand font are required?

> The four text faces; mono is optional and falls back to Liberation Mono.

9. OpenType fonts with CFF outlines?

> TrueType outlines only; CFF is rejected with a clear message.

10. Fonts whose licence restricts embedding (`OS/2 fsType`)?

> No check in the library; a warning in the docs.

11. Where does the PPTX theme font's family name come from?

> Read from the regular face's `name` table, overridable with `BrandFonts.Family`.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### What was built

- **Public API** in `wwosik.DocLibs.DocumentGeneration` (`Brand.cs`, `BrandPack.cs`) as planned:
  `Brand`, `BrandColors`, `BrandImage`, `BrandFonts`, `BrandFont`, `PptxBrandTemplate`,
  `BrandPack` with all four `Load` overloads. Internally `ResolvedBrand` (validation, defaults),
  `BrandRegistry` (what front matter may pick), `TrueTypeInfo` (outline check, family name).
- **Options:** `BrandTemplateOptions` (PPTX) and `BrandPdfTemplateOptions` (PDF) replace the
  neutral records; `PptxOptions.Brands` / `PdfOptions.Brands`; front-matter key `showTagline`.
- **PDF:** `FontSet.For(brand)` builds the font set from the brand's faces, Liberation Mono
  filling in missing mono faces. **PPTX:** the theme's major and minor font is the brand's family.
- **Tests:** 55 in the shared package (new), 64 PPTX, 61 PDF; every existing theory now also runs
  for the made-up `sample` brand pack (`src/wwosik.DocLibs.DocumentGeneration.Tests/Samples/brand-sample/`,
  linked into both generator test projects; Liberation Serif as its fonts).
- **Docs:** *Brands* chapter in `docs/wwosik.DocLibs.DocumentGeneration.md`; template tables,
  front-matter keys, fonts and known limits in the PPTX and PDF docs; `README.md` *Your own
  design*; `docs/README.md` on the sample brand; `ai/memory.md`.

### Deviations from the plan

- **PPTX cover artwork** is not limited to 42 % of the height as in the PDF: on a 16:9 slide that
  covered the title (seen when the deck was rendered). It now sits in the bottom right corner,
  at most as high as the band below the subtitle.
- **Default gradient:** `Primary` → `Link` as planned, but when both are the same colour (the
  default) it runs to `Primary` at 60 % on white — a one-colour gradient would be flat.
- **The caller's own template brand is always selectable** by front matter, besides `neutral` and
  `Brands` — otherwise `template: <its own name>` would fail, which the existing rule ("naming the
  caller's template keeps its values") requires.
- **A brand with its own `.potx` accepts no `showDate` / `showTagline`** in front matter (like
  `CustomTemplateOptions`, it never gets a date and has no built-in cover to put a tagline on).
- **A different brand named `neutral`** in `Brands` is rejected, as are two different brands
  sharing a name.

### Changed along the way

- The PPTX master's cover artwork was drawn with geometry made for one particular image (a crop
  and tilted clip path from the earlier internal repo). Replaced by plain aspect-preserving
  placement. Logos and the tagline were drawn into fixed-size boxes regardless of their aspect
  ratio; they are now fitted into those boxes, aligned right.
- PPTX image parts were always declared PNG; a JPEG brand image is now stored as `image/jpeg`.
- The PPTX footer text run carried `lang="de-DE"`; now `en-US` like every other run.
- The neutral theme is named `neutral` (was `Neutral`): the theme name is the brand name.
- `Brand.Neutral` lost its double byte-order mark; new `.cs` files have the single BOM
  `.editorconfig` asks for. `.gitattributes` marks `*.ttf` binary.

### Verified

- Release build without warnings, including the AOT and trimming analyzers of the
  `IsAotCompatible` packages. All tests pass on Linux (WindowsOnly skipped there; its code was
  not touched).
- veraPDF 1.30.3, PDF/A-2b profile: `report-sample.pdf`, `long-sample.pdf` (brand fonts, images)
  and `report-neutral.pdf` are compliant, 0 failed rules.
- `deck-sample.pptx` opened and exported slide by slide by PowerPoint; cover, content slide and
  section divider and `report-sample.pdf` pages 1–2 looked at as PNGs.

### Worth a look during verification

- `.tmp/review/deck-sample.pptx` and `.tmp/review/report-sample.pdf` (regenerated by the tests).
- The default colour derivations (`ResolvedBrand.Create`): 10 % band, 60 % gradient end, the
  five neutral accents in a derived theme.

### Loose ends

- **Not checked by a real Native AOT publish** — only by the analyzers. Worth one `PublishAot`
  run of a small app that calls `BrandPack.Load` and `PdfGenerator.Generate`.
- The JPEG test uses a header-only JPEG: it proves the part type and the aspect ratio, not that
  PowerPoint renders it.
- `BrandPack.Load(directory)` resolves a symbolic link on the file itself; a linked *directory*
  inside the pack pointing outside is caught only by the path check, not followed.
- The PPTX footer text box has a fixed width without wrapping: a long `FooterText` runs into the
  date (documented as a known limit).
- The sample brand's Liberation Serif adds about 1.5 MB of fonts to the repo's test samples.
- Later plans already agreed: a tool drafting a brand pack from a client's `.potx`; the first
  nuget.org release.
