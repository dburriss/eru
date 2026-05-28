# Plan: File Block Patterns

## Context

Users fetch files from remote knowledge sources. Some files should never be pulled locally — executables (`.exe`, `.dll`, `.so`, etc.) are the primary concern. The feature needs to be configurable in both global and local eru config files, with local replacing global when specified, and a default block list that covers common executable types.

Linux binaries without extensions cannot be matched by glob alone. Rather than using a sentinel or an allowlist for known text files, this plan adds a `allowBinaries` flag that detects binary content by checking for null bytes in the fetched file content. A binary like `mybinary` is blocked; a text file like `Makefile` passes without any allowlist entry needed.

## Approach

### 1. Shared defaults constant (`src/Eru.Domain/Config.fs`)

Expose three public values at the top of the `Config` module — single source of truth used by both `Config.merge` (as fallback) and `Init.fs` (for the generated template):

```fsharp
let defaultBlockPatterns = ["*.exe"; "*.dll"; "*.so"; "*.dylib"; "*.bin"; "*.out"; "*.app"]
let defaultAllowPatterns : string list = []
let defaultAllowBinaries = false
```

### 2. Config types (`src/Eru.Domain/Config.fs`)

Add to `GlobalDefaults`:
- `BlockPatterns: string list option` — JSON `defaults.blockPatterns`
- `AllowPatterns: string list option` — JSON `defaults.allowPatterns`
- `AllowBinaries: bool option` — JSON `defaults.allowBinaries`

Add to `LocalSettings`:
- `BlockPatterns: string list option` — JSON `settings.blockPatterns`
- `AllowPatterns: string list option` — JSON `settings.allowPatterns`
- `AllowBinaries: bool option` — JSON `settings.allowBinaries`

Add to `EffectiveConfig`:
- `BlockPatterns: string list`
- `AllowPatterns: string list`
- `AllowBinaries: bool`

**Merge semantics** (each field merged independently; local replaces global when set):
- `BlockPatterns`: local `Some ps` → use `ps`; else global value; else `Config.defaultBlockPatterns`
- `AllowPatterns`: local `Some ps` → use `ps`; else global value; else `Config.defaultAllowPatterns`
- `AllowBinaries`: local `Some b` → use `b`; else global value; else `Config.defaultAllowBinaries`

### 3. Pattern matching (`src/Eru.Domain/Patterns.fs` — new file)

Implement a simple glob matcher (no new NuGet dependency). Supports:
- `*` — any chars except `/`
- `/**/` — zero or more path segments (e.g. `docs/**/*.md` matches `docs/file.md` and `docs/sub/file.md`)
- `**` elsewhere — any chars including `/`
- `?` — single char except `/`
- Literal chars otherwise; matching is case-insensitive

For patterns with no `/`, match against the filename only (e.g. `*.exe` matches `foo/bar.exe`).
For patterns with `/`, match against the full relative path.

```fsharp
module Patterns =
    let isBinaryContent (content: string) : bool          // null byte check
    let matchesGlob (pattern: string) (path: string) : bool
    let isPathBlocked (blockPatterns: string list) (allowPatterns: string list) (path: string) : bool
    let isBlocked (blockPatterns: string list) (allowPatterns: string list) (allowBinaries: bool) (path: string) (content: string) : bool
```

`isBlocked` logic:
1. If `allowPatterns` has a match → `false` (allow wins)
2. If `blockPatterns` has a match → `true`
3. If `allowBinaries = false` and `isBinaryContent content` → `true`
4. Otherwise → `false`

`isPathBlocked` is the path-only variant (no content needed); used as a fast pre-filter in Sync and for MCP search results.

### 4. Apply blocking in Add (`src/Eru.Domain/Add.fs`)

In `pullOne`, after `FetchRemoteContent` returns files, filter using both path and content:

```fsharp
let allowed, blocked = files |> List.partition (fun (path, content) ->
    not (Patterns.isBlocked blockPatterns allowPatterns allowBinaries path content))
for (path, _) in blocked do printfn "[blocked]  %s" path
```

Thread `eff.BlockPatterns`, `eff.AllowPatterns`, `eff.AllowBinaries` through `pullOne` and `pullMany`.

