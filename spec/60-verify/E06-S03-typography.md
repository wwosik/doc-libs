# E06-S03 Typography

> Status: in verification
> Epic: [E06 Markdown syntax](../50-todo/E06-markdown-syntax.md)

## Summary

The generators set text the way a typesetter would, in the rules of the document's language:
straight quotes become typographic quotes (“…” in English, „…“ in German), `--` and a spaced hyphen
become dashes, `...` an ellipsis, and a no-break space keeps "10 km", "z. B." and "13. November"
together on one line. English and German profiles ship; a host can add more. Code is never touched.

The analysis lists typography and code highlighting as one story; they share nothing but the epic,
so code highlighting is E06-S04 (question 1).

## Context

- **Where text comes from:** both parsers collect runs in their `RunCollector`
  (`DeckParser.RunCollector`, `MarkdownParser.RunCollector`) and then pass them through
  `ColouredWords.Apply`, a shared, generic pass over runs (text getter, code test, rebuild
  callbacks) that masks code. A typography pass fits the same shape and the same place.
- **What else carries text:** front-matter `title` and `author` on the cover, table cells, footnote
  texts, alert titles (generated), drawings from renderers (Mermaid labels, chart labels), and the
  PDF footer and bookmarks (`Layout.Title`).
- **Markdig's SmartyPants** (`UseSmartyPants`) replaces quotes, `--`, `---`, `...` and `<<`/`>>` while
  parsing, as `SmartyPant` inlines. Its quotes come from one mapping per pipeline, and both
  pipelines are static (`DeckParser.Pipeline`, `MarkdownParser.Pipeline`), built once. It knows no
  spaced hyphen and no no-break spaces, and it decides each quote within one inline container, so a
  quote that opens in plain text and closes after `**bold**` can come out wrong.
- **Language:** `DocumentLanguage.Resolve` (E06-S02) gives the tag (`lang` in front matter, else
  `PptxOptions.Language` / `PdfOptions.Language`) and matches it in full, then by language, against
  the host's `Texts` and the built-in English and German.
- **Fonts:**
  - PDFs throw on a character the fonts lack, unless `PdfOptions.ReplaceUnsupported` replaces it
    (`LayoutEngine.Supported`, `character-replaced`); a missing face stays an error (epic decision).
    Liberation Sans has the quotes, dashes, ellipsis and U+00A0; a brand font may lack some, and
    U+202F (narrow no-break space) is missing from many fonts.
  - Decks: PowerPoint substitutes a font for a missing character. The brand's faces are known
    (`ResolvedFonts`), `TrueTypeInfo.HasCharacter` checks a code point, and the guides' *Characters*
    section already lists what the fonts lack (`AuthoringGuides.Characters`).
- **Converters:** the round-trip samples (`*.roundtrip.expected.md`) turn generated decks and PDFs
  back into Markdown, so any change to the text shows in them.

## Proposed approach

1. **Switching it on.**
   - A shared front-matter key `typography` (`true` / `false`), listed in both guides, and options
     `PptxOptions.Typography` and `PdfOptions.Typography` (`bool`, default `false`, question 2).
     Front matter wins over the options.
   - Off, nothing changes: the pass is not run.
