# E06-S04 Code highlighting

> Status: in verification
> Epic: [E06 Markdown syntax](../50-todo/E06-markdown-syntax.md)

## Summary

Fenced code blocks that name their language (```` ```csharp ````) are highlighted in decks and PDFs:
keywords, strings, comments, numbers and type names in colours taken from the brand, the rest in
the text colour. The tokenising is built into the shared package `wwosik.DocLibs.DocumentGeneration`
with ColorCode (MIT); no new package. Highlighting is off unless switched on, by an option or the
front-matter key `highlight: true`, so existing output does not change.

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
  Highlighting does not follow that pattern (question 2): it lives in the shared package
  `wwosik.DocLibs.DocumentGeneration`, which every generator references, which depends only on
  DocumentConversions and Markdig today, and which is `IsAotCompatible`.
- **Front matter:** shared keys are constants in DocumentGeneration (`FrontMatter.TemplateKey`,
  `TitleKey`, `AuthorKey`, `DocumentLanguage.Key`); generator-only keys are private constants
  (`PdfGenerator.PageSizeKey`, `PptxTemplateOptions.ListBulletsKey`).

## Proposed approach

1. **Switch: off unless switched on.**
   - Options `PptxOptions.HighlightCode` and `PdfOptions.HighlightCode` (`bool`, default `false`).
   - A shared front-matter key `highlight` (`true` / `false`; `CodeHighlighting.Key` in
     DocumentGeneration, next to `DocumentLanguage.Key`) that overrides the option for one document:
     `highlight: true` switches it on when the option is off, `highlight: false` switches it off when
     the option is on. Any other value is an error, as for other keys.
   - Neither set: no highlighting, and output is byte-for-byte as before.
2. **Built-in highlighter, in DocumentGeneration** (new file
   `src/wwosik.DocLibs.DocumentGeneration/CodeHighlighting.cs` plus a `Highlighting/` folder for the
   language definitions).
   - Internal `CodeHighlighter.Highlight(string language, string code)` returning
     `IReadOnlyList<CodeToken>?`: `null` for a language it does not know, else tokens covering the
     code. Internal, not a public extension point: there is one highlighter, and a public interface
     can be added later if someone needs another.
   - `CodeToken(int Start, int Length, CodeTokenKind Kind)` (internal); public `CodeTokenKind`, since
     `BrandCodeColors` names its members: `Plain`, `Keyword`, `String`, `Comment`, `Number`, `Type`,
     `Punctuation` (drawn as plain), `Markup` (tags and attribute names).
   - Over ColorCode.Core, mapping its scope names to `CodeTokenKind`; names and aliases (`cs`,
     `csharp`, `c#`, `js`, `ts`, `py`, `ps1`, `sql`, `xml`, `html`, …) are matched case-insensitively.
   - Own ColorCode language definitions for `json`, `yaml` and `bash`/`sh`, the gaps that matter most
     in technical documents (question 3).
   - ColorCode's languages are loaded and their regexes built on the first highlighted block only, so
     a document without highlighting pays nothing at run time.
3. **Parsers.**
   - PDFs keep the language: `CodeBlock(Lines, Language)`.
   - When highlighting is on (point 1), both parsers ask the highlighter for a fenced block with a
     language that no renderer took, and split the lines into spans by token. When it is off they do
     not call it. Decks: the code runs carry a token kind
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
   `text`, `plain`, `console` and `output`. In `NoteCodes.All` and the docs table; given only while
   highlighting is on for the document.
7. **Dependencies and licences.**
   - ColorCode.Core (MIT): a central `PackageVersion` in `src/Directory.Packages.props` (Libraries
     group), and a `PackageReference` in
     `src/wwosik.DocLibs.DocumentGeneration/wwosik.DocLibs.DocumentGeneration.csproj`. So every
     consumer of the shared package gets it, transitively; its size is measured and recorded in the
     DocumentGeneration docs page.
   - The shared package's `THIRD-PARTY-NOTICES.txt` (packed at the package root; started by E04-S06
     for QRCoder and Tabler Icons — whichever story lands first creates it) gains ColorCode's licence.
   - The shared package is `IsAotCompatible` and must stay so. ColorCode compiles its regexes at run
     time, which works under AOT (interpreted). A Native AOT publish is checked for warnings; see Notes
     for what happens if it warns.
   - No new package, docs page or test project; tests go in `wwosik.DocLibs.DocumentGeneration.Tests`
     and the Pptx / Pdf test projects.
