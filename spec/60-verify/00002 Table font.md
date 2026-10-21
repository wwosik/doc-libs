# 00002 Table font

> Status: verify — implemented 2026-10-22

## Summary

Let a brand set a separate font family for tables — typically a condensed cut of its text font,
so wide tables fit without shrinking the text. It works in both generators: the PDF generator
embeds the faces, and the PPTX generator names the family in table cells.

## Context

- `BrandFonts` (`src/wwosik.DocLibs.DocumentGeneration/Brand.cs`) holds four required text faces
  (`Regular`, `Bold`, `Italic`, `BoldItalic`), optional `Mono` / `MonoBold`, and an optional
  `Family` name. `ResolvedBrand.ResolveFonts` validates them (TrueType outlines only, via
  `TrueTypeInfo`) and resolves the family name from the regular face's `name` table.
- **PDF:** `FontFace` (`src/wwosik.DocLibs.DocumentGeneration.Pdf/Fonts.cs`) has six faces.
  `FontSet.For(ResolvedBrand)` maps them to files. `LayoutEngine.Table` sets cells at a fixed
  `TableSize` of 9.5 pt. It wraps, measures column widths (`ColumnWidths`) and draws them with
  the same faces as body text, through `Face(RunStyle)`. The header row is bold.
- **PPTX:** the theme's heading and body font is the brand's family (`TemplateBuilder`,
  `fontScheme`). `DeckWriter.Table` builds cells from runs (`DeckWriter.Run`) and sets an
  explicit `a:latin` only for code (`PptxOptions.CodeFont`). Row heights come from
  `EstimateLines`, a character-count heuristic.
- Brand packs map `brand.json` → `BrandManifest.FontsSection`
  (`src/wwosik.DocLibs.DocumentGeneration/BrandPack.cs`). Unknown keys are rejected.
- Documentation: [docs/wwosik.DocLibs.DocumentGeneration.md § Fonts](../../docs/wwosik.DocLibs.DocumentGeneration.md#fonts),
  and the Fonts sections of the PDF and PPTX docs.

## Proposed approach

### 1. API

```csharp
public sealed record BrandFonts
{
    // ... existing faces ...
    public BrandTableFonts? Table { get; init; }        // null: tables use the text faces
}

public sealed record BrandTableFonts
{
    public required BrandFont Regular { get; init; }
    public required BrandFont Bold { get; init; }        // header row, **bold** in cells
    public BrandFont? Italic { get; init; }              // default: Regular
    public BrandFont? BoldItalic { get; init; }          // default: Bold
    public string? Family { get; init; }                 // default: from Regular's name table
}
```

- Table fonts live inside `BrandFonts`, so a brand without its own text fonts cannot have table
  fonts. A condensed cut belongs to the brand's family anyway. (Liberation has no condensed
  cut.)
- A missing italic face falls back to the upright face **of the table family**, not to the text
  italic. Mixing families in one cell looks worse than losing the slant. Nothing is
  synthesized.
- Validation is the same as for the text faces: TrueType outlines only, and errors name the
  property (`Fonts.Table.Bold has CFF (PostScript) outlines…`). Equality compares by content,
  like the other font records.
- `ResolvedFonts` gains `TableRegular`, `TableBold`, `TableItalic`, `TableBoldItalic` and
  `TableFamily`. Without table fonts, these are the text faces and the text family.

### 2. Brand pack

```json
"fonts": {
  "regular": "fonts/House-Regular.ttf",
  "bold": "fonts/House-Bold.ttf",
  "italic": "fonts/House-Italic.ttf",
  "boldItalic": "fonts/House-BoldItalic.ttf",
  "table": {
    "regular": "fonts/HouseCondensed-Regular.ttf",
    "bold": "fonts/HouseCondensed-Bold.ttf",
    "family": "House Condensed"
  }
}
```

The keys are `fonts.table.regular` / `bold` (required in the section), `italic` / `boldItalic` /
`family` (optional). Error messages name the full key, e.g. `fonts.table.bold`.

### 3. PDF

- Add four faces to `FontFace`: `TableRegular`, `TableBold`, `TableItalic`, `TableBoldItalic`.
  `FontSet.For` maps them to the table files, or to the text files when the brand has none.
- **Embed each distinct file once.** When a face reuses the bytes of another face (no table
  fonts, or a missing table italic), the renderer must share the embedded font and its subset,
  so the PDF does not grow. Check `PdfRenderer`'s font slots, and key them by file rather than by
  face if needed.
- `LayoutEngine` picks faces through a `table` flag on the wrapping / measuring path
  (`Pieces`, `Wrap`, `ColumnWidths`, `Row`). Inside a table, `Face(RunStyle)` returns the table
  faces. Inline code in a cell stays mono. `TableSize` stays 9.5 pt.
- Glyph rules are unchanged. A character missing from a table face is handled exactly like one
  missing from a text face: it throws, or is replaced under `PdfOptions.ReplaceUnsupported`, and
  the message names the face.

### 4. PPTX

- In `DeckWriter.Table`, every non-code run gets `<a:latin typeface="{TableFamily}"/>`
  (`a:ea` / `a:cs` are left alone). Without table fonts, nothing is written, and cells keep the
  theme font as today.
