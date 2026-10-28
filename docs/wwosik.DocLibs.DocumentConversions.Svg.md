# wwosik.DocLibs.DocumentConversions.Svg

Converts SVG to PNG with [SkiaSharp](https://github.com/mono/SkiaSharp) and
[Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia) (both MIT), and lets the document generators
place `.svg` images. Depends on
[wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md); AOT-compatible (a
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
