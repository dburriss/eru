---
status: planned
---

# Plan: `eru cache clear`

## Context

Users occasionally need to wipe the entire local knowledge cache — for example after a corrupt sync, to reclaim disk space, or to force a fully clean re-sync. The existing `eru cache prune` removes only orphaned content files that are no longer referenced by any source index. There is no command to clear everything. This plan adds `eru cache clear` as a new subcommand of the existing `cache` group.

---

## What Gets Deleted

Three directories under `~/.cache/eru/`, resolved via existing `Paths` functions:

| Directory | `Paths` function | Contains |
|---|---|---|
| `sources/` | `sourceCacheManifestPath "dummy" \|> GetDirectoryName \|> GetDirectoryName` | Per-source `manifest.json`, `index.json`, `files/<hash>` |
| `index/` | `Paths.searchIndexDir()` | Full-text search word-index JSON files |
| `collections/` | `Paths.collectionCachePath()` | Collection file cache used by `IndexService` |

`mcp.log` and all config files are left untouched.

---

## Implementation

### `src/Eru.Cli/Args.fs`

Add `CacheClearArgs` immediately before `CacheArgs`, then add a `Clear` case to `CacheArgs`:

```fsharp
type CacheClearArgs =
    | [<Unique>]                         Dryrun
    | [<Unique; AltCommandLine("-o")>]   Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Dryrun   -> "List what would be deleted without deleting anything."
            | Output _ -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type CacheArgs =
    | [<SubCommand>] Prune of ParseResults<CachePruneArgs>
    | [<SubCommand>] Clear of ParseResults<CacheClearArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Prune _ -> "Remove orphaned content files not referenced by any source index."
            | Clear _ -> "Delete all cached indexes and files."
```

### `src/Eru.Cli/CacheClearCli.fs` (new file)

Pattern mirrors `CachePruneCli.fs`. Structure:

- Active pattern `CacheClearCmd` extracts `CacheArgs.Clear clearArgs` from parsed results.
- `runClear (clearArgs: ParseResults<CacheClearArgs>) : int`:
  1. Extract `dryRun = clearArgs.Contains CacheClearArgs.Dryrun`
  2. Extract `format = parseFormat (clearArgs.TryGetResult CacheClearArgs.Output)` (reuse `OutputFormat.parseFormat`)
  3. Build `targets` list of the three cache dirs, filtered to those that `Directory.Exists`
  4. If `targets` is empty: print "Cache is already empty." and return 0
  5. In dryrun mode: render the target paths per format and return 0 (no confirmation, no deletion)
  6. In real mode: render the target paths, prompt `[y/N]`, then `Directory.Delete(path, true)` for each confirmed target

Output rendering uses the established three-function pattern:
- `renderText`: one path per line via `printfn`
- `renderTable`: Spectre.Console table with Path and Status columns (`Would delete` / `Deleted`)
- `renderJson`: serialize `{| path: string; deleted: bool |}` list via `System.Text.Json`

### `src/Eru.Cli/Eru.Cli.fsproj`

Insert `CacheClearCli.fs` immediately after `CachePruneCli.fs`:

```xml
<Compile Include="CachePruneCli.fs" />
<Compile Include="CacheClearCli.fs" />
<Compile Include="Program.fs" />
```

### `src/Eru.Cli/Program.fs`

Add `open Eru.Cli.CacheClearCli` with the existing opens, then add the dispatch case alongside the prune case:

```fsharp
| CacheClearCmd clearArgs -> CacheClearCli.runClear clearArgs
```

---

## Files to Modify

| File | Change |
|---|---|
| `src/Eru.Cli/Args.fs` | Add `CacheClearArgs` type; add `Clear` case to `CacheArgs` |
| `src/Eru.Cli/CacheClearCli.fs` | New file — active pattern, `runClear`, three render functions |
| `src/Eru.Cli/Eru.Cli.fsproj` | Add `CacheClearCli.fs` to compile order |
| `src/Eru.Cli/Program.fs` | Add `open` and dispatch case |

---

## Verification

```bash
dotnet build src/Eru.Cli/Eru.Cli.fsproj

# Help text
eru cache --help
eru cache clear --help

# Dryrun — lists the 3 cache dirs, no deletion
eru cache clear --dryrun
eru cache clear --dryrun --output text
eru cache clear --dryrun --output json

# Actual clear (answer y at prompt)
eru cache clear

# After clearing, dryrun should report "Cache is already empty."
eru cache clear --dryrun
```
