# Feature gaps

> Date: 2026-11-25. Subject: what doc-libs (release `2611.15.12`) still lacks after the stories
> planned on 2026-11-13.

## Summary

The stories planned on 2026-11-13 are all implemented and in verification. Six gaps remain:

1. **Word round trip.** The DOCX converter reads headings, lists, tables, emphasis, links and pictures.
   It does not give back the Markdown of a document `DocxGenerator` wrote:
   - front matter, alerts, code with its language;
   - captions, footnotes, charts, diagram sources.

   It reads no footnotes from any `.docx`.
2. **Image styles in Word.** `.round`, `fit=cover`, `focus` and `.grey` only give the
   `attribute-ignored` note.
3. **Dark brands in PDFs.** PDFs never use the brand's dark colours.
4. **Word core properties.** Check that title, subject, creator and language are all set.
5. **Contact sheet in the AI tool's reply.** It would be built from the deck preview that already
   exists.
6. **Audience versions, backup slides and lettered appendices.** All three were declined on
   2026-11-13.

**Decision (2026-11-26): take all of it**, including the items declined on 2026-11-13. The stories are
in `spec/50-todo/`; see also [Workbooks and small additions](2026-11-26%20workbooks-and-small-additions.md).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->
