# E06-S04 Code highlighting

> Status: todo
> Epic: [E06 Markdown syntax](E06-markdown-syntax.md)

## Summary

Fenced code blocks that name their language (```` ```csharp ````) are highlighted in decks and PDFs:
keywords, strings, comments, numbers and type names in colours taken from the brand, the rest in
the text colour. A new opt-in package does the tokenising with ColorCode (MIT); the generators only
colour the tokens they are given, so without it code looks as it does today.

Split from the analysis's "typography and code highlighting" item; typography is E06-S03.

## Context

- **Code blocks today:**
  - Both parsers ask the code-block renderers first (`CodeBlockRenderers.Render`); a block nobody
    draws stays code. The fence's info string is in `FencedCodeBlock.Info`.
  - Decks: on a flow slide, a `CodeLinesBlock(Lines)` drawn by `DeckWriter.Code` on a rounded box
    in `SlidePalette.Code` (`DarkSurface` on dark slides), each line a paragraph of one
    `RunStyle.Code` run in the code font (`PptxOptions.CodeFont` ?? the brand's mono family ??
    Consolas). On plain slides each line is a `TextParagraph` of one code run (`DeckParser.Walk`).
    Run colours come from `DeckWriter`'s run properties: a status colour (`TextRun.Colour`), the mark
    colour, or the paragraph's colour.
  - PDFs: `MarkdownParser` makes a `CodeBlock(Lines)` and drops the language; `LayoutEngine.Code`
    breaks each line by characters (`BreakByCharacters`) and draws one `TextItem` per line in
    `FontFace.Mono`, in the text colour, on `CodeBackground` (`F3F4F6`).
- **Fonts:** PDFs have Liberation Mono Regular and Bold only, and a brand's mono face may have no
  italic; a missing face is an error (epic decision), so comments cannot be italic.
- **Colours:** `BrandColors` has the primary, link, footer, table, diagram, label, chart and
  highlight colours, all resolved by `ResolvedBrand` (upper-case hex), with `SlideColors` merged over
  them for slides and `DarkSlideBrand` for dark ones. Caller templates without a brand get
  `SlidePalette.Scheme`, which refers to theme colours (`accent1` …) instead of hex values.
- **Libraries:**
  - **ColorCode.Core** (MIT, managed, regex-based, from the .NET Foundation): C#, VB.NET, F#, C++,
    Java, JavaScript, TypeScript, PHP, Python, PowerShell, SQL, HTML, XML, CSS, Markdown, Haskell. No
    JSON, YAML, shell, Go or Rust. A `CodeColorizerBase` subclass gets the text and its scopes
    (keyword, string, comment, number, class name, …), and own languages can be loaded
    (`Languages.Load(ILanguage)`).
  - **TextMateSharp** (MIT): TextMate grammars for hundreds of languages, but its regex engine is
    Oniguruma through a native library per platform, and the grammar package is several MB.
- Renderers already come as opt-in packages registered in the options (`ChartRenderer`,
  `MermaidRenderer`); the guides gain sections from them (`ICodeBlockRenderer.AuthoringGuide`).

## Proposed approach

1. **Hook.** In DocumentGeneration:
   - `ICodeHighlighter` with `IReadOnlyList<CodeToken>? Highlight(string language, string code)`:
     `null` for a language it does not know, else tokens covering the code.
   - `CodeToken(int Start, int Length, CodeTokenKind Kind)`; `CodeTokenKind`: `Plain`, `Keyword`,
     `String`, `Comment`, `Number`, `Type`, `Punctuation` (drawn as plain), `Markup` (tags and
     attribute names).
   - Options `PptxOptions.CodeHighlighter` and `PdfOptions.CodeHighlighter` (`ICodeHighlighter?`,
     default `null`), and a shared front-matter key `highlight` (`true` / `false`) that turns it off
     per document. Without a highlighter, nothing changes.
2. **Package `wwosik.DocLibs.DocumentGeneration.Highlighting`.**
   - `ColorCodeHighlighter : ICodeHighlighter` over ColorCode.Core, mapping its scope names to
     `CodeTokenKind`; names and aliases (`cs`, `csharp`, `c#`, `js`, `ts`, `py`, `ps1`, `sql`, `xml`,
     `html`, …) are matched case-insensitively.
   - Own ColorCode language definitions for `json`, `yaml` and `bash`/`sh`, the gaps that matter most
     in technical documents (question 3).
   - The package's `AuthoringGuide` section lists the languages.
3. **Parsers.**
   - PDFs keep the language: `CodeBlock(Lines, Language)`.
   - Both parsers ask the highlighter for a fenced block with a language that no renderer took, and
     split the lines into spans by token. Decks: the code runs carry a token kind
     (`TextRun.Token`); `CodeLinesBlock` holds lines of runs. PDFs: `CodeBlock` holds lines of
     `(string Text, CodeTokenKind Kind)` spans.
   - Indented code blocks and inline code spans have no language and stay plain.
