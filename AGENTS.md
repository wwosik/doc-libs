# DocLibs

.NET libraries for converting documents to and from Markdown: `.pdf`, `.docx`, `.pptx` (and,
on Windows with Office, `.doc`, `.rtf`, `.xls`, `.xlsx`) to Markdown, Markdown ↔ JIRA wiki
markup, and Markdown to `.pptx` and PDF/A. Open source (MIT) on GitHub, published to nuget.org.

Stack: .NET 10 class libraries, packed as NuGet packages (`wwosik.DocLibs.*`), tested with
xUnit, built on GitHub Actions.

Start here, then follow the pointers:

- [ai/standards.md](ai/standards.md) — the conventions this repo follows.
- [ai/memory.md](ai/memory.md) — accumulated facts about this codebase.
- [ai/workflow.md](ai/workflow.md) — how work happens here: ideas, plans, implementation,
  verification.
- [spec/](spec/) — current and past work.
- [docs/](docs/) — documentation of how the libraries actually work today.
- [README.md](README.md) — the public face of the repo on GitHub. Each package on nuget.org shows
  its own page of `docs/`.
- [CHANGELOG.md](CHANGELOG.md) — what changed for consumers, per version, breaking changes first.

## Language

This repo's language is **English**. That covers all prose, plans, documentation, UI text, and
commit messages.

The technical skeleton is English in every project of this family and is not a language choice:
`src/`, `tools/`, `deploy/`, `logs/`, `data/`, `.tmp/`, `run.ps1`, `run.sh`, and all
configuration files, contents included. The folder names `ai/`, `spec/`, `docs/`, `.github/`
and the files `AGENTS.md`, `CLAUDE.md`, `README.md` are also fixed — but everything inside
them is in the project language.

In code and in the database everything is English — identifiers, table and column names,
comments, log messages, domain vocabulary included. Only the strings a user reads are in the
project language, which here is also English.

## Hard rules

- **Stage, never commit — unless the human asks.** When a task is done, `git add` what it
  touched and leave the commit to the human. Commit, rebase or push only when asked. Details in
  [ai/standards.md](ai/standards.md).
- **No AI attribution in commit messages.** Never add `Co-Authored-By` (or any other trailer
  naming an AI) to a commit message, whatever a tool's default says.
- **This repo is public and client-neutral.** No client, employer or project names, brands,
  logos, internal documents, hostnames or feed URLs — not in code, samples, tests, docs, plans
  or commit messages. A client's design lives in the client's own code or package, never here.
- **Temporary and scratch files go in `.tmp/`** at the repo root (git-ignored) — never the
  system temp directory, never AppData.
- **All project memory is shared in `ai/`** — never in a tool's device-local memory, so every
  agent on every machine reads the same thing.
- **Never implement straight from an idea.** Ideas become plans in `spec/50-todo/`, plans get
  iterated with the human, and only an accepted plan gets implemented.
- **Analyses go in files, never in chat.** When asked to analyse something, write it to
  `spec/10-analysis/` and say it is done.
- **Report completion without a summary.** After implementing a task, say it is done — the
  detail belongs in the plan file's *Implementation notes*, not in chat.

## Commands

`./run.ps1` (or `./run.sh`) with no arguments lists the available verbs and tools.