- This also applies when the brand brings its own `.potx` (`Brand.Pptx`). It does not apply
  with `CustomTemplateOptions`, which has no brand.
- `EstimateLines` stays character-based. A condensed font only makes rows look roomier than
  estimated, never overflow.
- Nothing is embedded. As with the theme font, PowerPoint substitutes a family that is not
  installed.

### 5. Tests, samples, docs

- Sample brand pack: add a condensed OFL family under `fonts/table/` with its `OFL.txt`, and
  reference it in `brand.json`. Candidate: **Roboto Condensed** or **Open Sans Condensed**,
  whichever has TrueType outlines and the smaller files. Check the size, because the pack is
  linked into three test projects.
- Shared tests: resolving falls back to the text faces; italic falls back to regular, bold
  italic to bold; `Family` is overridden or read from the `name` table; a CFF table face is
  rejected with its property name; brand pack keys and their error messages; equality.
- PDF tests:
  - the table text is drawn in the table face (check the font names in the PDF);
  - a narrower face gives narrower columns than the text face on the same table;
  - one embedded font when the brand has no table fonts;
  - veraPDF on `report-sample`.
- PPTX tests: cells carry `a:latin` with the table family, and carry none without table fonts.
- Regenerate the `*-sample` expected files, and check a table-heavy page in the PDF and a table
  slide in PowerPoint visually.
- Docs: a Table fonts paragraph in § Fonts and in the brand pack table, the README "Your own
  design" list, the package descriptions if they list font features, and `ai/memory.md`.

## Acceptance criteria

- [x] `BrandFonts.Table` exists, with the fallbacks above, validated like the text faces.
- [x] Brand packs load `fonts.table`, and invalid keys are rejected by name.
- [x] The PDF draws table cells in the table faces, sizes columns by their widths, embeds each
      file once, and stays PDF/A-2b valid.
- [x] The PPTX sets the table family on table runs only when the brand has table fonts.
- [x] Without table fonts, both generators produce output identical to today's (the existing
      expected files are unchanged).
- [x] Docs and the sample brand are updated; all tests pass; Release build has 0 warnings.

## Out of scope

- A separate table text size, or auto-shrinking wide tables.
- Table fonts without brand text fonts, or on the Liberation default.
- Fonts for other elements (headings, footer, code) beyond what exists.

## Open questions

1. Table fonts: a separate family with Regular + Bold required, and Italic / BoldItalic optional?

> Yes — a separate family; Regular and Bold required, the italics optional.

2. Both generators, or PDF only?

> PDF and PPTX.

## Notes

## Implementation notes

### Decisions

- **The table flag is `RunStyle.Table`**, an internal flag the layout ORs into the base style of
  every cell (`LayoutEngine.CellStyle`), never set by the parser. It flows through `Supported`,
  `Pieces`, `Wrap` and `ColumnWidths` unchanged, and `Face(RunStyle)` maps it to the four
  `Table*` faces. Code keeps priority, so inline code in a cell stays mono.
- **Embedding each file once:** without table fonts, `ResolvedFonts` and `FontSet` hold the
  *same arrays* for the table faces as for the text faces. `PdfRenderer` keys its font copies
  by file (reference equality) rather than by face, and `FontSet` parses each distinct array
  once. A missing table italic shares the upright table array in the same way.
  `ResolvedFonts.HasTableFonts` is that reference check.
- **PPTX:** `ResolvedBrand.TableFontFamily` is `null` without table fonts. `PptxGenerator`
  passes it to `DeckWriter` only for templates that carry a brand (`BrandValue`), which covers
  `Brand.Pptx` and excludes `CustomTemplateOptions`. Table cells pass it on to `Run`, which
  writes `a:latin` for every non-code run.
- **Sample font: PT Sans Narrow** (ParaType, OFL 1.1, from `google/fonts` `ofl/ptsansnarrow`),
  regular and bold, under `brand-sample/fonts/table/` with its `OFL.txt`. It has static TrueType
  outlines and covers latin-ext and Cyrillic. Roboto Condensed and Archivo Narrow are now
  variable fonts only, and Open Sans Condensed has no regular weight. The two files add about
  880 KB to the pack. The sample has no table italics, so the fallback is exercised.

### Changed expectations

- `long-sample.roundtrip.expected.md`: one page break moved, because the condensed table rows
  are shorter. Every `*-neutral` expectation and `report-sample` are unchanged.

### Verified

- Release build with 0 warnings; all tests pass (WindowsOnly skipped on Linux).
- veraPDF 1.30.3, PDF/A-2b: `report-sample.pdf`, `long-sample.pdf` and `report-neutral.pdf`
  pass.
- Looked at as PNGs: `report-sample.pdf` page 2, where the table is in PT Sans Narrow and the
  body in Liberation Serif, and `deck-sample.pptx` slide 3, a table slide exported by PowerPoint
  in which the cells show PT Sans Narrow.

### Worth a look during verification

- PowerPoint showed PT Sans Narrow because the font is installed on this machine. Where a family
  is missing, PowerPoint substitutes another, as it does for the theme font.
- The glyph error names the face, e.g. `(TableRegular)`, rather than the property path.
