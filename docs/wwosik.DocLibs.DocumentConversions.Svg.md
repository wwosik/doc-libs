# wwosik.DocLibs.DocumentConversions.Svg

Converts SVG to PNG with [SkiaSharp](https://github.com/mono/SkiaSharp) and
[Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) (both MIT), lets the document generators
place `.svg` images, and paints previews of decks: each slide as a PNG and a contact sheet, also for
`DocumentTool`. Depends on [wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md)
and, for `DeckContactSheet`, on
[wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md); AOT-compatible (a
Native AOT app publishes and runs with it without warnings).

```csharp
using wwosik.DocLibs.DocumentConversions.Svg;

byte[] png = SvgConverter.ToPng(File.ReadAllBytes("plan.svg"));                       // twice its size
byte[] wide = SvgConverter.ToPng(svgText, new SvgPngOptions { Width = 1200, Background = "FFFFFF" });

var options = new PptxOptions { ImageConverters = [new SvgImageConverter()] };        // .svg images in decks
var pdfOptions = new PdfOptions { ImageConverters = [new SvgImageConverter()] };      // and in PDFs
```

## Converting

- **Any SVG.** Its natural size is its `width` and `height`, else its `viewBox`, else its content
  (`SvgConverter.Size` returns it). The PNG is drawn at `SvgPngOptions.Scale` times that (default 2)
  or at `Width` pixels wide, at most 16 384 pixels a side.
- **Background:** transparent unless `Background` (`RRGGBB`) is set.
- **Fonts:** text is set in `Fonts` (TrueType or OpenType files, such as a brand's) when one has the
  `font-family` asked for — matched by family name whatever its width, in the nearest weight —
  else in the system's fonts, else in the default font.
- **Nothing outside the SVG is read.** Scripts never run; before drawing, the SVG loses its
  scripts, `foreignObject` elements, DTD, every link (`href`) that is not to its own elements or a
  `data:` URI, and the same in CSS `url()` values and `@import` rules. So an SVG from an untrusted
  source cannot reach files or the network — Svg.Skia itself would load an `<image>` from a file or
  a web address.
- **Errors:** an SVG that cannot be read or drawn throws `DocumentConversionException` with
  `Format = Svg`.

## In the generators

`SvgImageConverter` is an `IImageConverter` (see
[Image converters](wwosik.DocLibs.DocumentGeneration.md#image-converters)). It takes resources of
type `image/svg+xml`, with a path ending in `.svg`, or whose content starts like SVG, and returns a
PNG at the scale the generator asks for, with the SVG's natural size, at which the image is placed.
Its `Fonts` work as above.

- **Decks** place the PNG and keep the SVG beside it in PowerPoint's SVG extension (`asvg:svgBlip`):
  PowerPoint 2016 and later draw the SVG, other viewers the PNG.
- **PDFs** embed the PNG.

## Deck previews

`DeckPreview` paints each slide of a deck as a PNG, and all slides as one contact sheet, with no
browser, no Office and no network — for thumbnails, chat replies, review pages and documentation.
It paints the slide SVGs that `DeckSvg.Write` in
[wwosik.DocLibs.DocumentGeneration.Pptx](wwosik.DocLibs.DocumentGeneration.Pptx.md#slide-svgs-for-previews)
writes from any `.pptx`: the same drawing as that package's HTML deck, so a preview breaks lines
where the HTML deck does and shows what it shows. A host that wants previews references both
packages and makes two calls; `DeckPreview` itself takes only `SlideSvg` from
[wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md#slide-svgs), and
`DeckContactSheet` (below) makes both calls for you.

```csharp
using wwosik.DocLibs.DocumentConversions.Svg;
using wwosik.DocLibs.DocumentGeneration.Pptx;

using var pptx = File.OpenRead("review.pptx");
var svgs = DeckSvg.Write(pptx);                                   // svgs.Notes: what could not be drawn
var previews = DeckPreview.Render(svgs.Slides, new() { Fonts = svgs.Fonts });
File.WriteAllBytes("slide-1.png", previews.Slides[0].Png.ToArray());
File.WriteAllBytes("sheet.png", DeckPreview.ContactSheet(previews, new() { LabelFont = svgs.LabelFont }));

var thumbnail = DeckSvg.Write(pptx2, new() { Slides = [1] });     // reads and draws slide 1 only
```

`DeckPreview.Render(slides, options)` returns a `DeckPreviewResult` whose `Slides` hold, in order,
a `SlidePreview` per slide: `Number` (its position in the deck), `Title` and `Png`.

| `DeckPreviewOptions` | Default | |
| -------------------- | ------- | - |
| `Width` | 1280 | Pixels per slide, across; the height follows the slide (720 on 16:9, 960 on 4:3). At most 16 384. |
| `Fonts` | none | The faces to paint with: pass `DeckSvgResult.Fonts`. |
| `Background` | `FFFFFF` | What shows behind a slide without a background, as in PowerPoint. |

`DeckPreview.ContactSheet(previews, options)` returns one PNG: the slides scaled down with
high-quality sampling, in rows, each with a hairline grey border and its slide number centred under
it. Rows are as tall as their tallest thumbnail; a deck with fewer slides than columns gives a
narrower sheet.

| `ContactSheetOptions` | Default | |
| --------------------- | ------- | - |
| `Columns` | 4 | Thumbnails per row. |
| `ThumbnailWidth` | 320 | Pixels per thumbnail, across. |
| `Gap` | 16 | Pixels around and between the thumbnails. |
| `Labels` | `true` | The slide number under each thumbnail (the number only, never the title). |
| `LabelFont` | none | The labels' TrueType face: pass `DeckSvgResult.LabelFont` (Liberation Sans). Without one, SkiaSharp's default typeface; a platform without any gets no labels. |
| `Background` | `F2F2F2` | The sheet behind the thumbnails. |

- **Size limit.** A sheet larger than 16 384 pixels a side is refused with an `ArgumentException`
  that names the largest `ThumbnailWidth` that fits (100 slides of 16:9 in one column fit at 228 px;
  the defaults fit about 290 slides).
- **Errors.** A slide that Svg.Skia cannot paint throws `DocumentConversionException` with
  `Format = Svg`, naming the slide's number. A `Width` out of range throws
  `ArgumentOutOfRangeException`. What the reader could not draw (`drawing-unsupported`) and which
  fonts it measured by stand-in or estimate (`font-missing`) are in `DeckSvgResult.Notes`; the
  preview itself has no notes.
- **Build slides** are painted in their final state: everything any step shows, so a thumbnail
  looks complete. Only PNG is written.
- **Fonts and stand-ins.** Each run in a slide SVG names the face its line was measured with: a face
  embedded in the deck (the TrueType inside its EOT), one passed in `DeckSvgOptions.Fonts`, or the
  bundled Liberation Sans and Mono standing in for Arial, Helvetica, Courier New, Consolas and the
  like. A family with no face at all is painted in Liberation Sans, and the reader has reported it.
  `DeckSvgResult.Fonts` carries every one of those faces once, so a slide is never painted in a
  random system face — also in a container without fontconfig
  (`SkiaSharp.NativeAssets.Linux.NoDependencies`, see below), where the system has none.
- **Speed.** A slide at 1280 px takes a few tens of milliseconds; the contact sheet scales the PNGs
  already painted.

**Known limits.** The preview draws what the HTML deck draws and has its limits (see
[HTML deck](wwosik.DocLibs.DocumentGeneration.Pptx.md#html-deck)): effects such as shadows are not
painted, text has no kerning, ligatures or hinting, and line pitch, first baselines and bullet
positions follow the reader's rules, which have not been compared with PowerPoint's exports yet.
Svg.Skia's anti-aliasing differs slightly from a browser's.

## Native libraries

SkiaSharp and HarfBuzzSharp load native libraries. Windows and macOS come with the packages; Linux
needs one more package in the application:

- `SkiaSharp.NativeAssets.Linux` where fontconfig is installed (most distributions): system fonts
  are found by name.
- `SkiaSharp.NativeAssets.Linux.NoDependencies` in minimal containers without fontconfig: no system
  fonts are found, so pass the fonts the SVGs use in `Fonts`.

## Known limits

- Rendering is Svg.Skia's: SVG 1.1 and most of SVG 2, but no animation, and text layout can differ
  slightly from a browser's.
- External images, stylesheets and fonts referenced by the SVG are never loaded (see above): embed
  them as `data:` URIs.

## Contact sheets for DocumentTool

`DeckContactSheet` is the `IDeckPreview` that
[`DocumentTool`](wwosik.DocLibs.DocumentGeneration.md#contact-sheet) calls for each deck it writes:
it reads the deck with `DeckSvg.Write`, paints the slides with `DeckPreview.Render` at twice the
thumbnail width, and lays them out with `DeckPreview.ContactSheet`, four to a row, numbered, labels in
Liberation Sans. At most `IDeckPreview.MaxSlides` (60) slides, from the first; the same deck gives the
same PNG.

```csharp
var tool = new DocumentTool([PptxGenerator.ForOptions(options)], new() { Preview = new DeckContactSheet() });
var wider = new DeckContactSheet(sheet: new ContactSheetOptions { Columns = 6, ThumbnailWidth = 240 });
```

- **`new DeckContactSheet(fonts, sheet)`**: `fonts` are TrueType files for families the decks name but
  do not embed (`DeckSvgOptions.Fonts`; generated decks embed brand fonts by default); `sheet` the
  `ContactSheetOptions`, its `LabelFont` defaulting to Liberation Sans.
- **`ContactSheet(pptx, result)`** returns `null` for empty bytes or a deck without slides. It
  throws `DocumentConversionException` when the deck cannot be read or a slide painted (`DocumentTool`
  then replies without a sheet), and `ArgumentException` when `sheet` makes the sheet larger than
  16 384 pixels a side (a host error: `DocumentTool.Run` passes it on). The defaults fit 60 slides.
