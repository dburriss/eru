---
status: todo
---

# Plan: `eru add` command

## Context

`eru add` is the core pull command — it fetches a file (or a set of files matched by tag or collection) from a configured knowledge source and writes it into the local repo, recording a `LockEntry` in `eru.lock` for each pulled file. Without this command, users have no way to actually get content from a source into their project.

Three modes:

- **Direct path pull** — `eru add <remote-path>` or `eru add <source>:<remote-path>`: pull one specific file.
- **Tag-based pull** — `eru add --tag <tag> [--tag <tag>]...`: pull all files from global collections matching all given tags (AND semantics).
- **Collection pull** — `eru add --collection <name>` or `eru add --collection <source>:<name>`: pull every file in a named collection.

Named arguments (remote-path and --collection) support an optional `source:` discriminator prefix. Tags do not use this convention since they are source-agnostic.

---

## Command shape

```
eru add <remote-path> [--source <name>]
eru add <source>:<remote-path>
eru add --tag <tag> [--tag <tag>]...
eru add --collection <name>
eru add --collection <source>:<name>
```

- `<remote-path>` — optional positional; path within the source repo (relative to `BasePath` if set). May be prefixed with `source:` to embed the source name.
- `--source` / `-s` — source name fallback for direct path mode when no `source:` prefix is used
- `--tag` / `-t` — tag filter; repeatable; AND semantics
- `--collection` / `-c` — collection name from global config; may be prefixed with `source:` to restrict to files from that source only
- `--target` / `-d` — local directory to write files into; prepended to the derived `localPath`

At least one of `<remote-path>`, `--tag`, or `--collection` must be supplied — validated in domain, not CLI layer.

### Discriminator parsing

A private helper splits on the first `:` when the prefix matches a known source name:

```fsharp
let private parseDiscriminator (value: string) : string option * string =
    match value.IndexOf(':') with
    | -1 -> None, value
    | i  -> Some value.[..i-1], value.[i+1..]
// caller validates whether the prefix is a real source name
```

This is applied to the raw positional `<remote-path>` and the `--collection` value before any further logic.

---

## Local path derivation

Step 1 — strip source `BasePath`:
- `source.BasePath = Some "KNOWLEDGE"`, `remotePath = "KNOWLEDGE/dotnet/Logging.fs"` → `"dotnet/Logging.fs"`
- If `remotePath` does not start with the basePath prefix, use `remotePath` as-is
- `source.BasePath = None` → `localPath = remotePath`

Step 2 — apply `--target` prefix (if provided):
- `target = "docs"`, stripped path = `"dotnet/Logging.fs"` → `localPath = "docs/dotnet/Logging.fs"`
- No `--target` → `localPath` stays as-is after step 1

---

## Direct path pull algorithm

1. Read and merge config: `deps.ReadGlobalConfig ()` + `deps.ReadLocalConfig ()` → `Config.merge` → `EffectiveConfig`
2. Parse discriminator from `cmd.RemotePath`: `parseDiscriminator rawPath` → `(embeddedSource option, remotePath)`
3. Select source (first match wins): embedded prefix → `cmd.SourceName` → first in `EffectiveConfig.Sources`; error if list is empty or name not found
4. Error if selected source has no `Url`
5. Resolve branch: `source.Branch |> Option.defaultValue "HEAD"`
6. Call `deps.FetchRemoteContent url branch remotePath` — error on `Error`
7. Derive `localPath` using two-step derivation (strip BasePath, apply `cmd.Target`)
8. `deps.WriteLocalFile localPath content`
9. Compute `hash = deps.HashContent content`
10. Read lock entries: `deps.ReadLockEntries effectiveConfig.StateFile`
11. Replace any existing entry for `localPath`, append new `LockEntry { LocalPath; SourceName; RemotePath; ContentHash = hash }`
12. Write back: `deps.WriteLockEntries effectiveConfig.StateFile updatedEntries`
13. Print: `Pulled <remotePath> → <localPath>` and return `0`

---

## Tag-based pull algorithm

1. Read and merge config (same as above)
2. Read global config: `deps.ReadGlobalConfig ()` — error if `None`
3. Call `Config.resolveByTags tags globalCfg` → `(sourceName, remotePath) list`
4. If empty → print `No files found matching tags: <tag1>, <tag2>` and return `1`
5. For each `(sourceName, remotePath)` pair:
   - Find source in `EffectiveConfig.Sources` — skip with warning if not found
   - Error if source has no URL
   - Fetch, derive localPath (with `cmd.Target`), write, hash
   - Accumulate lock entry
6. Read existing lock entries; replace/append; write back
7. Print: `Pulled <n> file(s)` and return `0`

---

## Collection pull algorithm

1. Read and merge config (same as above)
2. Read global config: `deps.ReadGlobalConfig ()` — error if `None`
3. Parse discriminator from `cmd.Collection`: `parseDiscriminator raw` → `(filterSource option, collectionName)`
4. Find collection in `globalCfg.Collections` by name — error if not found
5. Resolve file refs: `collection.Files` filtered by `filterSource` (if prefix was given, keep only files where `f.Source = filterSource`)
6. If no files remain after filtering → error: `No files in collection '<name>' from source '<filterSource>'`
7. For each `CollectionFileRef`:
   - Find source in `EffectiveConfig.Sources`
   - Fetch, derive localPath (with `cmd.Target`), write, hash
   - Accumulate lock entry
8. Read existing lock entries; replace/append; write back
9. Print: `Pulled <n> file(s) from collection '<name>'` and return `0`

