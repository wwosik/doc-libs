# Conventions this repo follows

This file is self-contained: everything an agent needs is here, not behind a link to another
repo or a local drive.

## Repository

- The whole application lives in **one git repo**.
- All project memory is **shared** in `ai/` — never device-local.
- **Temporary and scratch files** go in the repo-root `.tmp/` (git-ignored) — never system temp,
  never AppData.
- `AGENTS.md` stays small and points into `ai/`; `CLAUDE.md` only forwards to `AGENTS.md`.

## Folder structure

| Path | Purpose |
| ---- | ------- |
| `ai/` | AI instructions and shared project memory |
| `docs/` | Documentation of how the program actually works |
| `.tmp/` | Scratchpad (git-ignored) |
| `spec/` | Specifications and the task lifecycle (see [workflow.md](workflow.md)) |
| `tools/` | Helper tools, one folder per tool, invoked via `run.ps1` / `run.sh` |
| `src/` | .NET solution (`wwosik.DocLibs.slnx`) — one project per NuGet package, each with its sibling test project |
| `artifacts/` | Packages written by `run.sh pack` / `run.ps1 pack` (git-ignored) |
| `.github/workflows/` | CI (`ci.yml`) and release to nuget.org (`release.yml`) |
| `LICENSE` | MIT; applies to everything but the bundled fonts (OFL, `Fonts/OFL.txt`) |

## Tooling

- Tools written **specifically for this repo** are **single-file .NET 10 apps**, run as
  `dotnet run tools/<name>/<name>.cs` (assume .NET 10 is installed). NuGet dependencies go in
  `#:package` directives at the top of the file.
- **Not PowerShell, not Python.** The only `.ps1` in the repo is the launcher itself.
- If a ready-made **npm package** already does the job, use it as-is under `tools/npm` with
  **pnpm** — do not reimplement it in .NET.
- Existing third-party tools do not need rewriting.
- **One-off, throwaway** tools and scratch data go in `.tmp/`, not `tools/`. `tools/` is for
  things that get kept.
- `tools/`, `run.ps1` and `run.sh` are **English throughout** — code, comments, and output —
  whatever language the rest of the repo is in.

Both launchers must stay in step: a verb added to `run.ps1` is added to `run.sh` in the same
change.

## .NET conventions

- Always the **newest** .NET version.
- The solution is `src/wwosik.DocLibs.slnx`, with each project in its own subfolder.
- **One library project is one NuGet package.** The package ID is the project name
  (`wwosik.DocLibs.<Area>[.<Format>]`), which is also its root namespace.
- **Shared build and package settings** live in `src/Directory.Build.props`: target framework,
  nullable, implicit usings, XML documentation, package metadata, and the version. A project sets
  only what is specific to it.
- **Central package management** via `src/Directory.Packages.props` — a package version is
  declared exactly once there, and projects reference packages without a version.
- **Shared package metadata** (authors, MIT licence, repository and project URL, tags, the
  repo `README.md` as package readme, symbol packages) lives in `src/Directory.Build.props` —
  never per project. Each project sets only its `Description`.
- The shared **`.editorconfig`** at the repo root is not project-specific: do not edit it to
  suit this repo.
- A **local `nuget.config`** pins the package sources, with `<clear />` first so no machine-wide
  source leaks in.
- **Tests live in the same solution**, as a sibling project next to the code they test
  (`wwosik.DocLibs.Documents.Tests` next to `wwosik.DocLibs.Documents`) — never a separate solution.
  Projects named `*.Tests` are never packed.
- **Sample files for tests** (binary `.pdf`, `.docx`, `.pptx`, …) are committed in the test
  project's `Samples/` folder; `.gitattributes` marks them binary.
- **Libraries keep no runtime state**: no logging sinks, no configuration files, nothing written
  to `data/` or `logs/`. That belongs to the app that consumes them.
- **Platform-specific code** (e.g. Windows-only COM automation) lives in its own package, so the
  cross-platform packages never depend on it.

## Build and deployment

Builds run on GitHub Actions, from the workflows in `.github/workflows/`:

- **`ci.yml`** — every push to `main` and every pull request: restore, build, test, pack (the
  packages are kept as a build artifact, never published). The WindowsOnly tests skip
  themselves on the Linux runner.
- **`release.yml`** — a pushed tag `v<SemVer>` (e.g. `v1.2.0`, `v1.3.0-beta.1`): build, test,
  pack with that version, and push every package to **nuget.org**. Authentication is nuget.org
  *trusted publishing* (OIDC via `NuGet/login`), so no API key is stored; the repository
  variable `NUGET_USER` names the nuget.org account, and the job runs in the GitHub environment
  `nuget`.

**Versioning is SemVer, and the tag is the version.** All packages share it; nothing in the repo
holds a version number (local packs get the SDK default `1.0.0` and are never published).
Breaking changes to a public API bump the major version — until `1.0.0`, the minor.

nuget.org is the only place packages are published to. Do not invent further release steps:
if one is missing, that is a question for the human, not a gap to fill.

## Git

- **Commit finished work by default.** When a task is done, commit it. Work left sitting in the
  working tree is work the human has to notice, review and commit by hand.
- **Commit what the task touched, and nothing else.** An unrelated fix is its own commit.
- Commit messages are in the project language, like the rest of the prose.
- **Never commit secrets, build output, or anything `.gitignore` covers.** If something ignored
  genuinely needs to be tracked, that is a question for the human, not a decision to make.
- **Do not commit a broken tree.** If the build or the tests fail, say so and stop — a red commit
  costs more than it saves.
- Never skip hooks (`--no-verify`) or bypass signing. A failing hook is a problem to fix, not to
  work around.

### After a major task

A major task is a plan implemented and moved to `spec/60-verify/` — or, without that workflow,
any self-contained piece of work the human asked for. Not every commit along the way.

1. **Rebase** onto the tracked upstream branch (`git pull --rebase`).
2. **Resolve conflicts.** If the correct resolution is not obvious, **stop and ask** — never
   guess at intent that is not yours.
3. **Push.**

`git pull --rebase` replays your unpushed commits on top of upstream, so an ordinary `git push`
follows. Rebasing commits that were *already* pushed rewrites history: the push then needs
`--force-with-lease`, which is fine on a branch only you push to, and a question to ask on any
branch shared with someone else. Never plain `--force`.

## Deviations

Where this repo departs from the conventions above — because it predates them, or because
something specific to it demanded it. Anything listed here is deliberate: do not "fix" it
without asking.

| Convention | What this repo does instead | Why |
| ---------- | --------------------------- | --- |
| Program data in `data/`, logs in `logs/` | Neither exists; no Serilog, no `AppPaths`, no `appsettings` | Libraries have no runtime state — the consuming app owns logging and storage. |
| Solution under `src/server/` with a `.Server` project and its tests | Solution at `src/wwosik.DocLibs.slnx`; library projects plus `src/Directory.Build.props` with shared package metadata | The repo publishes NuGet packages, not an app. |
| CI from a catalog component (only `ci-azure-devops` exists) | GitHub Actions (`.github/workflows/`), written for this repo | The repo is public on GitHub and publishes to nuget.org. |
| The pipeline only builds and tests | A tag also packs and pushes to nuget.org | Publishing packages is the point of the repo. |
| Commit finished work by default; rebase and push after a major task | Stage finished work (`git add`) and stop. Commit, rebase or push only when the human asks. | The human's decision (2026-10-21): they review and commit themselves. |
| — | Never add `Co-Authored-By` or any other AI attribution to a commit message, even if a tool proposes it. | The human's decision (2026-10-21). |
