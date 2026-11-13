# How work happens in this repo

Idea → epic/story → iteration → claim → implementation → verification → archive. The human
drives every transition; nothing moves to the next stage on its own.

## 1. Ideas — `spec/00-brainstorming/`

Written by the human, one file per idea or theme, free-form. An idea copied from an external
work-item tracker keeps its original reference so the trail back is never lost.

## 2. Plan — `spec/50-todo/`

Turn an accepted idea into a feature epic and its stories, or into a technical story when it does
not belong to a feature area. Use the repo's project language and
[../spec/_template.md](../spec/_template.md) for stories.

- **Feature epic:** `E01-short-title.md`. Give each feature area the next free `E` number. The
  epic is high-level: goal, why, scope table of stories and their states, decisions for the area,
  acceptance, and open questions. Put cross-cutting decisions for the area here, not in one
  story.
- **Feature story:** `E01-S01-short-title.md`. The story number is counted within its epic; use
  the next free one. Every story belongs to one feature epic or technical container. Its header
  names the epic: `> Epic: [E01 Epic title](E01-short-title.md)`.
- **Technical story:** `T01-S01-short-title.md`, with `> Epic: T01 (technical)`. `T01` is a
  container, not a file or feature epic. Use the next free `T` container after story `Tnn-S99`;
  each container holds at most 99 stories. If technical work serves one feature epic, put it in
  that epic instead.
- **Milestone:** `M01-short-title.md`. It groups stories from several epics delivered together.
  List the stories and the decisions that tie them together; each story stays under its own epic
  and names the milestone in its header: `> Milestone: [M01 title](M01-short-title.md)`.

If an external work-item reference is known, place `W-nnnn` after the story ID and
before the slug. IDs are stable as stories move between folders. Epic, technical-container,
milestone, and story IDs are shared across project languages.

Every story ends with **Open questions** (each question followed by a blank blockquote answer
slot) and **Notes** (free space for remarks unrelated to those questions).

## 3. Iterate

The human fills in answers; revise the same file until the human accepts the plan. Do not
implement an unaccepted plan.

## 4. Claim a story

Before implementation, claim the story as described in [Claiming a story](#claiming-a-story).
Epics and milestones are planning documents; claims are for stories.

## 5. Implement

Once the plan is accepted, the story is claimed by this session, and implementation is requested:

1. Implement it, preferably in a worktree branched from the claim commit.
2. **Move** the story from `spec/55-in-progress/<agent-id>/` to `spec/60-verify/` on the work
   branch — same filename. Remove the agent folder if empty and fix links in the story, epic,
   and milestone.
3. Update the epic's scope state to `in verification` and append **Implementation notes**:
   decisions, compromises, deviations, warnings, verification concerns, and loose ends — and
   **Breaking changes**, or that there are none. Add the same to `CHANGELOG.md` (see
   [standards.md](standards.md#build-and-deployment)).
4. Commit the implementation and moved story together, following the repo's Git rules.
5. Rebase and push after a completed task; resolve conflicts and ask if the right resolution is
   not obvious.
6. In chat, say only that the task is done. **No summary** — the detail is in the file.

## 6. Verify

The human verifies. On approval, **move** the story to
`spec/70-completed/<epic-id>-<epic-slug>/` and set its status to `completed`. Keep all stories
for a feature epic in that folder. When every story in a feature epic is completed, move the epic
file there too. Technical stories go under `T01-technical/` (or the matching `T` container).


## Claiming a story

Several agents may work in separate worktrees. A claim tells every other agent that a story and
the code it concerns are taken.

**Agent id.** Use one short, lowercase kebab-case id for all claims in a session (for example,
`nebel-2`). If the session has no id yet, ask the human to choose one before the first claim. A
new session asks again; the human may reuse an id to resume that agent's claims.

**To claim, before any implementation work:**

1. Work on `main` in the main checkout, not a worktree, and ensure the files involved have no
   uncommitted changes. Check that the story is still in `spec/50-todo/`. If it is already under
   another agent's `spec/55-in-progress/<other-id>/`, stop and ask the human; do not take it.
2. Move the story to `spec/55-in-progress/<agent-id>/` with the same filename. Set its header to
   `> Status: in progress (<agent-id>)`.
3. Fix links broken by the move. The story is one folder deeper, so adjust its relative links;
   update links to it in the epic scope table and any milestone. Set its state in the epic to
   `in progress (<agent-id>)`.
4. Commit only the claim on `main`, for example `Claim E01-S01 for <agent-id>`. Do not include
   unrelated changes.
5. Start implementation, preferably in a worktree branched from that claim commit.

**While claimed:**

- Never edit, move, or implement another agent's claimed story, and leave the code it concerns to
  its owner. If your work needs a change there, ask the human.
- The claim ends when the story has moved to `spec/60-verify/` on the agent's branch and that
  branch reaches `main`.
- To release a claim without implementing it, move the story back to `spec/50-todo/`, restore its
  status and links, and commit only that release on `main`, for example
  `Release E01-S01 from <agent-id>`.

## Epics, technical containers, and milestones

- **Feature epics `E01`, `E02`, ...** group stories for one product or domain area. The epic file
  lives in `spec/50-todo/`; it moves with its stories to `spec/70-completed/<epic-id>-<slug>/`
  when all its stories are complete. Allocate the next free `E` number.
- **Technical containers `T01`, `T02`, ...** group stories that serve no feature epic, such as
  hosting, storage, deployment, authentication, or tooling. They have no file or requirements;
  decisions live in their stories. A container holds at most 99 stories; after `T01-S99`, use
  `T02-S01`.
- **Milestones `M01`, `M02`, ...** group stories from multiple epics for a coordinated delivery.
  They have a file in `spec/50-todo/`, but the stories remain filed under their own epic.
- Refer to stories by ID (`E02-S03`, `E02-S03 §2.4`) and epics by ID (`E04`).

## Analyses — `spec/10-analysis/`

When asked to analyse something, write the answer to `spec/10-analysis/YYYY-MM-DD <slug>.md`,
never into chat. Say it is done and where it is. Analyses often precede plans: an analysis
establishes what is true; a plan proposes what to change.

## Fixed rules

- Never implement directly from an idea; iterate with the human and claim a story first.
- Never touch another agent's claimed story or its code.
- Commit finished work, and rebase and push after a completed task — see the Git section of
  [standards.md](standards.md).
- Temporary and scratch files go in the repo-root `.tmp/`, never system temp or AppData.
- All project memory stays shared in `ai/`.