4. **Colours.**
   - New public `BrandCodeColors` on `BrandColors.Code` (`Keyword`, `String`, `Comment`, `Number`,
     `Type`, `Markup`), each optional, in brand packs as `colors.code.*` (source-generated, unknown
     keys rejected as today).
   - Defaults from the brand: keyword `Primary`, type diagram `Accent`, string label `Success`,
     number label `Warning`, comment `Footer`, markup `Link`. `ResolvedBrand` checks each against the
     code background and darkens (or, on dark slides, lightens) a colour below 4.5:1 contrast, so
     highlighting is always readable.
   - Dark slides take `SlideColors.Code` merged over `Colors.Code`, like the other slide colours.
   - Caller templates without a brand use theme colours: keyword `accent1`, type `accent2`, string
     `accent6`, number `accent4`, comment the text colour at 60 %, markup `hlink`.
   - Colour only: no bold and no italic, so line widths are unchanged and no face is needed that a
     font lacks.
5. **Writers.**
   - **Decks:** `DeckWriter`'s run properties give a token run its colour; lines stay one paragraph
     each, so PowerPoint can edit them.
   - **PDFs:** `LayoutEngine.Code` breaks a line by characters across its spans and writes one
     `TextItem` per span, in its colour. Line breaks fall where they fall today.
6. **Notes.** `NoteCodes.CodeLanguageUnknown` (`code-language-unknown`): a fenced block names a
   language that neither a renderer nor the highlighter knows, so it is not highlighted. Not given for
   `text`, `plain`, `console` and `output`. In `NoteCodes.All` and the docs table, only while a
   highlighter is set.
7. **Dependencies and licences.**
   - ColorCode.Core (MIT) in `Directory.Packages.props`, referenced only by the new package, which
     packs `THIRD-PARTY-NOTICES.txt` with its licence.
   - The package aims at `IsAotCompatible`; ColorCode compiles its regexes at run time, which works
     under AOT (interpreted). A Native AOT publish is checked for warnings; if it warns, the package
     is marked not AOT-compatible and the docs say so.
   - The new package gets a docs page, a README package-table row and a `.Tests` sibling.
8. **Guides, docs, changelog.**
   - The PPTX and PDF guides: name the language after the fence; the `highlight` key. The package's
     section lists the languages. Expected guides regenerated.
   - Docs: DocumentGeneration (`ICodeHighlighter`, `CodeToken`, `BrandCodeColors`, the note), Pptx and
     Pdf, the new package's page, the brand-pack page (`colors.code.*`).
   - `CHANGELOG.md`: new: the package, the hook, the options, `BrandColors.Code`, the key. No breaking
     change: without a highlighter, output is as before.
9. **Tests.** Token mapping for each built-in and own language; aliases; the default colours and the
   contrast correction on light, dark and caller-template slides; split lines in PDFs keep their
   colours across the break; a block nobody knows gives `code-language-unknown`; a brand pack with
   `colors.code.keyword`; decks through the Open XML validator, PDFs through the PDF/A checks; one
   highlighted deck opened in PowerPoint.

## Acceptance criteria

- [ ] With `CodeHighlighter = new ColorCodeHighlighter()`, a ```` ```csharp ```` block shows keywords,
  strings, comments, numbers and types in the brand's code colours in decks (plain and flow slides,
  light and dark) and PDFs.
- [ ] `json`, `yaml` and `bash` blocks are highlighted.
- [ ] `highlight: false` in front matter turns it off for the document.
- [ ] Every default colour has at least 4.5:1 contrast on its background; `colors.code.*` in a brand
  pack overrides them.
- [ ] A block in an unknown language stays plain and gives `code-language-unknown`; `mermaid` and
  `chart` blocks still go to their renderers first.
- [ ] Without a highlighter, samples produce the same decks and PDFs as before.
- [ ] Decks pass the Open XML validator and open in PowerPoint without repair; PDFs stay PDF/A-2b.
- [ ] The new package carries `THIRD-PARTY-NOTICES.txt`; guides, docs and CHANGELOG are updated, and
  all tests pass.

## Open questions

1. Library: ColorCode.Core (proposed: MIT, managed only, small; fewer languages, filled with own
   definitions for JSON, YAML and shell), or TextMateSharp (MIT, far more languages, but a native
   regex library per platform and several MB of grammars)?

> Answered by the human (2026-11-14): as proposed.

2. Opt-in: a separate package passed as `CodeHighlighter` (proposed: no output change and no new
   dependency for anyone who does not want it), or highlighting built into the generators and on by
   default, as a breaking change?

> Answered by the human (2026-11-14): No separate package: the highlighter (ColorCode.Core) lives in the shared package `wwosik.DocLibs.DocumentGeneration` (the human chose fewer packages). Highlighting stays off unless switched on, so output does not change.

3. Own languages: add `json`, `yaml` and `bash` (proposed), more (Go, Rust, Kotlin, Dockerfile), or
   only what ColorCode ships?

> Accepted as proposed (the human, 2026-11-14, without discussion).

4. Default colours: derived from existing brand colours with a contrast correction (proposed), or a
   fixed neutral palette (such as GitHub's light and dark themes) unless the brand sets
   `colors.code.*`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Comments: colour only (proposed: Liberation Mono has no italic and a missing face is an error), or
   italic where the mono font has an italic face?

> Accepted as proposed (the human, 2026-11-14, without discussion).

6. The note `code-language-unknown`: give it (proposed: a typo such as `csharpp` is otherwise
   invisible), or stay silent, since many blocks name languages on purpose for readers only?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. Inline code with a language (`` `x`{.cs} ``): out of scope (proposed), or part of this story?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