8. **Guides, docs, changelog.**
   - The PPTX and PDF guides, always (an author can switch highlighting on in front matter even when
     the caller left it off): name the language after the fence, `highlight: true`, and the list of
     languages. Expected guides regenerated.
   - Docs: `docs/wwosik.DocLibs.DocumentGeneration.md` (the switch, the `highlight` key,
     `CodeTokenKind`, `BrandCodeColors`, the languages, the note, the ColorCode dependency),
     `docs/wwosik.DocLibs.DocumentGeneration.Pptx.md` and `docs/wwosik.DocLibs.DocumentGeneration.Pdf.md`
     (`HighlightCode`), and the brand-pack section (`colors.code.*`).
   - `CHANGELOG.md`: new: code highlighting, the `HighlightCode` options, the `highlight` key,
     `BrandColors.Code`. No breaking change in output: highlighting is off unless switched on.
     DocumentGeneration now depends on ColorCode.Core.
9. **Tests.** Token mapping for each built-in and own language; aliases; the default colours and the
   contrast correction on light, dark and caller-template slides; split lines in PDFs keep their
   colours across the break; a block nobody knows gives `code-language-unknown` (only with
   highlighting on); the switch: option off and no key gives unchanged output, `highlight: true` with
   the option off highlights, `highlight: false` with the option on does not; a brand pack with
   `colors.code.keyword`; decks through the Open XML validator, PDFs through the PDF/A checks; one
   highlighted deck opened in PowerPoint.

## Acceptance criteria

