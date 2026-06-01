# Plan: Batch file fetches per source during sync (one clone per repo, not per file)

## Context

`FetchRemoteContent` in `GitAdapter` does a fresh blobless sparse clone into a temp dir for every single file. `Sync.execute` calls it once per lock entry via `classifyEntry`, meaning 10 files from one repo = 10 clones. `KnowledgeSyncService` has the same problem for collection files. The fix is to batch all paths from the same source into a single clone call, reducing network I/O from O(files) to O(sources).

`git sparse-checkout set --no-cone` already accepts multiple paths in one invocation, so the adapter change is minimal.

## Changes

### 1. `src/Eru.Domain/Deps.fs`
Change the `FetchRemoteContent` field signature from single path to path list:
```fsharp
// Before
FetchRemoteContent : string -> string -> string -> Result<(string * string) list, string>
// After
FetchRemoteContent : string -> string -> string list -> Result<(string * string) list, string>
//                   url      branch    paths
```

### 2. `src/Eru.Adapters/GitAdapter.fs`
Update `fetchRemoteContent` to accept `string list` and join them for `sparse-checkout set`:
```fsharp
let fetchRemoteContent (verbose: bool) (url: string) (branch: string) (remotePaths: string list) =
    withTempDir (fun tmpDir ->
        try
            runGit verbose $"clone --filter=blob:none --sparse --depth=1 {branchFlag branch}-- {url} {tmpDir}" None
            let pathsArg = remotePaths |> String.concat " "
            runGit verbose $"sparse-checkout set --no-cone {pathsArg}" (Some tmpDir)
            // read all checked-out files as before
            ...
```

Missing files simply won't be checked out — they'll be absent from the returned list, which is the correct signal for `EMissing`. No error thrown for partial misses.

### 3. `src/Eru.Domain/Sync.fs` — core optimisation
Refactor `execute` to pre-fetch all files per source in one batch, then classify using a lookup map instead of remote calls.

**Remove** `classifyEntry`'s remote call (or remove `classifyEntry` entirely and inline the logic).

**New flow in `execute`:**
```
1. Group lock entries by SourceName
2. For each source group:
   a. Resolve url + branch from eff.Sources
   b. Call deps.FetchRemoteContent url branch [all RemotePaths in group]  ← single clone
   c. Build map: remotePath -> content
3. For each entry, classify using the map:
   - path in map with matching hash → Current
   - path in map with different hash → Drifted
   - path not in map (not checked out) → Missing
   - source not in eff.Sources → Skipped
   - path blocked → Blocked
```

Sources with no URL or unreachable repos produce `Missing`/`Skipped` for all their entries (same as today, just one error per source instead of one per file).

### 4. Callers that use the old single-path signature — wrap in `[path]`

These callers are unchanged in intent but must compile with the new list signature:

| File | Current call | Updated call |
|---|---|---|
| `Sync.fs` (manifest fetch) | `FetchRemoteContent url branch ".eru/manifest.json"` | `FetchRemoteContent url branch [".eru/manifest.json"]` |
| `KnowledgeSyncService.fs` (manifest fetch) | same | same |
| `Add.fs` (single file pull) | `FetchRemoteContent url branch remotePath` | `FetchRemoteContent url branch [remotePath]` |

### 5. `src/Eru.Mcp/KnowledgeSyncService.fs` — also batch collection files
Same problem exists here: one clone per collection file entry. Apply the same grouping pattern:
- Group `freshEff.Collections` by Source
- For each source, call `FetchRemoteContent url branch [all RemotePaths]` once
- Distribute results to individual cache writes

### 6. Tests — `tests/Eru.Tests/*.fs`
All test stubs for `FetchRemoteContent` take `_ _ _` (ignore all args) and return a fixed value. Updating the signature from `string` to `string list` for the third parameter requires no stub logic change — just the type annotation if any are explicit. Verify all test files compile.

Files to check: `AddTests.fs`, `SyncTests.fs`, `CollectionTests.fs`, `ManifestTests.fs`, `SourceTests.fs`, `DisconnectTests.fs`, `RemoveTests.fs`, `InitTests.fs`, `SearchTests.fs`.

## Verification
1. `dotnet build` — no compile errors across all projects
2. `dotnet test` — all existing tests pass
3. Manual: run `eru sync` on a project with multiple files from the same source; observe only one git clone per source in verbose/debug output (`--debug` flag)
4. Confirm `eru sync` still correctly updates drifted files and skips current ones
