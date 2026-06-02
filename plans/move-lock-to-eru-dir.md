# Plan: Move `eru.lock` to `.eru/eru.lock`

## Context

`eru.lock` currently resolves to the CWD root (e.g., `./eru.lock`) from the Domain layer, cluttering consumer repos with a new top-level file. The goal is to move it inside the existing `.eru/` directory (alongside `config.json` and `manifest.json`), so all eru-managed state lives in one place.

The path helper `Paths.lockFilePath` already builds `.eru/<stateFile>`, but this is only used in the MCP layer. The Domain layer (`Add`, `Remove`, `Sync`, `Disconnect`, `Search`) passes `eff.StateFile` (just the filename `"eru.lock"`) directly to `deps.ReadLockEntries` / `deps.WriteLockEntries`, which in `AdapterDeps.create` are bound directly to `LockFileAdapter.read/write` — so they treat the filename as a relative path from CWD, resolving to `./eru.lock` instead of `.eru/eru.lock`.

## Root Cause

`src/Eru.Adapters/AdapterDeps.fs` (lines 47–48):
```fsharp
ReadLockEntries  = LockFileAdapter.read   // receives "eru.lock" → reads ./eru.lock
WriteLockEntries = LockFileAdapter.write  // same
```

`src/Eru.Mcp/McpTools.fs` and `McpResources.fs` manually call `Paths.lockFilePath` before invoking `ReadLockEntries`, so they already land in `.eru/` — but inconsistently with the Domain layer.

## Changes

### 1. `src/Eru.Adapters/AdapterDeps.fs` — fix path resolution + add migration

Wrap the bindings so the adapter layer owns path construction, and auto-migrate existing root-level lock files:

```fsharp
ReadLockEntries = fun stateFile ->
    let newPath = Paths.lockFilePath cwd (Some stateFile)
    let oldPath = IO.Path.Combine(cwd, stateFile)
    if not (IO.File.Exists newPath) && IO.File.Exists oldPath then
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName newPath) |> ignore
        IO.File.Move(oldPath, newPath)
    LockFileAdapter.read newPath
WriteLockEntries = fun stateFile entries ->
    LockFileAdapter.write (Paths.lockFilePath cwd (Some stateFile)) entries
```

The migration runs once transparently: if `.eru/eru.lock` is absent but `./eru.lock` exists, it moves the file.

### 2. `src/Eru.Mcp/McpTools.fs` — remove redundant `lockFilePath` calls (3 sites)

Lines 65–67, 218–220: remove the `let lockPath = Paths.lockFilePath ...` construction and pass `eff.StateFile` directly:
```fsharp
// Before
let lockPath = Paths.lockFilePath (deps.GetCwd()) (Some eff.StateFile)
match deps.ReadLockEntries lockPath with

// After
match deps.ReadLockEntries eff.StateFile with
```

### 3. `src/Eru.Mcp/McpResources.fs` — same fix (1 site, lines 53–54)

### 4. Docs: bare `eru.lock` → `.eru/eru.lock`

- `docs/concepts.md` lines 16, 36, 68, 152, 158 — update bare `eru.lock` references
- `docs/state-and-search.md` line 138 — same
- `src/Eru.Domain/Config.fs` line 86 comment — update `// set if the file is in eru.lock` → `// set if the file is in .eru/eru.lock`

### 5. `todo.md` — mark item done

Line 14: change `- [ ]` to `- [x]`

### 6. This repo's own lock file

Move the committed `eru.lock` (repo root) into `.eru/` via git:
```bash
mkdir -p .eru
git mv eru.lock .eru/eru.lock
```

The migration logic in step 1 handles this transparently at runtime for existing consumer repos.

## Files Modified

| File | Change |
|---|---|
| `src/Eru.Adapters/AdapterDeps.fs` | Wrap ReadLockEntries/WriteLockEntries with lockFilePath; add migration |
| `src/Eru.Mcp/McpTools.fs` | Remove redundant lockFilePath construction (3 sites) |
| `src/Eru.Mcp/McpResources.fs` | Remove redundant lockFilePath construction (1 site) |
| `docs/concepts.md` | Fix bare `eru.lock` references |
| `docs/state-and-search.md` | Fix bare `eru.lock` reference |
| `src/Eru.Domain/Config.fs` | Fix comment line 86 |
| `todo.md` | Mark item done |
| `eru.lock` → `.eru/eru.lock` | `git mv` |

## Verification

1. `dotnet build` — no compile errors
2. `dotnet test` — all tests pass (ConfigTests `StateFile` tests still pass since `StateFile` value remains `"eru.lock"`)
3. Manual: in a test repo with an existing root-level `eru.lock`, run `eru add <source>/<file>` → file migrates to `.eru/eru.lock` automatically, old file gone
4. Manual: `eru sync` writes to `.eru/eru.lock`, not root
5. MCP: `search_knowledge` tool and `installed` resource return correct results