2. **Profiles.**
   - A public record `TypographyRules` in DocumentGeneration:
     - `DoubleQuotes` and `SingleQuotes` (opening, closing), `Apostrophe`;
     - `Dash` (what a spaced hyphen becomes, with its spaces);
     - `Abbreviations`: abbreviations whose inner space becomes a no-break space (`z. B.`, `d. h.`,
       `u. a.`, `i. d. R.`, `S.`, `Nr.`, `Abs.`, `§`);
     - `Units`: symbols that keep the number before them on the line (`%`, `‰`, `°C`, `€`, `EUR`,
       `USD`, `$`, `km`, `m`, `cm`, `mm`, `kg`, `g`, `t`, `l`, `ml`, `h`, `min`, `s`, `kB`, `MB`,
       `GB`, `TB`, `px`, `pt`);
     - `DatePatterns`: day and month kept together (`13. November`, `13 November`, and the month
       names of `GeneratedTexts` once E04-S07 adds them).
   - `TypographyRules.English`: “ ” ‘ ’, apostrophe ’, spaced en dash, no abbreviations, the units.
     `TypographyRules.German`: „ “ ‚ ‘, apostrophe ’, spaced en dash, the German abbreviations, the
     units, the date pattern.
   - The host adds languages with `PptxOptions.TypographyRules` / `PdfOptions.TypographyRules`
     (`IReadOnlyDictionary<string, TypographyRules>`), matched like `Texts`. A language without
     rules uses English punctuation and gets no note beyond `language-unknown`, which E06-S02
     already reports for it.
3. **The pass.** A new internal `Typography.Apply` in DocumentGeneration, generic over the run type
   like `ColouredWords.Apply`, called by both parsers right after it, on every run list: paragraphs,
   headings, list items, table cells, footnotes, definition terms, and the cover's title and author.
   - It works on the concatenated text of a paragraph with code runs masked, so a quote that opens
     before `**bold**` and closes after it pairs correctly. Link targets are not text and are never
     changed; link text is.
   - **Only characters are replaced; spaces are never added or removed.** "10%" stays without a
     space; "10 %" gets a no-break space.
   - **Quotes:** a `"` or `'` after start, space, opening bracket or dash opens; otherwise it closes.
     A `'` between letters or after a digit (`it's`, `’90s`, `5'`) is the apostrophe; `5"` after a
     digit stays a straight double quote (inches).
   - **Dashes:** `---` → em dash; `--` → en dash; space-hyphen-space → the profile's dash. A hyphen
     between digits (`2020-2026`, `2026-11-14`, phone numbers) is never changed.
   - **Ellipsis:** exactly three dots → `…`.
   - **No-break spaces:** U+00A0 inside the profile's abbreviations, between a number and a following
     unit, between `§`/`Nr.`/`S.` and the number after it, and in the date patterns.
4. **Fonts.** A character is only introduced when the faces it will be set in have it:
   - PDFs check every face the run can be set in (text and table faces, regular to bold italic)
     with `TrueTypeInfo.HasCharacter`. Decks with a brand's fonts check them the same way; decks on a
     caller's template without known fonts get every replacement, since Office substitutes.
   - A replacement the font lacks is skipped and the character stays as typed, with one note per
     character, `typography-skipped` (question 4). The PDF can therefore never fail on a character
     the pass introduced.
5. **Not touched:** code spans, code blocks, front-matter values other than `title` and `author`,
   URLs, HTML shown as text, and the text inside drawings from renderers (Mermaid labels, chart
   categories), which keep exactly what the writer typed.
6. **Notes.** `NoteCodes.TypographySkipped` (`typography-skipped`): a typographic character was not
   used because the document's fonts lack it. In `NoteCodes.All` and the docs table.