### 5. Apply blocking in Sync (`src/Eru.Domain/Sync.fs`)

Add `Blocked of LockEntry` to the private `EntryResult` DU.

In `classifyEntry`:
- Fast path before fetch: `isPathBlocked` on `entry.RemotePath` → `Blocked entry`
- After fetch: `isBlocked` with content → `Blocked entry` if matched

Include `nBlocked` in the summary line: `%d updated, %d current, %d missing, %d skipped, %d blocked.`

### 6. Init template (`src/Eru.Domain/Init.fs`)

**Local scaffold** (`.eru/config.json`): Show settings structure with null values so users see available fields.

**Global init** (`emptyGlobal`): Write `Defaults = Some { ... BlockPatterns = Some Config.defaultBlockPatterns; ... }` so the generated global config explicitly shows the active defaults. Users can modify or remove these values. Fell back defaults (`None`) always resolve to the same constants from `Config.defaultBlockPatterns`.

### 7. MCP server (`src/Eru.Mcp/McpTools.fs`)

`search_knowledge`: filter all three result sources (cached collection files, lock entries, local knowledge dirs) through `isPathBlocked` before reading content.

`read_artifact` case 4 (live `sourceName:remotePath` fetch): check `isBlocked` after fetch and return an error string if blocked.

`McpServer.fs` fallback `EffectiveConfig`: use `Config.defaultBlockPatterns` / `Config.defaultAllowPatterns` / `Config.defaultAllowBinaries`.

### 8. Tests (`tests/Eru.Tests/PatternsTests.fs` — new file)

- `isBinaryContent`: null byte → true, plain text → false
- `matchesGlob`: exact, `*`, `**` with zero and one+ intermediate dirs, `?`, path-scoped, no-match cases
- `isPathBlocked`: block wins, allow overrides, no match
- `isBlocked`: extension block, allow override, binary detection on/off, allow wins over binary

## Files modified

| File | Change |
|---|---|
| `src/Eru.Domain/Config.fs` | Shared defaults constants; new fields on `GlobalDefaults`, `LocalSettings`, `EffectiveConfig`; `Config.merge` |
| `src/Eru.Domain/Patterns.fs` | New — glob matcher, `isBinaryContent`, `isPathBlocked`, `isBlocked` |
| `src/Eru.Domain/Eru.Domain.fsproj` | Add `Patterns.fs` before `Add.fs` |
| `src/Eru.Domain/Add.fs` | Filter blocked files in `pullOne`; print `[blocked]` |
| `src/Eru.Domain/Sync.fs` | `Blocked` DU case; `classifyEntry` checks; summary count |
| `src/Eru.Domain/Init.fs` | Local scaffold shows settings fields; global `emptyGlobal` writes explicit defaults |
| `src/Eru.Mcp/McpTools.fs` | `search_knowledge` path filter; `read_artifact` live-fetch block check |
| `src/Eru.Mcp/McpServer.fs` | Fallback `EffectiveConfig` uses `Config.default*` constants |
| `tests/Eru.Tests/PatternsTests.fs` | New test file |
| `tests/Eru.Tests/Eru.Tests.fsproj` | Add `PatternsTests.fs` |
| `tests/Eru.Tests/ConfigTests.fs` | Updated `GlobalDefaults`/`LocalSettings` inline record constructions |
| `tests/Eru.Tests/InitTests.fs` | Updated assertion for `emptyGlobal.Defaults` |

## Verification

1. `dotnet build` — clean compile (0 errors)
2. `dotnet test` — 157 tests pass (25 new Patterns tests)
3. Manual smoke test:
   - Default config (nothing set) → `*.exe` blocked; binary content blocked; `.md` and text `Makefile` pass
   - `eru init --global` → global config shows `blockPatterns` with the default list
   - Set `"allowBinaries": true` in local config → binary files are no longer blocked
   - Set local `"blockPatterns": []` → local empty list replaces global, extensions no longer blocked
   - Set local `"allowPatterns": ["scripts/*.sh"]` → that path is allowed even if it matched a block
   - `read_artifact sourceName:binary` via MCP → returns error if content is binary and `allowBinaries=false`
