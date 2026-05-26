---
status: todo
---

# Plan: `eru search` command

## Context

`eru search` allows users to discover knowledge files across all configured sources. The CLI plumbing (args, command mapper, program dispatch) is already in place; only `Search.run` in `Search.fs` needs implementing — it currently prints "not yet implemented" and exits 0.

The search covers two data sources that are available without network access:
1. **Global config collections** — the curated catalog of tagged knowledge files (`GlobalConfig.Collections[].Files`)
2. **Lock file entries** — files already pulled into this repo (from `eru.lock`)

Results from both are merged by `(sourceName, remotePath)` so a file appearing in both is shown once, enriched with its local path.

---

## Query semantics

- **Terms** (`eru search foo bar`) — OR semantics; a file matches if its `RemotePath` or `LocalPath` (if present) contains any term (case-insensitive substring match)
- **Tags** (`-t dotnet -t observability`) — AND semantics; a file must carry all specified tags (case-insensitive exact match); tags come from the collection file ref and/or its parent collection
- Both can be combined; a result must satisfy the tag filter AND the term filter

---

## Config changes

Add optional `Description` to two types in `src/Eru.Domain/Config.fs`:

```fsharp
type CollectionFileRef = {
    Source      : string
    RemotePath  : string
    Tags        : string list
    Description : string option   // NEW
}

type CollectionConfig = {
    Name        : string
    Tags        : string list
    Files       : CollectionFileRef list
    Description : string option   // NEW
}
```

`System.Text.Json` + the existing `OptionConverter` will deserialise missing/null fields as `None` automatically — no serialisation code changes needed.

---

## New result type

Add `SearchResult` to `Search.fs`:

```fsharp
type SearchResult = {
    SourceName  : string
    RemotePath  : string
    Tags        : string list
    Description : string option
    LocalPath   : string option   // Some if the file is in the lock file
}
```

Description is taken from `CollectionFileRef.Description` first; if absent, fall back to the parent `CollectionConfig.Description`. Lock-only entries have `Description = None`.

---

## `Search.run` algorithm

1. Read global + local config; error on failure
2. `Config.merge` → `EffectiveConfig`; error on failure
3. **Build collection results** — iterate `globalCfg.Collections`; for each `CollectionFileRef` emit a `SearchResult` with merged tags (`file.Tags @ col.Tags |> List.distinct`) and description (`file.Description |> Option.orElse col.Description`); if global config is absent, collection results are empty
4. **Build lock results** — `deps.ReadLockEntries eff.StateFile`; on error treat as empty (lock may not exist); map each `LockEntry` to a `SearchResult` with `Tags = [], Description = None`
5. **Merge** — for each collection result, check the lock map `(sourceName, remotePath) → localPath`; if found, set `LocalPath = Some lp`; append lock-only entries (those not in any collection)
6. **Filter by tags** — keep results where all query tags appear in `result.Tags` (skip if `query.Tags = []`)
7. **Filter by terms** — keep results where any query term is a case-insensitive substring of `result.RemotePath`, `result.LocalPath`, or `result.Description` (skip if `query.Terms = []`)
8. If nothing matches, print `"No results found."` and return `0`
9. Print each result and return `0`

### Output format (one line per result)

```
<sourceName>:<remotePath>  [tags: <t1>, <t2>]  [local: <localPath>]
  <description>
```

Description is printed on a second indented line only when present. Tag and local segments are omitted when empty/absent.

---

## Files to modify

### `src/Eru.Domain/Config.fs`
Add `Description : string option` to `CollectionFileRef` and `CollectionConfig`. No logic changes needed — JSON deserialisation handles missing fields as `None` via the existing `OptionConverter`.

### `src/Eru.Domain/Search.fs`
Replace the stub with:
- `SearchResult` type (with `Description` and `LocalPath` fields)
- Private helpers: `matchesTerm` (checks RemotePath, LocalPath, Description), `matchesTags`, `mergeResults`
- Full `run` implementation per algorithm above

No changes needed to `Args.fs`, `CommandMapper.fs`, or `Program.fs` — all wiring is already correct.

### New: `tests/Eru.Tests/SearchTests.fs`
Follow the same `makeDeps` + inline-function pattern used in `AddTests.fs` and `SyncTests.fs`.

Test cases:
- No query (`Terms = [], Tags = []`) → returns all results (collection + lock)
- Term filter (OR): single term matches path substring
- Term filter: multiple terms, result matches one of them
- Term filter: match against description text
- Term filter: no match → "No results found." exit 0
- Tag filter (AND): single tag matches collection file
- Tag filter: multiple tags, all must match
- Tag filter: partial match (only one of two tags) → excluded
- Combined term + tag: must satisfy both
- Lock-only entry (not in any collection) appears in results
- Collection entry enriched with local path from lock
- Files in both collection and lock shown once (not duplicated)
- File description taken from `CollectionFileRef.Description` when present
- File description falls back to parent `CollectionConfig.Description` when file has none
- Global config absent → only lock results are searched
- Lock file absent/empty → only collection results are searched
- Empty global config and empty lock → "No results found."

### `tests/Eru.Tests/Eru.Tests.fsproj`
Add `<Compile Include="SearchTests.fs" />` after `SyncTests.fs`.

---

## Reused utilities

- `Config.merge` (`src/Eru.Domain/Config.fs`) — already used by Add and Sync
- `Config.resolveByTags` is NOT used here (it returns pairs for pulling; search needs richer data including tags)
- `deps.ReadGlobalConfig`, `deps.ReadLocalConfig`, `deps.ReadLockEntries` — all already in `Deps`

---

## Verification

```bash
dotnet build
dotnet test

# Smoke test (with real sources configured):
eru search                         # all known files
eru search logging                 # files with "logging" in path
eru search -t dotnet               # files tagged "dotnet"
eru search logging -t dotnet       # intersection: logging in path AND dotnet tag
eru search foo bar                 # files matching "foo" OR "bar" in path
```
