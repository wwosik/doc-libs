# 00018 SVG to PNG

> Status: verify — implemented 2026-10-28

## Summary

A converter that turns any SVG into a PNG, in a new package
`wwosik.DocLibs.DocumentConversions.Svg`. The generators use it so that an `.svg` image in the
Markdown works in decks and PDFs. It also gives the charts of 00017 and the Mermaid diagrams a
PNG form for places that take no SVG, such as e-mail.

## Context

- **What the generators accept today:**
  - Decks take PNG, JPEG or GIF and PDFs take PNG or JPEG (`ImageInfo`). Any other image throws
    `DocumentConversionException`.
  - An `.svg` image in the Markdown is an error in both generators, although the resource collector
    already knows the `image/svg+xml` type.
- **No parser fits:** `SvgDrawing` in the Diagrams package reads only the SVG subset Mermaider
  writes into a `Drawing`, and throws on anything else.
- **No encoder:** there is no PNG encoder in the repo.
- **00017** adds a `Drawing` → SVG writer, so every chart and diagram the library draws can become
  an SVG, and with this converter a PNG.
- Full SVG with real text rendering needs a mature rasteriser. SkiaSharp (MIT) with Svg.Skia (MIT)
  is the established choice in .NET. It brings a native library per platform, so it gets a package
  of its own and no other package depends on it.

## Proposed approach

1. **Package:** `wwosik.DocLibs.DocumentConversions.Svg`.
   - It references `SkiaSharp` and `Svg.Skia`, which are added to `Directory.Packages.props`. It
     depends on DocumentConversions only.
   - Windows and macOS native assets come with SkiaSharp. Linux needs a native-assets package: the
     test project references `SkiaSharp.NativeAssets.Linux`, and the docs say which one to add for
     containers with and without fontconfig.
2. **API:**
   - `SvgConverter.ToPng(ReadOnlySpan<byte> svg, SvgPngOptions? options = null)` → `byte[]`.
   - **Options:**
     - `Scale`: pixels per SVG pixel, default 2;
     - or `Width`: the width in pixels, with the height following the aspect ratio;
     - `Background`: default transparent;
     - `Fonts`: font files to use for the SVG's `font-family` names before the system fonts. Brand
       fonts can be passed here.
   - **Size:** taken from the SVG's `width`/`height`, else its `viewBox`. An SVG with neither needs
     `Width` and a height option, or it throws.
   - **Errors:** an SVG that cannot be read or drawn throws `DocumentConversionException`, with a
     new `DocumentFormat.Svg` and the reason.
   - A missing font falls back to the system's default typeface, never to no text.
3. **Generators use it** when they are given it:
   - **Hook:** `IImageConverter` in DocumentConversions, with
     `bool CanConvert(DocumentResource)` and `DocumentResource Convert(DocumentResource, double width)`.
     `SvgImageConverter` in the new package implements it.
   - **Options:** `PptxOptions.ImageConverters` and `PdfOptions.ImageConverters`, empty by default.
     Without a converter an SVG image stays an error, so nothing changes for existing callers.
   - **Size:** the PNG is rendered at twice the size the image is placed at, so it is sharp on the
     slide or page. The image's natural size, used for the 00012 rules, is the SVG's own size.
   - **Decks (proposed):** the original SVG is also written as PowerPoint's SVG extension
     (`asvg:svgBlip`) beside the PNG. PowerPoint 2016+ then shows it as a vector; other viewers
     show the PNG.
4. **Drawings:** `SvgConverter.ToPng(Drawing, …)` as a convenience, via 00017's `DrawingSvg.Write`.
   The hover parts are left out of a PNG.
5. **AOT:** check that the package publishes with Native AOT without warnings. If Svg.Skia warns,
   the package is marked not AOT-compatible and the docs say so; the other packages stay as they
   are.
6. **Tests:**
   - Rendering:
     - pixel colours at known points of a simple SVG;
     - size from `width`/`height`, from `viewBox` and from the options;
     - transparent and coloured backgrounds;
     - text drawn in a font passed in `Fonts`;
     - an invalid SVG (throws).
   - Generators: an `.svg` image in a deck and a PDF with the converter, and an error without it.
   - Rendering a Mermaid diagram and a 00017 chart via `DrawingSvg` to PNG.
   - CI on Linux.
