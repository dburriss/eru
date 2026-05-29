# Plan: `ck` Search Backend for `search_knowledge`

## Context

`ck` ("seek") is a semantic + keyword code-search CLI that uses local embedding models to find code by meaning rather than just tokens. When installed it offers richer results than the inverted-index (`IndexedSearch`) backend. This plan:

1. Adds a `CkIndexService` that builds the `ck` vector index for managed directories at MCP server start — enabling semantic search immediately and on every restart.
2. Adds a `CkSearch` backend that uses `ck --hybrid` (semantic + keyword) per candidate file, falling back gracefully to `IndexedSearch` when `ck` is absent.

---

## Approach

**Indexing at startup** — `CkIndexService` (a `BackgroundService`, same pattern as `CollectionCacheService`) runs `ck --index <dir>` on startup and on each `McpRefreshIntervalMinutes` tick for two roots:
- `deps.GetCwd()` — the project root; covers lock-file entries, local `knowledge/` dirs, and all project files
- `Paths.collectionCachePath()` — the collection cache, which lives outside the project tree

**Search** — `CkAdapter.searchFile` runs `ck --hybrid -n --no-filename "query" "absPath"` per candidate file. Hybrid mode combines keyword + semantic results and uses the index built at startup. On any error (ck absent, index not built yet, non-zero exit) the function returns `[]` so the candidate is silently skipped.

**Backend selection** — extends the existing `ERU_SEARCH_BACKEND` env-var switch to a four-way dispatch:

| Value | Backend |
|-------|---------|
| `simple` | `SimpleScan.search` |
| `indexed` | `IndexedSearch.search` |
| `ck` | `CkSearch.search` (explicit) |
| _(unset / other)_ | `CkSearch.search` if `ck` on PATH, else `IndexedSearch.search` |

---

## Files to Create / Modify

### 1. `src/Eru.Adapters/CkAdapter.fs` — new file

`SimpleExec` is already a dep in `Eru.Adapters.fsproj` — no new packages needed.

```fsharp
namespace Eru.Adapters

open System
open SimpleExec

module CkAdapter =

    let isAvailable () =
        try
            Command.ReadAsync("ck", "--version").Result |> ignore
            true
        with _ -> false

    // Builds (or updates) the ck vector index for a directory.
    let indexDir (dir: string) =
        try Command.ReadAsync("ck", $"--index \"{dir}\"").Result |> ignore
        with _ -> ()

    // Hybrid keyword + semantic search on a single file; returns matching line texts.
    let searchFile (termList: string list) (absPath: string) : string list =
        try
            let query = (termList |> String.concat " ").Replace("\"", "")
            let struct (stdout, _) =
                Command.ReadAsync("ck", $"--hybrid -n --no-filename \"{query}\" \"{absPath}\"").Result
            stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            |> Array.choose (fun line ->
                let parts = line.Split(':', 2)
                if parts.Length = 2 then Some (parts.[1].Trim())
                else None)
            |> Array.toList
        with _ -> []
```

### 2. `src/Eru.Adapters/Eru.Adapters.fsproj`

Add after `SearchIndexAdapter.fs`:
```xml
<Compile Include="CkAdapter.fs" />
```

### 3. `src/Eru.Mcp/CkIndexService.fs` — new file

Mirrors `CollectionCacheService` exactly: runs `ck --index` at startup, then repeats on `McpRefreshIntervalMinutes` so newly-synced files are indexed after each collection refresh cycle.

```fsharp
namespace Eru.Mcp

open System
open System.IO
open System.Threading
open Eru.Adapters
open Microsoft.Extensions.Hosting

type CkIndexService(deps: Deps, eff: EffectiveConfig) =
    inherit BackgroundService()

    let indexAll () =
        if CkAdapter.isAvailable () then
            let cwd       = deps.GetCwd()
            let cacheRoot = Paths.collectionCachePath ()
            // CWD covers lock entries, local knowledge dirs, and project files.
            // Collection cache lives outside the project tree so indexed separately.
            for dir in [ cwd; cacheRoot ] do
                if Directory.Exists dir then
                    CkAdapter.indexDir dir

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            indexAll ()
            use timer = new PeriodicTimer(TimeSpan.FromMinutes(float eff.McpRefreshIntervalMinutes))
            while! timer.WaitForNextTickAsync(ct) do
                indexAll ()
        }
```