7. **Dependencies and licences.** None new; Markdig's SmartyPants is not used (question 3).
8. **Guides, docs, changelog.**
   - Both authoring guides: the `typography` key and what it does in the document's language, and
     that writers may type straight quotes. Expected guides regenerated (with and without the
     option, to cover the key's description).
   - Docs: DocumentGeneration (`TypographyRules`, the pass, the note), Pptx and Pdf (the option).
   - `CHANGELOG.md`: new: typography, `TypographyRules`, options and key. No breaking change while
     it is off by default; if the answer to question 2 turns it on, it goes under breaking changes
     (every deck and PDF with quotes, dashes or units changes its text).
9. **Tests.** A table-driven test per rule and language (quotes across runs, apostrophes, inches,
   dashes, digit ranges untouched, ellipsis, abbreviations, units, dates); code spans and URLs
   untouched; a font without “ (made-up `IFontMetrics` and a subset brand font) gives the straight
   quote and `typography-skipped`; a host profile for `fr` (« » with no-break spaces inside); decks
   through the Open XML validator and PDFs through the PDF/A checks; the round-trip samples
   unchanged with the option off and regenerated for one sample with it on.

## Acceptance criteria

- [ ] With `typography: true` and `lang: en`, `"Hello" -- it's 10 km...` becomes
  `“Hello” – it’s 10 km…` (no-break space before `km`) in decks and PDFs.
- [ ] With `lang: de`, `"Hallo" - z. B. am 13. November` becomes `„Hallo“ – z. B. am 13. November`
  with no-break spaces inside `z. B.` and after `13.`.
- [ ] Code spans, code blocks, link targets, `2026-11-14` and `2020-2026` are never changed.
- [ ] A font lacking a typographic character keeps the typed one and gives `typography-skipped`; no
  PDF throws because of the pass.
- [ ] A host's `TypographyRules` for another language are used for it.
- [ ] Without the key or option, samples produce the same decks and PDFs as before.
- [ ] Decks pass the Open XML validator; PDFs stay PDF/A-2b.
- [ ] Guides, docs and CHANGELOG are updated, and all tests pass.

## Open questions

1. Split: typography here (E06-S03) and code highlighting as its own story (E06-S04) (proposed:
   different code, a dependency only for highlighting), or both in one story as the analysis has it?

> Answered by the human (2026-11-14): as proposed.

2. Default: off, switched on by `typography: true` or the option (proposed: existing documents
   keep their text), or on by default as a breaking change, since it is what most writers want?

> Answered by the human (2026-11-14): as proposed.

3. Own pass or Markdig's `UseSmartyPants`: an own pass over runs (proposed: per-language quotes
   without per-language pipelines, quotes across emphasis, dashes and no-break spaces, font checks),
   or SmartyPants for quotes, dashes and ellipsis plus an own pass only for no-break spaces?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. A character the fonts lack: keep the typed one with a note (proposed), keep it silently, or let
   PDFs throw as for any missing character?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Which no-break space: U+00A0 everywhere (proposed: every font has it, Office and PDF viewers
   handle it), or the narrow U+202F inside German abbreviations and before units, as typesetting
   rules ask, where the font has it?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. English dash: spaced en dash "a – b" (proposed: British style, same as German), or unspaced em
   dash "a—b" (American style), or a choice between `en-GB` and `en-US` profiles?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Swiss German (`de-CH`): the German profile through the language fallback (proposed), or a
   built-in `de-CH` profile with «…» guillemets?

> Accepted as proposed (the human, 2026-11-14, without discussion).

8. Drawings: leave the text of Mermaid diagrams and charts as typed (proposed: their labels are
   often identifiers), or apply the pass there too?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

### Breaking changes

None. Typography is off by default (question 2): without `typography: true` or the option, every deck,
PDF and Word document is as before (the existing round-trip samples are unchanged). Added:
`TypographyRules` (public record), `PptxOptions`/`PdfOptions`/`DocxOptions.Typography` and
`.TypographyRules`, the shared front-matter key `typography`, `NoteCodes.TypographySkipped`. The authoring
guides change (a `typography` row in *Front matter*, the new note in the notes list), and with them
`tool-guide.expected.md`.

### Decisions and deviations

- **DOCX too,** as asked after the plan was written: `DocxOptions.Typography`, `DocxOptions.TypographyRules`,
  the same key and the same pass in `DocxRuns`, plus the cover's title and subtitle in `DocxWriter.Cover`.
- **Where the pass runs:** inside each parser's `Icons(...)` wrapper, so after `ColouredWords` *and*
  `IconSyntax` (the plan said right after `ColouredWords`). Every run list goes through that wrapper, and
  icon runs then have empty text and are naturally skipped.
- **The pass is an instance** (`Typography`, built once per document by `Typography.For`, `null` when off)
  rather than a static method, because it carries the rules, the font check and the per-document set of
  reported characters. `Typography.Apply<T>` is generic over the run type like `ColouredWords.Apply`; it
  keeps the number and order of runs.
