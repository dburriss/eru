---
status: done
---

# Plan: `eru sync` command

## Context

`eru sync` reconciles the local repo's state file (`eru.lock`) against the configured knowledge sources. Every `LockEntry` records where a file came from and its content hash at pull time. Sync re-fetches each tracked file, compares the current remote hash against the stored hash, and — unless `--dry-run` — overwrites stale local files and updates the lock entry hash.

The command scaffolding already exists end-to-end: `SyncArgs`/`SyncCmd`/`Sync.run` are wired from CLI to domain. Only the `Sync.run` body is a stub. The adapter `FetchRemoteContent` is also still a stub — this plan includes implementing it.

---

## Part 1: Git Adapter (`FetchRemoteContent` + `ListRemoteTopLevel`)

### Approach

Add a `GitAdapter` module in `Eru.Adapters` that uses **SimpleExec** (per AGENTS.md) to shell out to `git`. SimpleExec is not yet referenced in the adapters project — add the NuGet package to `Eru.Adapters.fsproj`.

Both operations use a **shallow sparse clone** to a temp directory (prefixed `eru-`) so that only the required file(s) are downloaded:

```bash
# FetchRemoteContent url branch path
git clone --filter=blob:none --sparse --depth=1 --branch <branch> <url> <tmpDir>
git -C <tmpDir> sparse-checkout set <path>
# then File.ReadAllText(Path.Combine(tmpDir, path))
```

```bash
# ListRemoteTopLevel url branch
git clone --filter=blob:none --depth=1 --no-checkout --branch <branch> <url> <tmpDir>
git -C <tmpDir> ls-tree HEAD --name-only
```

Both clean up the temp dir on completion or error. The `--filter=blob:none` + `--sparse` flags avoid downloading file content until `sparse-checkout set` is called, keeping this efficient.

### `FetchRemoteContent` signature (from `Deps`):
```fsharp
FetchRemoteContent : string -> string -> string -> Result<string, string>
// url -> branch -> remotePath -> file content or error
```

### `ListRemoteTopLevel` signature:
```fsharp
ListRemoteTopLevel : string -> string option -> Result<string list, string>
// url -> branch option -> top-level entry names
```

### New file: `src/Eru.Adapters/GitAdapter.fs`

```fsharp
module GitAdapter =
    let fetchRemoteContent (url: string) (branch: string) (path: string) : Result<string, string>
    let listRemoteTopLevel (url: string) (branch: string option) : Result<string list, string>
```

### Files modified:
- `src/Eru.Adapters/Eru.Adapters.fsproj` — add `<PackageReference Include="SimpleExec" Version="13.*" />` and `<Compile Include="GitAdapter.fs" />`
- `src/Eru.Adapters/AdapterDeps.fs` — wire `FetchRemoteContent` and `ListRemoteTopLevel` to `GitAdapter.*`

---

## Part 2: `Sync.run` domain logic

### Status model

For each lock entry, derive one of four statuses (using existing `ArtifactStatus` DU in `src/Eru.Domain/Domain.fs`):

| Status | Condition |
|---|---|
| `Current` | Remote hash equals `entry.ContentHash` |
| `Drifted` | Remote hash differs from `entry.ContentHash` |
| `Missing` | Fetch returns `Error` (file not found on remote) |
| `Skipped` | Source not in config, or source has no URL |

Initial implementation assumes `Upstream` sync policy (source always wins). `LockEntry` has no `SyncPolicy` field yet — full policy support is a future concern.

### Algorithm (`Sync.run`)

1. Read + merge config: `deps.ReadGlobalConfig()` + `deps.ReadLocalConfig()` → `Config.merge` → `EffectiveConfig`; error if either fails.
2. Read lock entries: `deps.ReadLockEntries eff.StateFile`; empty list is valid.
3. For each `LockEntry`, call a private `classifyEntry` helper:
   - Source not in `eff.Sources` → `Skipped "source '<name>' not configured"`
   - Source has no URL → `Skipped "source '<name>' has no URL"`
   - `deps.FetchRemoteContent url branch entry.RemotePath` returns `Error` → `Missing`
   - `Ok content`: compute `hash = deps.HashContent content`
     - `hash = entry.ContentHash` → `Current`
     - `hash ≠ entry.ContentHash` → `Drifted (entry, content)`
4. **Dry-run** (`opts.DryRun = true`): print per-entry status lines, print summary, return `0`, write nothing.
5. **Real run**: for each `Drifted (entry, content)` result:
   - `deps.WriteLocalFile entry.LocalPath content` — on error, exit 1
   - Update `entry.ContentHash` in the lock list
   - Leave `Current` and `Missing` entries unchanged
   - `deps.WriteLockEntries eff.StateFile updatedEntries` — on error, exit 1
   - Print summary, return `0`.

### Output format