---

## Error cases

| Situation | Behaviour |
|---|---|
| No `remote-path`, `--tag`, or `--collection` | `Error: specify a remote path, --collection, or at least one --tag.` → exit 1 |
| Local config missing | `Error: no eru.json found. Run 'eru init' first.` → exit 1 |
| Global config missing (tag/collection mode) | `Error: no global config found; collections require global config.` → exit 1 |
| Named source not configured | `Error: source '<name>' not configured.` → exit 1 |
| No sources configured at all | `Error: no sources configured. Run 'eru source add' first.` → exit 1 |
| Source has no URL | `Error: source '<name>' has no URL.` → exit 1 |
| Fetch fails | `Error: could not fetch '<path>' from '<source>': <reason>` → exit 1 |
| No files match tags | `No files found matching tags: <tag>` → exit 1 |
| Collection not found | `Error: collection '<name>' not found in global config.` → exit 1 |
| Source filter leaves no files | `Error: no files in collection '<name>' from source '<source>'.` → exit 1 |

---

## Files to create or modify

### Modified: `src/Eru.Domain/Add.fs`

Update `Command` type to add `CollectionName` and `Target`:

```fsharp
type Command = {
    RemotePath     : string option
    Tags           : string list
    SourceName     : string option
    CollectionName : string option
    Target         : string option
}
```

Replace the stub `run` with the full implementation. Key private helpers:

```fsharp
let private parseDiscriminator (value: string) : string option * string
let private deriveLocalPath (basePath: string option) (target: string option) (remotePath: string) : string
let private pullOne (deps: Deps) (sources: SourceConfig list) (target: string option) (sourceName: string) (remotePath: string) : Result<LockEntry, string>
```

`run` dispatches: `cmd.CollectionName` → collection pull; `cmd.Tags` non-empty → tag pull; `cmd.RemotePath` → direct pull; otherwise error. All three paths call `pullOne` per file and share a single lock file read-replace-write at the end.

### Modified: `src/Eru.Cli/Args.fs`

Change `Remote_Path` from `ExactlyOnce` to `Optional`; add `Collection` and `Target`:

```fsharp
type AddArgs =
    | [<MainCommand; Optional>] Remote_Path  of remotePath: string
    | [<AltCommandLine("-t")>]  Tag          of tag: string
    | [<AltCommandLine("-s")>]  Source       of sourceName: string
    | [<AltCommandLine("-c")>]  Collection   of collectionName: string
    | [<AltCommandLine("-d")>]  Target       of targetPath: string
```

### Modified: `src/Eru.Cli/Program.fs`

Update the `AddCmd` branch to wire the two new fields:

```fsharp
let cmd : Add.Command = {
    RemotePath     = args.TryGetResult AddArgs.Remote_Path
    Tags           = args.GetResults  AddArgs.Tag
    SourceName     = args.TryGetResult AddArgs.Source
    CollectionName = args.TryGetResult AddArgs.Collection
    Target         = args.TryGetResult AddArgs.Target
}
```

### New: `tests/Eru.Tests/AddTests.fs`

Test `Add.run` with in-memory fake `Deps` (same pattern as `SourceTests.fs`).

Test cases:
- Direct pull: writes file and lock entry with correct `localPath`, `sourceName`, `remotePath`, `hash`
- Source `BasePath` prefix is stripped from `localPath`
- `--target` prefix is prepended to derived `localPath`
- Both `BasePath` strip and `--target` prefix applied together
- `source:path` discriminator prefix selects the right source
- `--source` fallback selects the right source when no prefix
- Default to first source when neither prefix nor `--source` given
- Unknown source name → error
- Source with no URL → error
- No sources configured → error
- Tag pull: resolves files from `globalCfg.Collections`, pulls all matched
- Tag pull with no global config → error
- Tag pull with no matching files → exit 1
- Collection pull: fetches all files in named collection
- Collection pull with `source:name` prefix filters to that source's files only
- Collection pull with unknown collection → error
- Collection pull source filter leaves no files → error
- Existing lock entry for same `localPath` is replaced, not duplicated
- No `remote-path`, `--tag`, or `--collection` → error

Add `AddTests.fs` to `Eru.Tests.fsproj` after `SourceTests.fs`.

---

## Verification

```bash
dotnet build
dotnet test

# Smoke tests (once git fetch adapter is wired — currently stubs return Error):
eru init
eru source add https://github.com/acme/knowledge-base.git --basepath KNOWLEDGE

# Direct pull — both forms equivalent:
eru add KNOWLEDGE/shared/templates/adr.md --source knowledge-base
eru add knowledge-base:KNOWLEDGE/shared/templates/adr.md
cat eru.lock     # one entry: localPath, sourceName:remotePath, sha256 hash

# Direct pull with target:
eru add knowledge-base:KNOWLEDGE/shared/templates/adr.md --target docs/
cat eru.lock     # localPath = docs/shared/templates/adr.md

# Tag-based (requires global config with collections):
eru add --tag dotnet --tag observability
cat eru.lock     # entries for all matched collection files

# Collection pull:
eru add --collection dotnet-starter
eru add --collection knowledge-base:dotnet-starter   # restrict to one source
cat eru.lock     # entries for all files in the collection

# Error cases:
eru add                                # Error: specify a remote path, --collection, or at least one --tag
eru add some/path --source nope        # Error: source 'nope' not configured
eru add --collection nonexistent       # Error: collection 'nonexistent' not found in global config
```
