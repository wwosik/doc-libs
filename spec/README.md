# spec

Specifications and the task lifecycle. The process is described in
[../ai/workflow.md](../ai/workflow.md); this file is the map of the folders.

| Folder | Holds |
| ------ | ----- |
| `00-brainstorming/` | Raw ideas, written by the human. One file per idea or theme. |
| `10-analysis/` | Requested analyses, `YYYY-MM-DD <slug>.md`. Output goes here in files, never into chat. |
| `50-todo/` | Open feature epics, milestones, and stories awaiting implementation. |
| `55-in-progress/<agent-id>/` | Stories claimed by that agent. Do not touch another agent's folder or its story's code. |
| `60-verify/` | Implemented stories awaiting human verification. Each carries an *Implementation notes* chapter. |
| `70-completed/<epic-id>-<slug>/` | Verified stories, grouped with their completed feature epic. Technical stories use a `T` container folder. |

The numeric prefixes exist so the lifecycle reads in order whatever the project language is.
Gaps are deliberate — a new stage can be inserted without renumbering.

| Kind | ID and filename | File |
| ---- | --------------- | ---- |
| Feature epic | `E01-short-title.md` | Yes; groups a product/domain area's stories |
| Feature story | `E01-S01-short-title.md` | Yes; story number is counted within its epic |
| Technical story | `T01-S01-short-title.md` | Yes; `T01` is a container, not a file |
| Milestone | `M01-short-title.md` | Yes; groups stories from several epics |

Allocate the next free `E`/`M` id and the next free story number within its `E`/`T` container.
Technical containers hold up to 99 stories; after `T01-S99`, use `T02-S01`. If known, an
external reference goes after the story ID and before the slug. Story filenames stay unchanged
as they move between folders.

Each feature epic tracks its stories and their states in a scope table. A story belongs to one
feature epic or technical container; cross-epic delivery groupings belong in milestones.

- Claim a story before implementing it; claims are committed on `main`. Never touch a story
	claimed by another agent. See [../ai/workflow.md](../ai/workflow.md#claiming-a-story).
- Commit finished work; rebase and push once a task is complete.
- Never write an analysis into chat; write the file.

## Former numbers

Until 2026-11-13 plans were numbered `NNNNN Title.md` across the whole of `spec/`. They are now
stories of their epics; each keeps its old number in a `> Former number:` header line, and commit
messages from before then still use it.

| Former | Story | Title |
| ------ | ----- | ----- |
| 00001 | E01-S01 | Client brands for the generators |
| 00002 | E01-S02 | Table font |
| 00003 | E04-S01 | Mermaid diagrams |
| 00004 | E03-S01 | Keep headings with their content |
| 00005 | E02-S01 | Slide layouts from Markdown |
| 00006 | E01-S03 | Brand fonts embedded in decks |
| 00007 | E05-S01 | Renderer errors as DocumentConversionException |
| 00008 | E05-S02 | Authoring guides for Markdown writers |
| 00009 | T01-S01 | Package READMEs and a changelog |
| 00010 | E01-S04 | Slides on dark templates |
| 00011 | E02-S02 | Table column widths |
| 00012 | E02-S03 | Images on slides |
| 00013 | E02-S04 | Nothing runs off the slide |
| 00014 | E01-S05 | Brand template features |
| 00015 | E06-S01 | Coloured words |
| 00016 | E04-S02 | Native charts |
| 00017 | E04-S03 | Financial charts |
| 00018 | E04-S04 | SVG to PNG |
| 00019 | E05-S03 | Generation results and notes |