```
[current]  shared/logging/Logger.fs
[drifted]  shared/di/ServiceCollectionExtensions.fs    ← dry-run label
[updated]  shared/di/ServiceCollectionExtensions.fs    ← real-run label
[missing]  shared/old/Deprecated.fs
[skipped]  shared/other/File.fs  (source 'other' not configured)

Sync complete: 1 updated, 1 current, 1 missing, 1 skipped.
```

### File modified: `src/Eru.Domain/Sync.fs`

Replace the stub body. Key private helpers:

```fsharp
type private EntryResult =
    | Current of LockEntry
    | Drifted of LockEntry * string   // entry + new content
    | Missing of LockEntry
    | Skipped of LockEntry * string   // entry + reason

let private classifyEntry (deps: Deps) (sources: SourceConfig list) (entry: LockEntry) : EntryResult
```

---

## Part 3: Tests

### 3a. Acceptance tests — `tests/Eru.Tests/SyncTests.fs`

Tests for `Sync.run` using a fake in-memory `Deps` record (same pattern as `AddTests.fs`).
These verify domain logic in isolation — no real filesystem or git involved.

Follow `AddTests.fs` exactly: mutable `CapturedState` + `makeDeps` factory.

Test cases:
- All entries current → exit 0, no file writes, lock unchanged
- One drifted entry → file overwritten, lock hash updated, exit 0
- Multiple drifted entries → all updated
- `--dry-run` with drifted entry → no writes, prints `[drifted]`, exit 0
- `--dry-run` all current → no writes, prints `[current]`, exit 0
- Missing entry (fetch returns `Error`) → `[missing]` printed, lock unchanged, exit 0
- Skipped: source not in config → `[skipped]` printed, no fetch attempted, exit 0
- Skipped: source has no URL → `[skipped]` printed, exit 0
- Empty lock file → exit 0, "0 updated"
- No local config → exit 1 with error message
- Lock write failure → exit 1

Register after `AddTests.fs` in `Eru.Tests.fsproj`.

### 3b. Communication tests — `tests/Eru.Tests/GitAdapterTests.fs`

Tests for `GitAdapter.fetchRemoteContent` and `GitAdapter.listRemoteTopLevel` against a **real local git repo**. These verify that the adapter correctly shells out to git and parses the results — no fake deps, no mocking.

**Setup pattern per test:**
1. Create a temp directory via `Path.Combine(Path.GetTempPath(), "eru-" + Guid.NewGuid().ToString())`
2. `git init -b main <tmpDir>`
3. Write one or more files, `git -C <tmpDir> add .`, `git -C <tmpDir> commit -m "init"`
4. Use `"file://<tmpDir>"` as the URL — git supports local `file://` remotes for all transport operations

**`fetchRemoteContent` test cases:**
- File exists at root → returns `Ok` with correct content
- File exists in subdirectory → returns `Ok` with correct content
- File does not exist → returns `Error`
- Branch does not exist → returns `Error`

**`listRemoteTopLevel` test cases:**
- Repo with files at root and in subdirs → returns only top-level names (no `/` in entries)
- Branch with `KNOWLEDGE/` directory → `"KNOWLEDGE"` appears in result (drives `detectBasePath` in `Source.fs`)
- `branch = None` → falls back to a default (e.g. `"HEAD"` or `"main"`)

Each test cleans up its temp dir in a `finally` block.

Register after `SyncTests.fs` in `Eru.Tests.fsproj`.

---

## Files to create or modify

| File | Action |
|---|---|
| `src/Eru.Adapters/GitAdapter.fs` | Create — `fetchRemoteContent` + `listRemoteTopLevel` |
| `src/Eru.Adapters/Eru.Adapters.fsproj` | Add SimpleExec package + compile `GitAdapter.fs` |
| `src/Eru.Adapters/AdapterDeps.fs` | Wire `FetchRemoteContent`/`ListRemoteTopLevel` to `GitAdapter` |
| `src/Eru.Domain/Sync.fs` | Replace stub with full logic |
| `tests/Eru.Tests/SyncTests.fs` | New — acceptance tests for `Sync.run` with fake deps |
| `tests/Eru.Tests/GitAdapterTests.fs` | New — communication tests for `GitAdapter` against local git repos |
| `tests/Eru.Tests/Eru.Tests.fsproj` | Register `SyncTests.fs` then `GitAdapterTests.fs` |

---

## Reuse

- `Config.merge` — `src/Eru.Domain/Config.fs`
- `ArtifactStatus` DU — `src/Eru.Domain/Domain.fs` (use `Current`/`Drifted`/`Missing`)
- `updateLockEntries` pattern from `src/Eru.Domain/Add.fs` — replicate inline in Sync

---

## Verification

```bash
dotnet build
dotnet test

# Sync-specific tests:
dotnet test --filter "FullyQualifiedName~Sync"

# End-to-end (requires a real source configured):
eru init
eru source add https://github.com/acme/knowledge-base.git
eru add knowledge-base:docs/example.md
# Modify the remote file, then:
eru sync --dry-run   # shows [drifted] for changed files
eru sync             # fetches and overwrites, updates eru.lock
```
