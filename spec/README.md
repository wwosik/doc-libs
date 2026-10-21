# spec

Specifications and the task lifecycle. The process is described in
[../ai/workflow.md](../ai/workflow.md); this file is the map of the folders.

| Folder | Holds |
| ------ | ----- |
| `00-brainstorming/` | Raw ideas, written by the human. One file per idea or theme; pasted external user stories keep their reference number. |
| `10-analysis/` | Requested analyses, `YYYY-MM-DD <slug>.md`. Output goes here in files, never into chat. |
| `50-todo/` | Concrete, iterated feature plans awaiting implementation. |
| `60-verify/` | Implemented tasks awaiting human verification. Each carries an *Implementation notes* chapter. |
| `70-completed/` | Verified and approved. |

The numeric prefixes exist so the lifecycle reads in order whatever the project language is.
Gaps are deliberate — a new stage can be inserted without renumbering.

Plan files are named `NNNNN Title.md`, or `NNNNN W-nnnn Title.md` when an external reference is
known. The number is sequential across all of `spec/`, and the filename does not change as the
file moves between folders.

- Stage finished work, never commit it unless the human asks (see `ai/standards.md`, *Deviations*).
- Never write an analysis into chat; write the file.
