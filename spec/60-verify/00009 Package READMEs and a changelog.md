# 00009 Package READMEs and a changelog

> Status: verify — implemented 2026-10-23

## Summary

Every package carries its own documentation page as its README, and all of them carry a
`CHANGELOG.md` that says, version by version, what a consumer has to do when updating —
breaking changes first. Plans record their breaking changes, and the changelog gets the entry in
the same change.

## Context

- Every package packs the repo-root `README.md` (`src/Directory.Build.props`), so nuget.org shows
  the same overview for all six packages. The per-package pages in `docs/` — options, Markdown
  mapping, front matter, known limits — are only on GitHub. A coding agent in a consuming repo
  sees `~/.nuget/packages/<id>/<version>/README.md` and nothing else.
- `docs/` pages link to each other relatively (`wwosik.DocLibs.DocumentGeneration.md#fonts`);
  those links break in a packed copy.
- **Versioning:** the tag is the version, `v<yyMM>.<d>.<H>[.<r>]` (Europe/Berlin), as in
  `ai/standards.md` *Build and deployment* — e.g. `v2610.22.18`, `v2610.22.18.1`. The version
  says nothing about compatibility, and there is no record of what changed between versions.
- `release.yml` takes any `v*` tag as the version, without checking it against the scheme.
- Plans 00005–00008 each change behaviour a consumer notices (bigger decks, `CodeFont` nullable,
  a new exception type, Markdown that now fails because `:::` and `{…}` became syntax).
- `ai/workflow.md` step 4 asks for *Implementation notes*, but not for breaking changes.

## Proposed approach

1. **Package README per package:** set `PackageReadmeFile` to a `README.md` written at pack time
   from `docs/<PackageId>.md` (an inline `RoslynCodeTaskFactory` task in
   `src/Directory.Build.props`, target before `_GetPackageFiles`). Relative links to sibling pages
   are rewritten to absolute GitHub links pinned to the version's tag:
   `https://github.com/wwosik/doc-libs/blob/v$(Version)/docs/<page>#<anchor>` — `main` for local
   packs, which are never published. The `docs/` files keep their relative links. Each page gets a
   one-line pointer to the repo overview at the top, so the family is still discoverable.
2. **`CHANGELOG.md`** at the repo root, newest first, packed into every package:
   - one section per released version and an *Unreleased* section on top;
   - under each, **Breaking changes** first — anything that can make a consumer's code or
     documents fail or look different (an API that no longer compiles or behaves as before,
     changed default output such as design, fonts, page breaks or file size, Markdown that
     generated before and now fails), each saying what the consumer does about it — then **New**;
   - internal changes need no entry;
   - an entry *Before …* for the versions already published, pointing at `spec/60-verify/`.
   Because the version is a date, the changelog is the only place compatibility is stated; its
   header says so and tells consumers to read the sections between their version and the new one,
   breaking changes first.
3. **Release:** cutting a release renames *Unreleased* to the version (`## 2610.30.14`) in the
   commit the tag points at. `release.yml` fails if the tag does not match
   `v<yyMM>.<d>.<H>[.<r>]`, or if `CHANGELOG.md` has no section for the tag's version, and creates
   a GitHub Release with that section as its notes.
4. **Process:** `ai/workflow.md` step 4 adds "**Breaking changes**, or that there are none" to the
   implementation notes and the same to `CHANGELOG.md`; `ai/standards.md` *Build and deployment*
   describes the changelog; `docs/README.md` explains versions and the changelog to consumers.
5. **Package descriptions:** review every `<Description>`; the first sentence says what goes in and
   what comes out.
6. **Check:** `run.sh pack`, then open each `.nupkg`: `README.md` is the package's page with
   absolute links, `CHANGELOG.md` is there; the links resolve on GitHub.

Best implemented before 00005–00008, so their entries go straight into the changelog.

## Acceptance criteria

- [ ] Every packable package carries its `docs/` page as `README.md`, with working links pinned to
      its version.
- [ ] `CHANGELOG.md` exists, is packed into every package, and the release fails without an entry
      for the tag's version.
- [ ] `ai/workflow.md`, `ai/standards.md` and `docs/README.md` describe the changelog and the
      breaking-changes step.

## Open questions

1. Versioning: keep SemVer as `ai/standards.md` says (the next tag being e.g. `v0.2.0`), or adopt
   the date scheme the tags already use (`yyMM.d.H[.r]`) and correct the standard? With the date
   scheme the changelog is the only place compatibility is stated.

> The date scheme `yyMM.d.H[.r]`. `ai/standards.md` was corrected on 2026-10-23; this plan's
> context and steps 2–3 follow it.

2. Package README: the package's own page (proposed), or keep the shared overview and append the
   package's page below it?

> Confirmed by the human (2026-10-23): the package's own page, with a line pointing to the repo under its title.

3. Should the release workflow also create the GitHub Release (step 3), or only check the entry?

> Confirmed by the human (2026-10-23): yes, it creates the GitHub release from the changelog section (and leaves an existing one alone on a re-run).

## Notes

<!-- Free space for remarks that are not answers to the questions above. -->

## Implementation notes

- **README per package:** an inline `RoslynCodeTaskFactory` task in `src/Directory.Build.props`
  writes `obj/.../README.md` from `docs/<PackageId>.md` before packing: links to sibling pages
  become `https://github.com/wwosik/doc-libs/blob/<ref>/docs/<page>#<anchor>`, where `<ref>` is
  `GITHUB_REF_NAME` when `GITHUB_REF_TYPE` is `tag` (the release build) and `main` otherwise; a line
  under the title points to the repo and the changelog. The root `README.md` is no longer packed;
  `AGENTS.md` and `ai/standards.md` say so.
- **`CHANGELOG.md`** with an *Unreleased* section holding 00005–00009, and one section for the two
  versions already published. Packed into every package.
- **`release.yml`:** checks the tag against `v<yyMM>.<d>.<H>[.<r>]` (month 01–12, day 1–31, hour
  0–23, no leading zeros on day and hour), extracts `## <version>` from `CHANGELOG.md` (fails without
  it), and after pushing to nuget.org runs `gh release create --verify-tag` with that section as
  notes; the job's `contents` permission is now `write` for that.
- **Rules:** `ai/workflow.md` step 4 asks for *Breaking changes* and the changelog entry;
  `ai/standards.md` describes the changelog and how to cut a release; `docs/README.md` explains
  versions to consumers; `README.md` links the changelog and the authoring guides.
- Package descriptions: the PPTX one mentions embedded fonts; the others already said what goes in
  and out.

### Verified

- `dotnet pack` locally and as a simulated tag build (`GITHUB_REF_TYPE=tag`,
  `GITHUB_REF_NAME=v2610.23.20`): every `.nupkg` holds `README.md` (its page, links at
  `blob/v2610.23.20/…`) and `CHANGELOG.md`; the GitHub URL pattern resolves for `main`.
- The tag check and the changelog extraction were run against valid and invalid samples; the
  workflow parses as YAML. **It has not run on GitHub** — the first real tag is its test.

### To do at the next release

- Rename *Unreleased* to the release's version in the commit that gets tagged.

### Breaking changes

- None for code. Package pages on nuget.org now show each package's own documentation instead of
  the repo overview.
