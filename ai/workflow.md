# How work happens in this repo

Idea → plan → iteration → implementation → verification → archive. The human drives every
transition; nothing moves to the next stage on its own.

## 1. Ideas — `spec/00-brainstorming/`

Written by the human, one file per idea or theme, free-form. An idea copied from an external
work-item tracker keeps its original reference number (e.g. `W-001235`) so the trail back is
never lost.

## 2. Plan — `spec/50-todo/`

When asked to turn an idea into a plan, write it to `spec/50-todo/` as
`NNNNN Title.md` — or `NNNNN W-nnnn Title.md` when the external reference is known, with the
reference between the number and the title.

The five-digit number is sequential across the whole of `spec/`: take the highest already used
in any of its folders and add one.

Use [../spec/_template.md](../spec/_template.md) as the skeleton. Every plan ends with:

- **Open questions** — each question followed by a blank blockquote line as the answer slot.
- **Notes** — free space for the human's remarks that are not answers to those questions.

## 3. Iterate

The human fills in answers; revise the plan in the same file. Repeat until the human accepts
it. Do not start implementing an unaccepted plan.

## 4. Implement

Once the plan is accepted and implementation is requested:

1. Implement it.
2. **Move** the plan file from `spec/50-todo/` to `spec/60-verify/` — same filename.
3. Append an **Implementation notes** chapter: decisions taken along the way, compromises,
   deviations from the plan, warnings, things worth attention during verification, loose ends
   worth considering later.
4. **Stage** the implementation and the moved plan file together (`git add`) — do not commit.
   Commit, rebase and push only when the human asks.
5. In chat, say only that the task is done. **No summary** — the detail is in the file.

## 5. Verify

The human verifies. Once approved, **move** the file to `spec/70-completed/`.

## Analyses — `spec/10-analysis/`

When asked to analyse something, the answer goes to a file in `spec/10-analysis/` named
`YYYY-MM-DD <slug>.md` — never into chat. Say it is done and where it is.

Analyses often precede plans: an analysis establishes what is true, a plan proposes what to
change.

## Fixed rules

- **Stage finished work, never commit it** unless the human asks; never add an AI
  `Co-Authored-By` trailer — see the Deviations in [standards.md](standards.md).
- Temporary and scratch files go in the repo-root `.tmp/`, never system temp or AppData.
- All project memory stays shared in `ai/`.