7. **Docs:**
   - a package page, with native assets per platform and fonts in containers;
   - the PPTX and PDF pages: SVG images;
   - the authoring guides: `.svg` images, where the converter is enabled;
   - the README package table and the CHANGELOG.

## Acceptance criteria

- [ ] Any SVG converts to a PNG of the requested size, with text in the given or system fonts.
- [ ] Decks and PDFs accept `.svg` images when the converter is in their options, and are unchanged
      without it.
- [ ] Charts and diagrams the library draws can be written as PNG.
- [ ] The native dependency lives only in the new package; CI passes on Linux.
- [ ] Tests, docs, the authoring guides and the CHANGELOG are updated.

## Breaking changes

- None. It is a new package, and the image converters are opt-in options.

## Open questions

1. Which SVG does the converter take: any SVG, or only drawings the library makes?

> Answered by the human (2026-10-26): any SVG.

2. The rasteriser: SkiaSharp with Svg.Skia, or an own rasteriser without dependencies?

> Answered by the human (2026-10-26): SkiaSharp with Svg.Skia.

3. Where does it live: a new package, or the Charts package?

> Answered by the human (2026-10-26): a new package, `wwosik.DocLibs.DocumentConversions.Svg`.

4. Do the generators use it for `.svg` images, or is it an API only?

> Answered by the human (2026-10-26): the generators use it for `.svg` images, when it is in their
> options.

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Decisions

- **Package `wwosik.DocLibs.DocumentConversions.Svg`:**
  - `SvgConverter.ToPng` (bytes or string), `SvgConverter.Size`, `SvgPngOptions` (`Scale` 2, `Width`,
    `Background`, `Fonts`) and `SvgImageConverter`.
  - It references SkiaSharp 4.153.1 and Svg.Skia 5.2.3, and the tests `SkiaSharp.NativeAssets.Linux`.
- **Safety:** `SvgSanitizer` removes scripts, `foreignObject`, DTDs, and external links (`href`, CSS
  `url()`, `@import`) before Svg.Skia sees the SVG. Svg.Skia loaded a local file through `<image>`
  under every one of its own settings. JavaScript is also switched off.
- **Fonts:** a provider of its own matches the passed fonts by family name and nearest weight,
  because Svg.Skia's matches width exactly and never finds a condensed font.
- **Hook:** `IImageConverter` and `ConvertedImage` in DocumentConversions; `DocumentFormat.Svg`.
  `PptxOptions.ImageConverters` and `PdfOptions.ImageConverters`, empty by default.
  - **Scale:** the generators ask for 2 (PDFs: 2 × the image's `scale=`), and place the image at the
    natural size the converter reports.
  - **Decks** also write the SVG as `asvg:svgBlip` beside the PNG.

### Deviations from the plan

- **No `SvgConverter.ToPng(Drawing)`:** that would make the package depend on DocumentGeneration. A
  drawing goes `DrawingSvg.Write` → `SvgConverter.ToPng` (documented, and tested with a Mermaid
  diagram and the financial charts).
- **An SVG without width, height or viewBox** is sized by its content, as Svg.Skia does, rather than
  being an error.
- **The plan's "twice the size it is placed at":** the PNG is drawn at twice the natural size, which
  is what PDFs place it at. A deck that enlarges the image more relies on the SVG for PowerPoint, and
  on the PNG for other viewers.
- **The package is AOT-compatible.** A Native AOT app publishes without warnings and runs.

### Breaking changes

- `DocumentFormat` has a new member, `Svg`. In `CHANGELOG.md`.

### Verified

- **Tests:** Svg 21 (pixels, sizes, background, a passed font, errors, nothing external loaded,
  internal links and `data:` URIs still drawn, a `.svg` image in a deck with its SVG extension and in
  a PDF at its natural size, an error without a converter, a Mermaid diagram and every financial
  chart to PNG). Release build 0 warnings; CI runs the tests on Linux.
- **Not verified:** the SVG-image deck (`.tmp/review/svg-image.pptx`) was not opened in PowerPoint
  (see 00017).

