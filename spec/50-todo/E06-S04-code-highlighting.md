# E06-S04 Code highlighting

> Status: todo
> Epic: [E06 Markdown syntax](E06-markdown-syntax.md)

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