- [ ] With `HighlightCode = true` (or `highlight: true` in front matter), a ```` ```csharp ```` block shows keywords,
  strings, comments, numbers and types in the brand's code colours in decks (plain and flow slides,
  light and dark) and PDFs.
- [ ] `json`, `yaml` and `bash` blocks are highlighted.
- [ ] Off by default; `highlight: true` in front matter turns it on for the document, and
  `highlight: false` turns it off even when the option is on.
- [ ] Every default colour has at least 4.5:1 contrast on its background; `colors.code.*` in a brand
  pack overrides them.
- [ ] A block in an unknown language stays plain and gives `code-language-unknown`; `mermaid` and
  `chart` blocks still go to their renderers first.
- [ ] With highlighting off (the default), samples produce the same decks and PDFs as before.
- [ ] Decks pass the Open XML validator and open in PowerPoint without repair; PDFs stay PDF/A-2b.
- [ ] The DocumentGeneration package's `THIRD-PARTY-NOTICES.txt` names ColorCode; the package stays
  `IsAotCompatible`; guides, docs and CHANGELOG are updated, and all tests pass.

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

- Revised after question 2. The old `highlight` key could only turn a configured highlighter *off*
  (`highlight: false`); with the highlighter built in and off by default, the key now works both ways
  (point 1). No answered question conflicts with this.
- Risk for the human: the old plan let only the separate package lose `IsAotCompatible` if ColorCode
  warns under Native AOT. Now that ColorCode is in the shared package, that fallback would mark the
  whole shared package — and so every generator — not AOT-compatible. If the AOT check warns,
  implementation stops and asks; the alternatives are suppressing a warning that is shown to be
  harmless, or an own regex tokeniser for the same languages without ColorCode.

## Implementation notes

### Breaking changes

- **The shared package `wwosik.DocLibs.DocumentGeneration` now depends on ColorCode.Core 2.0.15** (MIT,
  managed, `netstandard2.0`, 122 000 bytes, no further dependencies), so every generator brings it along.
  It is loaded only when a document is highlighted. In the CHANGELOG under *Breaking changes*, like the
  QRCoder dependency before it.
- No change in output: highlighting is off unless switched on. The off state was checked by generating every
  Markdown sample of the PPTX, PDF and DOCX tests plus a code-heavy document (all fenced languages, unknown
  ones, `text`, indented code, a `.dark` flow slide, long lines) for the neutral and the sample brand as
  `.pptx`, `.pdf` and `.docx` before and after the change, and comparing them: decks and Word documents part
  by part (embedded workbooks too) with relationship ids masked, PDFs with `/ID`, dates and XMP ids masked,
  plus the notes — 132 of 132 files identical; repeated after rebasing onto E03-S03, E03-S04, E07-S04 and
  E07-S06 against `origin/main`: 144 of 144 identical.
- The authoring guides change (a `highlight` row in *Front matter* with the languages, the note
  `code-language-unknown` in the notes list), and with them `tool-guide.expected.md`. To keep within the word
  limits (PPTX 3,500, PDF 2,500, both full after the stories merged in parallel), a few sentences elsewhere
  in `Pptx.md` and `Pdf.md` were shortened. PDF: the paragraph bullet ("broken at spaces"), the table
  bullet ("wider tables squeeze their words"), the image caption and `scale=` attribute lines, the QR-code
  length hint and "dark on white", the definition-list bullet, "not interpreted" for HTML, "and a link to
  it" for contents entries, the *When something is wrong* paragraph. PPTX: the *Keep slides short* bullet,
  the footnote bullet ("Use them for sources", "A reference without its definition stays as written" — the
  note `footnote-missing` says it), the definition-list and "several images" asides, the status-colour
  aside. Worth a read by the human; nothing a writer needs is lost.
- The front-matter error for an unknown key lists `highlight` among the valid keys.

### Decisions and deviations

- **DOCX too,** as asked after the plan was written: `DocxOptions.HighlightCode`, the same key, a run per
  piece in the `Code` style with `w:color` as direct formatting; on a caller's Word template the theme colours
  (`w:themeColor` accent 1, accent 2, accent 6, accent 4, `text1` with tint `99` for comments, `hyperlink`),
  with the neutral brand's hex as Word's fallback. The DOCX → Markdown round trip still reads the block.
- **JSON is an own definition although ColorCode 2.0.15 ships one:** its string pattern
  (`"[^"\\]*(?:\\[^\r\n]|[^"\\]*)*"`) nests quantifiers over the same characters and can take exponential
  time on an unclosed string — and ColorCode builds its regexes without a timeout. The own JSON, YAML and Bash
  definitions avoid such patterns; a test runs a 400 000-character unclosed string through all three.
  ColorCode's own languages remain regex-based without a timeout; with highlighting switched on, a hostile
  document could in principle find a slow pattern there (verification item below).
- **ColorCode's global registry is left alone:** the plan said own languages are loaded with
  `Languages.Load`. That would change a process-wide registry a host using ColorCode shares. Instead
  `CodeHighlighter` has its own `LanguageParser` with its own `LanguageCompiler` cache and an
  `ILanguageRepository` that serves the own definitions (ids `doclibs-json`, `doclibs-yaml`, `doclibs-bash`)
  and falls back to ColorCode's built-in ones. Nothing is compiled before the first highlighted block.
- **Languages offered:** the plan's list. ColorCode also has Fortran, MATLAB, Koka and the ASP.NET variants;
  they are not offered (not in the plan, not in the guides). Aliases: `cs`, `c#`; `vbnet`, `vb.net`; `fs`,
  `f#`; `c`, `c++`; `js`; `ts`; `py`; `ps1`, `pwsh`; `htm`; `xaml`; `md`; `hs`; `yml`; `sh`, `shell`, `zsh`.
  The guides list only the main names (word limit); the docs list the aliases.
- **Token mapping:** ColorCode's scope names map to `CodeTokenKind`; the innermost scope wins. Shell
  variables, PowerShell variables and attributes are `Type`; JSON and YAML keys and PowerShell parameters
  are `Markup`. ColorCode's own definitions decide what is found: e.g. C# class names in declarations, `let`
  in TypeScript and `#include` in C++ are not marked by ColorCode 2.0.15 and stay plain.
- **Note `code-language-unknown`:** given for any fenced language that neither a renderer drew nor the
  highlighter knows, so also for `mermaid` or `chart` when no such renderer is registered. A renderer that
  declined a block and reported `code-not-drawn` on its line suppresses the second note
  (`GenerationNotes.Has`).
- **Contrast correction:** only colours the brand leaves out are corrected; a colour set in
  `colors.code.*` is used as given (the plan's acceptance names the defaults). `On(background, dark)` darkens
  (or for light-on-dark text lightens) in 2 % steps until 4.5:1; where even black or white falls short, it
  becomes black or white. The direction is explicit, not inferred from the background: a `.dark` code box can
  be mid-tone while the text on it is white. Backgrounds: PDFs and Word `F3F4F6`; deck code box
  `Mix(TableLine, 0.30)` (also used for code on plain light slides, which sit on white and so have more
  contrast); `.dark` slides of a light template their code box `Mix(Primary, 0.85)`; dark templates a fixed
  `374151`, since their background is the template's and unknown to the palette.
- **Dark slides with a mid-tone primary get no visible highlighting:** on the sample brand's teal `.dark`
  slide even white text has only about 3.5:1 on the code box, so every derived colour becomes white. On a
  dark template, or a dark primary such as the neutral brand's, the lightened colours are visible but pale.
  `slideColors.code.*` sets deck colours, but for light and dark slides of one deck alike.
- **The neutral brand's keywords and types look almost like the text** (`Primary` and `Diagram.Accent` are
  both `1F2937`): highlighting there shows strings, numbers, comments and markup. As planned (question 4);
  worth a look.
- **Decks:** `TextRun.Token` (null outside highlighted code); `CodeLinesBlock` now holds lines of runs, built
  by the parser exactly as the writer used to build them when off. Theme colours on caller templates as
  planned; comments are text 1 with `lumMod 60000 lumOff 40000`.
- **PDFs:** `CodeBlock(Lines of CodeSpan, Language)`, `CodeBlock.Plain` for plain lines. `LayoutEngine` breaks
  by characters across spans (a break splits a span, keeping its kind) and writes one `TextItem` per span,
  each starting where the previous one ended in the mono face. With one span per line it is the old code
  path's output.
- **The HTML deck's DrawingML contract** (`EverySampleDeck_IsDrawn_WithNothingLeftOut`): the features deck
  now has `highlight: true` and highlighted code on a light and a `.dark` slide; the reader needed no change
  (run fills in hex and scheme colours were already read).

### Verification

- Native AOT: a small app in `.tmp/aot-app` referencing the PDF generator and generating a PDF with
  `highlight: true` (C#, JSON, YAML, Bash, HTML, an unknown language) publishes for `linux-x64` without a
  warning and runs; the shared package stays `IsAotCompatible`. A probe of ColorCode.Core alone published
  without warnings too.
- Tests: `CodeHighlightingTests` in the shared tests (each own language, ColorCode languages, aliases,
  token coverage, linear time, line splitting, the note and its suppression, the switch, default colours
  and contrast on light and dark backgrounds for three brands, brand colours used as given and checked,
  brand pack `colors.code.*` and an unknown key, slide colours merged), PPTX (off, the switch both ways,
  colours for both brands, a `.dark` flow slide, the dark template, a caller template's theme colours, brand
  colours, the note, renderers first, inline and indented code plain; decks through the Open XML validator),
  PDF (spans, the switch, colours and positions in the layout, breaking across spans, the note, PDF/A marker
  and text for both brands) and DOCX (all three templates through the validator, theme colours on the caller
  template, the switch, the note, the round trip).
- `./run.sh build`: 0 warnings. `./run.sh test` after the rebase: 123 + 161 + 1 + 300 + 35 + 466 + 525 + 71 + 78
  = 1,760 passed, 6 Windows-only skipped, none failed.
- Review files in `.tmp/review/`: `code-highlighting-*.pptx|pdf|docx` (from the tests),
  `code-neutral.*` / `code-sample.*` (the code-heavy document, highlighted) with PNG renders of the decks and
  PDFs, `aot-highlighting.pdf`.
- For the human: open a highlighted deck in PowerPoint (no PowerPoint here; the Open XML validator and the
  HTML deck reader pass); run veraPDF on a highlighted PDF (only the `pdfaid` marker is checked); look at the
  colours of the neutral brand and on `.dark` slides (see above) and decide whether the derivation is
  good enough; decide whether ColorCode's own regexes without a timeout are acceptable for untrusted Markdown
  with highlighting on.
