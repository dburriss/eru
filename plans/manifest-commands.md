---
status: done
---

# Plan: `eru manifest` command group

## Context

`.eru/manifest.json` is the file that knowledge-source repos publish to declare which files they expose. Source repo owners currently have to hand-craft this JSON. This command group gives producers a CLI to create, edit, and validate manifests without touching JSON directly.

## Commands

```
eru manifest init [--force]
eru manifest add <path> [-t tag]... [-d description] [--dryrun]
eru manifest remove <path> [--dryrun]
eru manifest verify
```

- `init` — creates `.eru/manifest.json` in the current directory with `{ version: 1, files: [] }`; fails if one exists unless `--force`
- `add` — appends a `ManifestFileRef` entry; path supports gitignore-style globs; errors if path already present
- `remove` — removes the entry whose `path` matches exactly; errors if not found
- `verify` — resolves every path (including globs) against local files using the existing `Patterns.matchesGlob`; exits 1 if any entry resolves to no files

## Domain changes

**`Deps.fs`**: Add three fields:
```fsharp
ReadLocalManifest  : unit -> Result<SourceManifest option, string>
WriteLocalManifest : SourceManifest -> Result<unit, string>
ResolveLocalGlob   : string -> string list   // pattern -> matching relative paths in cwd
```

**`Manifest.fs`** (new): Four command records and functions following the `(deps: Deps) (cmd: XxxCommand) : int` pattern:
- `InitCommand { Force: bool }` → `init`
- `AddFileCommand { Path; Tags; Description; DryRun }` → `addFile`
- `RemoveFileCommand { Path; DryRun }` → `removeFile`
- `verify : Deps -> int` (no command record — no params needed)

`verify` calls `deps.ResolveLocalGlob` for each `ManifestFileRef.Path`, collects entries with empty results, prints each to stderr and returns exit 1 if any.

## Adapter changes

**`Paths.fs`**: Add:
```fsharp
let localManifestPath (cwd: string) = IO.Path.Combine(cwd, ".eru", "manifest.json")
```

**`ManifestAdapter.fs`**: Add three functions:
- `readLocalManifest cwd` — reads `localManifestPath cwd`, deserialises to `SourceManifest option`
- `writeLocalManifest cwd manifest` — serialises (via existing `Serialization.serialize`) and writes; creates `.eru/` dir if needed
- `resolveLocalGlob cwd pattern` — walks `Directory.EnumerateFiles(cwd, "*", AllDirectories)`, converts each to a cwd-relative forward-slash path, filters with existing `Patterns.matchesGlob pattern`

No new NuGet packages — reuses the existing `Patterns.matchesGlob` regex implementation.

**`AdapterDeps.fs`**: Wire new deps using the closures over `cwd`:
```fsharp
ReadLocalManifest  = fun () -> ManifestAdapter.readLocalManifest cwd
WriteLocalManifest = ManifestAdapter.writeLocalManifest cwd
ResolveLocalGlob   = ManifestAdapter.resolveLocalGlob cwd
```

## CLI wiring

Follows the `source`/`collection` subcommand pattern:

**`Args.fs`**: Add `ManifestInitArgs`, `ManifestAddArgs`, `ManifestRemoveArgs`, `ManifestVerifyArgs`, and a `ManifestArgs` parent DU with `Init | Add | Remove | Verify` sub-cases. Add `| [<SubCommand>] Manifest of ParseResults<ManifestArgs>` to `EruArgs`.

**`CommandMapper.fs`**: Add four active patterns: `(|ManifestInitCmd|_|)`, `(|ManifestAddCmd|_|)`, `(|ManifestRemoveCmd|_|)`, `(|ManifestVerifyCmd|_|)`.

**`Program.fs`**: Add dispatch arms for all four patterns.

## Files changed

- `src/Eru.Domain/Deps.fs`
- `src/Eru.Domain/Manifest.fs` (new)
- `src/Eru.Domain/Eru.Domain.fsproj`
- `src/Eru.Adapters/Paths.fs`
- `src/Eru.Adapters/ManifestAdapter.fs`
- `src/Eru.Adapters/AdapterDeps.fs`
- `src/Eru.Cli/Args.fs`
- `src/Eru.Cli/CommandMapper/CommandMapper.fs`
- `src/Eru.Cli/Program.fs`
- `tests/Eru.Tests/ManifestTests.fs` (new)
- `tests/Eru.Tests/Eru.Tests.fsproj`

## Verification

```bash
dotnet build
dotnet test
dotnet run --project src/Eru -- manifest init
dotnet run --project src/Eru -- manifest add "docs/*.md" -t docs -d "All docs"
dotnet run --project src/Eru -- manifest add "README.md" -t meta
dotnet run --project src/Eru -- manifest verify
dotnet run --project src/Eru -- manifest remove "docs/*.md"
dotnet run --project src/Eru -- manifest verify   # exits 1 if README.md absent
```
