---
status: planned
---

# Plan: EntryId — formal identity for (Source, RemotePath)

## Context

An entry within a source is already, in practice, identified by the pair
`(SourceName, RemotePath)` — but that identity is currently expressed
inconsistently: `CollectionFileRef` and `LockEntry` carry it as two
separate named fields, `Config.fs`'s dedup/merge logic uses raw
`(string * string)` tuples (`Set<string*string>` in `withManifests`,
tuple lists in `resolveByTags`), and `eru.lock` serializes it ad hoc as a
colon-delimited `"sourceName:remotePath"` string parsed inline in
`LockFile.fs`. Nothing enforces these all agree on the same shape, and the
tuple-based spots are easy to transpose by accident (source/path swapped).

This plan introduces a single `EntryId` type that formalizes this identity,
centralizes its string (de)serialization, and replaces the raw-tuple call
sites with it. It's a structural cleanup — no behavior change, no on-disk
format change (the `eru.lock` `"source:remotePath"` string shape is
preserved, just produced/parsed through one function instead of being
duplicated).

This plan is a prerequisite for `plans/link-graph.md`: the graph's
`NodeId.InternalNode` case is defined in terms of `EntryId` rather than a
fresh `(string * string)` pair, so this should land first (or at least the
`EntryId` type itself should, if the two are done independently).

## Design

### 1. `src/Eru.Domain/Config.fs` (or a small new `Identity.fs` if preferred to avoid growing `Config.fs`)

```fsharp
type EntryId = { Source: string; RemotePath: string }

module EntryId =
    let toString (id: EntryId) = $"{id.Source}:{id.RemotePath}"
    let tryParse (s: string) : EntryId option =
        // split on the FIRST ':' only — must match LockFile.fs's existing
        // parse behavior exactly (RemotePath itself may not contain ':',
        // but preserve whatever the current split logic already assumes)
        match s.IndexOf(':') with
        | -1 -> None
        | i -> Some { Source = s.Substring(0, i); RemotePath = s.Substring(i + 1) }
```

Add conversion helpers rather than reshaping the existing records (avoids
touching YAML/JSON field names that `manifest.yaml`/`eru.lock` depend on):

```fsharp
module CollectionFileRef =
    let id (f: CollectionFileRef) : EntryId = { Source = f.Source; RemotePath = f.RemotePath }

module LockEntry =
    let id (e: LockEntry) : EntryId = { Source = e.SourceName; RemotePath = e.RemotePath }
```

### 2. Replace raw tuple usage with `EntryId`

- `Config.fs`, `withManifests` (~lines 254-270): `Set<string*string>` →
  `Set<EntryId>`, built via `CollectionFileRef.id`.
- `Config.fs`, `resolveByTags` (~lines 274-286): return `EntryId list`
  instead of `(string * string) list`.
- `Sync.fs`: the `Set.contains (e.SourceName, e.RemotePath) collectionPaths`
  checks (~lines 166-168, 213-214, 278-279) become
  `Set.contains (LockEntry.id e) collectionPaths`.

### 3. `LockFile.fs` — route existing parse/format through `EntryId`

- The inline colon-split/join logic (~lines 31-49, 72) is replaced with
  calls to `EntryId.toString` / `EntryId.tryParse`. On-disk format is
  unchanged — this is purely deduplicating the same logic into one place.
- `findByPathHash` (~lines 60-65) is unaffected (it hashes `RemotePath`
  alone, which stays a separate, intentionally source-agnostic UX concept
  per the earlier discussion — not part of this change).

### 4. Tests

- New `tests/Eru.Tests/EntryIdTests.fs`: `toString`/`tryParse` round-trip,
  and a `tryParse` case confirming it fails gracefully on a string with no
  `:`.
- No behavior change expected elsewhere — existing `SyncTests.fs`,
  `LockFile`-related tests, etc. should pass unmodified; run the full suite
  to confirm the tuple → `EntryId` swap didn't change dedup/lookup
  semantics.

## Verification

- `dotnet build` — compiles cleanly after the tuple → `EntryId` swap.
- `dotnet test` — full suite green, plus new `EntryIdTests.fs`.
- Manual: run `eru sync` and `eru source files` against a repo with an
  existing `eru.lock` (written by the pre-change code) and confirm it
  still parses/reconciles correctly — i.e. the on-disk format really is
  unchanged.
