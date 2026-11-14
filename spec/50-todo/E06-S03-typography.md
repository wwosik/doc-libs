# E06-S03 Typography

> Status: todo
> Epic: [E06 Markdown syntax](E06-markdown-syntax.md)

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

>

2. Default: off, switched on by `typography: true` or the option (proposed: existing documents
   keep their text), or on by default as a breaking change, since it is what most writers want?

>

3. Own pass or Markdig's `UseSmartyPants`: an own pass over runs (proposed: per-language quotes
   without per-language pipelines, quotes across emphasis, dashes and no-break spaces, font checks),
   or SmartyPants for quotes, dashes and ellipsis plus an own pass only for no-break spaces?

>

4. A character the fonts lack: keep the typed one with a note (proposed), keep it silently, or let
   PDFs throw as for any missing character?

>

5. Which no-break space: U+00A0 everywhere (proposed: every font has it, Office and PDF viewers
   handle it), or the narrow U+202F inside German abbreviations and before units, as typesetting
   rules ask, where the font has it?

>

6. English dash: spaced en dash "a – b" (proposed: British style, same as German), or unspaced em
   dash "a—b" (American style), or a choice between `en-GB` and `en-US` profiles?

>

7. Swiss German (`de-CH`): the German profile through the language fallback (proposed), or a
   built-in `de-CH` profile with «…» guillemets?

>

8. Drawings: leave the text of Mermaid diagrams and charts as typed (proposed: their labels are
   often identifiers), or apply the pass there too?

>

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