- **`MonthNames`** instead of the plan's `DatePatterns`: a list of month names (long names, plus the short
  ones of `GeneratedTexts.MonthNames`); a day of one or two digits with an optional `.` before one of them
  keeps its space as a no-break space. Only day-before-month ("13 November"); "November 13" is not handled.
- **`Dash`** is the character a spaced hyphen becomes; its spaces stay (a profile cannot remove them).
  Quotes are strings, so a host's French profile can bring no-break spaces inside « » (as tested).
- **Abbreviations:** besides the plan's list, German has `o. Ä.`, `u. U.`, `v. a.`. A space between any
  abbreviation and a following number becomes a no-break space (the plan named `§`, `Nr.`, `S.`; it is the
  same rule for all). Units gained `°F`, `°`, `CHF`, `£`, `m²`, `m³`, `km²`, `ms`.
- **Never changed, beyond the plan:** dashes in arrows (`-->`, `<--`) and runs of four or more hyphens; a
  spaced hyphen between digits (`5 - 3`, a minus); `--`/`---` between digits; a straight quote with spaces on
  both sides; e-mail addresses and `www.` addresses (masked by pattern, like `scheme://` URLs and HTML tags).
- **Single quotes:** a `'` after a letter while no single quote is open is the apostrophe (`Chris' book`).
  German closing ‘ and the apostrophe ’ differ, so `hab'` at a word end is the apostrophe.
- **A multi-character replacement** (`--`, `---`, `...`) must lie within one run; split across runs (e.g.
  `-**-**`) it stays as typed.
- **Notes:** one `typography-skipped` per character per document, at the line of the first paragraph
  where it was needed.
- **Fonts:** PDFs check the eight text and table faces (`FontSet.HasGlyph`); decks and Word documents check
  a brand's font files (`ResolvedFonts.HasEveryTextFace`), and set every character without them. No
  built-in or sample font lacks any of the characters, so the "font lacks it" path is tested with a made-up
  check at the parser (PDF, deck) and in the shared tests, not with a subset brand font.
- **Title and author:** front-matter `title` and `author` are typeset wherever they are used, including the
  file's properties; a title taken from the cover heading is typeset anyway (it comes from its runs). The
  DOCX cover is plain text pieces, so a code span in the cover heading is typeset there.
- **Drawn containers** (`timeline`, `gantt`, `steps`, `riskmatrix`) are not typeset either, like Mermaid and
  charts (question 8): their text comes from `DrawingTexts`, not the run lists.

### Verification

- Tests: `TypographyTests` in the shared tests (rules per language, quotes across runs, code runs, the
  font check, the key), and in the PPTX, PDF and DOCX tests (decks through the Open XML validator for both
  brands, Word documents for all three templates, PDFs with `pdfaid`, link targets, code, dates, German
  via `de-CH`, a host profile, the title in the properties, Mermaid code unchanged). New golden
  `typography.roundtrip.expected.md` in the PDF tests.
- For the human: look at a deck, PDF and Word document with `typography: true` (quotes, dashes, no line
  break inside "10 km" and "z. B."); veraPDF was not run.
- `./run.sh build`-equivalent (`dotnet build -m:1` after an Internal CLR error in the parallel run): 0
  warnings. Tests per project, after rebasing onto E07-S03: 34 + 123 + 1 + 78 + 71 + 153 + 244 + 454 + 385 = 1,543 passed, 6 Windows-only
  skipped. Running every test with `DOCLIBS_UPDATE_EXPECTED=1` changed only the seven authoring guides and
  `tool-guide.expected.md`: every round-trip sample of decks, PDFs and Word documents stayed the same with
  typography off. With it off the pass is not built (`Typography.For` returns `null`) and every call site
  returns its input; generated files were not byte-compared beyond the round-trip goldens.
- Three front-matter tests that spell out the valid keys now list `typography` after `draft`.
