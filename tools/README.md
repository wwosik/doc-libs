# tools

Helper tools for this repo, invoked through the launcher at the repo root:

```
./run.ps1              # lists verbs and tools
./run.ps1 <tool> [args...]
```

One folder per tool, with a single-file .NET entry point named after the folder:
`tools/<name>/<name>.cs`. NuGet dependencies go in `#:package` directives at the top of that
file. Nothing here is a multi-file project, and nothing here is PowerShell or Python.

Ready-made npm packages are used as-is under `tools/npm` (pnpm) rather than reimplemented.

This folder is English throughout — code, comments, and output — whatever language the rest of
the repo is in.