### 4. `src/Eru.Mcp/CkSearch.fs` — new file

```fsharp
module Eru.Mcp.CkSearch

open Eru.Adapters

let isAvailable () = CkAdapter.isAvailable ()

let search : SearchFn =
    fun termList candidates ->
        if termList = [] then
            candidates |> List.map (fun f -> f, [])
        else
            candidates |> List.choose (fun f ->
                let pathLower = f.RelPath.ToLowerInvariant()
                let pathHits  = termList |> List.exists pathLower.Contains
                let excerpts  = CkAdapter.searchFile termList f.AbsPath
                if pathHits || not excerpts.IsEmpty then Some (f, excerpts)
                else None)
```

### 5. `src/Eru.Mcp/Eru.Mcp.fsproj`

Add both new files in compile order — `CkSearch` after `IndexedSearch`, `CkIndexService` before `CollectionCacheService`:

```xml
<Compile Include="SearchTypes.fs" />
<Compile Include="SimpleScan.fs" />
<Compile Include="IndexedSearch.fs" />
<Compile Include="CkSearch.fs" />
<Compile Include="CkIndexService.fs" />
<Compile Include="CollectionCacheService.fs" />
<Compile Include="McpTools.fs" />
<Compile Include="McpServer.fs" />
```

### 6. `src/Eru.Mcp/McpTools.fs` — update backend selection only

```fsharp
let backend : SearchFn =
    match Environment.GetEnvironmentVariable "ERU_SEARCH_BACKEND" with
    | "simple"  -> SimpleScan.search
    | "indexed" -> IndexedSearch.search
    | "ck"      -> CkSearch.search
    | _         ->
        if CkSearch.isAvailable () then CkSearch.search
        else IndexedSearch.search
```

### 7. `src/Eru.Mcp/McpServer.fs` — register `CkIndexService`

Add one line alongside the existing `CollectionCacheService` registration:

```fsharp
.AddHostedService<CollectionCacheService>()
.AddHostedService<CkIndexService>()
```

---

## Key design notes

- **No new packages** — `SimpleExec` is already in `Eru.Adapters`.
- **Periodic re-indexing** — `CkIndexService` runs `ck --index` at startup then repeats on `McpRefreshIntervalMinutes` (same timer as `CollectionCacheService`). `ck --index` is incremental so subsequent runs are cheap and pick up any files added by the preceding collection sync.
- **Graceful degradation** — `CkAdapter.isAvailable` and `searchFile` both swallow all exceptions. If `ck` is absent the default backend silently falls through to `IndexedSearch`.
- **Path quoting** — `absPath` and `dir` are double-quoted in the args string to handle spaces.
- **Query sanitisation** — double quotes stripped from joined query to avoid breaking the args string.

---

## Verification

```bash
# Build
dotnet build

# Tests
dotnet test

# Start MCP server — CkIndexService will run ck --index on startup if ck is installed
dotnet run --project src/Eru -- mcp

# Confirm index was built (ck stores its index in a .ck/ dir inside the indexed directory)
ls ~/.cache/eru/collections/.ck/

# Force specific backends
ERU_SEARCH_BACKEND=ck      dotnet run --project src/Eru -- mcp
ERU_SEARCH_BACKEND=indexed dotnet run --project src/Eru -- mcp
ERU_SEARCH_BACKEND=simple  dotnet run --project src/Eru -- mcp

# With ck absent — confirm default falls back to IndexedSearch (no error, just normal results)
```
