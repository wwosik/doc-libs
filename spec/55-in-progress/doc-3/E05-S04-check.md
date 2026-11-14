# E05-S04 Check

> Status: in progress (doc-3)
> Epic: [E05 Authoring feedback](../../50-todo/E05-authoring-feedback.md)

## Summary

`PptxGenerator.Check` and `PdfGenerator.Check` run the generator without writing a file. They
apply style rules (bullets and words per slide, title length, alt text, image sources) and return
the generation notes, the rule findings and statistics: slides by kind, estimated talk time, and
pages. A host, or an AI agent through it, checks a draft before generating it, and gets every
problem at once instead of the first exception.

## Context

- **Generation results** (E05-S03). `Generate` returns a `GenerationResult` (`Pages`, `Notes`)
  collected by an internal `GenerationNotes` per call.
  - Every code in `NoteCodes` needs an entry in `NoteCodes.All`, with its formats and advice for
    the guides, and a row in the docs table, or a test fails.
  - Notes never change the output, and there is no strict mode: the host decides (E05 decisions).
- **Errors in the document throw `DocumentConversionException`**, with "on line N" in the message
  where known (E05-S01). Generation stops at the first:
  - a missing image (`DeckParser.Image`, `MarkdownParser.Image`: "is not among the document's
    resources");
  - an unknown container or attribute;
  - a bad front-matter value.

  A writer with three missing images learns about them one run at a time.
- **What the generators already know**, which the check can count:
  - **Decks.** `DeckParser.Parse` returns the `Slide` list before fitting:
    - `TitleSlide` (cover, section, `Closing`), `ContentSlide`, `VisualSlide` and `FlowSlide`;
    - each slide's `Line`;
    - its paragraphs with `ParagraphKind` (`Bullet`, `Numbered`, `Task`, …);
    - `AllRuns()`.

    `DeckWriter.Write` fits and splits, and returns the slide count.
  - **PDFs.** `MarkdownParser.Parse` returns the `DocumentModel` (blocks with headings, lists,
    tables, images), and the layout returns the pages. The PDF block model has no source lines
    yet (E05-S03's implementation notes: `character-replaced` has line 0).
- **Rendering cost.**
  - The PPTX writer needs the Open XML package to fit text, so a deck check writes into a memory
    stream and drops it.
  - The PDF can stop after layout: `PdfRenderer.Render` (font subsetting, PDF/A fixes) is the
    costly part and is not needed for a check.
- **Speaker notes** (E07-S01, planned) give a better basis for talk time than slide text, where
  they exist.
- **The analysis:** "limits as options, rules not tied to one language". The fork's rules counted
  words and bullets; nothing language-specific (stop words, spelling) comes over.
- **E05-S05** (AI tool) exposes the check as a mode of the tool.

## Proposed approach

1. **Public types in `wwosik.DocLibs.DocumentGeneration`:**

   ```csharp
   public sealed record CheckOptions
   {
       public static CheckOptions Default { get; } = new();
       public int? MaxBulletsPerSlide { get; init; } = 6;      // list items, all levels, per ## section
       public int? MaxWordsPerSlide { get; init; } = 80;       // title excluded, notes excluded
       public int? MaxTitleLength { get; init; } = 60;         // text elements; slide titles and PDF headings
       public int? MaxWordsPerParagraph { get; init; } = 150;  // PDFs
       public bool RequireAltText { get; init; } = true;
       public int WordsPerMinute { get; init; } = 130;         // talk time
       public TimeSpan MinTimePerSlide { get; init; } = TimeSpan.FromSeconds(30);
   }

   public sealed record CheckResult(IReadOnlyList<GenerationNote> Notes, CheckStatistics Statistics)
   {
       public string? Error { get; init; }                     // the first DocumentConversionException's message
       public bool IsClean => Error is null && Notes.Count == 0;
   }

   public sealed record CheckStatistics
   {
       public int Pages { get; init; }                         // slides written, or PDF pages
       public int Words { get; init; }
       public int Images { get; init; }
       public int Tables { get; init; }
       public int Diagrams { get; init; }                      // drawings and charts from renderers
       public IReadOnlyDictionary<string, int> SlidesByKind { get; init; }  // decks: cover, section, text, visual, layout, closing
       public TimeSpan? TalkTime { get; init; }                // decks
   }
   ```

   - A `null` limit switches its rule off.
   - The kinds are stable English strings, like note codes.
   - `CheckResult.ToString()` lists the notes like `GenerationResult`, then one line of
     statistics.
2. **Options.** `PptxOptions.Checks` and `PdfOptions.Checks` (`CheckOptions`, default
   `CheckOptions.Default`) carry the limits, so `Check`, the authoring guide and the AI tool
   (E05-S05) all see the same ones (question 2).
3. **Entry points:** `PptxGenerator.Check(MarkdownDocument, PptxOptions?)` and
   `PdfGenerator.Check(MarkdownDocument, PdfOptions?)`.
   - **Decks** run the whole `Generate` path into a `MemoryStream` that is dropped.
   - **PDFs** run front matter, parsing and layout, and skip `PdfRenderer.Render`. For this,
     `Generate` is split internally into a layout step and a render step.
   - The generation notes come back unchanged, so a check reports `slide-split`, `code-not-drawn`
     and so on exactly as `Generate` would.
4. **Errors become a result** (question 3).
   - `Check` catches `DocumentConversionException`. It returns `Error` with the message, the
     notes found so far, and the statistics it could count. It does not throw for document
     errors; `ArgumentException` for an invalid brand or options still throws.
   - **Image sources are checked first, all at once.** Before generating, a pass over the
     Markdown (the shared `MarkdownSyntax` pipeline) checks every image reference against the
     document's resources.
     - Each missing one gives a note `image-source` with its line.
     - So do references that are absolute URLs (`http:`, `https:`, `data:`), which the
       generators can never load.
     - If any are found, the images are replaced by their alt text for the rest of the check, so
       generation goes on and finds the other problems.
5. **Rules.** New codes in `NoteCodes`, each in `NoteCodes.All` with formats and advice. They come
   only from `Check`, never from `Generate` (question 1).

   | Code | Format | When |
   | ---- | ------ | ---- |
   | `too-many-bullets` | PPTX | A `##` section has more list items (all levels) than `MaxBulletsPerSlide` |
   | `too-many-words` | PPTX | A `##` section's text, title excluded, has more words than `MaxWordsPerSlide` |
   | `long-paragraph` | PDF | A paragraph has more words than `MaxWordsPerParagraph` |
   | `title-too-long` | both | A slide title or a PDF heading is longer than `MaxTitleLength` text elements |
   | `alt-text-missing` | both | An image has empty alt text (`![](../../50-todo/a.png)`) and `RequireAltText` is on |
   | `image-source` | both | An image is not among the resources, or is a remote or `data:` URL |

   - **Per section.** The deck rules count per source section, the slide the writer wrote, at its
     heading's line. A section that fitting split counts once; `slide-split` already says it was
     split.
   - **Words, in any language:**
     - **What counts.** Runs of letters, digits and joiners between whitespace or punctuation
       (`char.IsLetterOrDigit`, plus `'` and `-` inside a word). Code spans count as one word
       each; code blocks and diagrams do not count.
     - **Scripts without spaces.** In Han, Hiragana, Katakana and Thai, each two characters count
       as one word.
   - **Messages** name the slide or heading and the numbers, for example "The slide 'Results' has
     9 bullets; keep it to 6.".
   - **PDF lines.** The PDF rules need source lines. Headings, paragraphs and images get `Line`
     in the PDF block model (`Block.Line`, set by `MarkdownParser.Add` from `LineOf`), closing
     E05-S03's loose end. `character-replaced` keeps line 0.
6. **Statistics:**
   - **Pages:** the generator's count.
   - **Counts:** words over all text, as above; images (also in rows); tables; diagrams and
     charts drawn by renderers.
   - **Slide kinds** (decks), counted over the written slides, split parts included:
     - `cover`;
     - `section`;
     - `closing`;
     - `text` (`ContentSlide`);
     - `visual` (`VisualSlide`);
     - `layout` (`FlowSlide`).
   - **Talk time** (decks):
     - **Content slides:** the larger of `MinTimePerSlide` and the slide's words divided by
       `WordsPerMinute`. The speaker notes' words count instead of the slide's once E07-S01
       exists (question 4).
     - **Cover, section and closing slides:** 10 seconds each.
     - The total is rounded to whole minutes.
7. **Authoring guides.** A short section, *Limits*, lists the active limits of `Checks`, so the
   writer knows them before the check, for example "at most 6 bullets per slide". The section
   *Notes you may get back* includes the check codes, under a sentence that they come from a
   check. The expected guides are regenerated, and the guide tests require a text for every code.
8. **Docs and changelog.**
   - **Docs.** `docs/wwosik.DocLibs.DocumentGeneration.md` gets *Checking a document*: the
     types, the rules, the word count, talk time and the kinds. The notes table gets the six
     codes. The Pptx and Pdf pages get "what `Check` does here".
   - **CHANGELOG.** Under new: `Check`, `CheckOptions`, `CheckResult`, `CheckStatistics`,
     `Checks`, and the codes. Nothing breaks: `Generate` is unchanged, and the new PDF block
     lines are internal.
   - `ai/memory.md`: Check never throws for document errors, and rules come only from Check.
9. **Tests.**
   - Each rule, with the smallest Markdown that triggers it and the right line, including front
     matter. A limit set to `null` is silent.
   - Three missing images give three `image-source` notes, and the check goes on.
   - A document error comes back as `Error`, not an exception.
   - The word count over English, German, Polish, and a Japanese line.
   - Slide kinds and talk time for `deck.md` and `layouts.md`, as golden values.
   - Generation and check agree: for every sample, `Check(...).Notes` contains every note
     `Generate` returns, and `Pages` matches.
   - A PDF check does not render (a test seam counts `Render` calls), and the output of
     `Generate` is unchanged for all samples.

## Acceptance criteria

- [ ] `PptxGenerator.Check` and `PdfGenerator.Check` return a `CheckResult` with the generation
  notes, the rule notes and statistics, and write no file.
- [ ] Each rule in the table fires at its limit with the right line and is silent when its limit
  is `null`.
- [ ] Every missing or remote image is reported in one check; a document error is returned as
  `Error`, not thrown.
- [ ] Words are counted the same way in every language; scripts without spaces get a sensible
  count.
- [ ] Decks report slides by kind and a talk time; PDFs report pages.
- [ ] `Generate` returns the same notes and files as before for all samples.
- [ ] The guides list the limits and the check codes. Docs, CHANGELOG and memory are updated,
  and all tests pass.

## Open questions

1. Do the style rules come only from `Check` (proposed: `Generate`'s notes stay about what the
   generator did, as E05-S03 defined them)? Or does `Generate` report them too?

> Accepted as proposed (the human, 2026-11-14, without discussion).

2. Limits as a property of the generator options, `PptxOptions.Checks` (proposed: the guide and
   the AI tool see the same limits), or as a separate parameter of `Check`?

> Accepted as proposed (the human, 2026-11-14, without discussion).

3. Should `Check` return document errors as `Error` instead of throwing (proposed: a check that
   throws on the first problem is no check)? Or keep throwing, like `Generate`?

> Answered by the human (2026-11-14): as proposed.

4. Talk time: slide words at 130 words per minute with at least 30 seconds per content slide,
   and the speaker notes once they exist (proposed)? Or a flat time per slide, which is simpler
   and as rough?

> Accepted as proposed (the human, 2026-11-14, without discussion).

5. Default limits: 6 bullets, 80 words per slide, 60 characters per title, 150 words per PDF
   paragraph, alt text required (proposed, close to the fork's)? Or every rule off unless the
   host sets a limit?

> Answered by the human (2026-11-14): as proposed.

6. Deck rules per source section (proposed: what the writer wrote), or per slide after
   splitting, which can hide a section that is far too long?

> Accepted as proposed (the human, 2026-11-14, without discussion).

7. A DOCX check (E09): left to E09, with estimated pages from words and visuals there, since
   Word decides the pages (proposed)? Or should this story define the estimate now?

> Accepted as proposed (the human, 2026-11-14, without discussion).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
