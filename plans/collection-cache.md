---
status: todo
---

# Plan: Collection file cache

## Context

The MCP server needs to do full-text content search across knowledge artifacts. Cloning entire source repositories is too heavy; fetching individual files on every search call is too slow. The collection cache materialises only the files listed in `GlobalConfig.Collections` to a local directory, giving the MCP server fast, offline-capable content to search against.

Lock file entries are already on disk at their `LocalPath` — no caching needed for those.

---

## Cache location

`~/.cache/eru/collections/<sourceName>/<remotePath>`

`remotePath` is used as-is (forward slashes become path separators). Example:

```
~/.cache/eru/collections/shared-knowledge/docs/adr-template.md
~/.cache/eru/collections/shared-knowledge/observability/logging-guide.md
```

### `src/Eru.Adapters/Paths.fs`

Add alongside `globalConfigPath`:

```fsharp
let collectionCachePath () =
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
        let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
        IO.Path.Combine(localAppData, "eru", "collections")
    else
        let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
        let cacheHome =
            if xdgCache <> null && xdgCache <> "" then xdgCache
            else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
        IO.Path.Combine(cacheHome, "eru", "collections")
```

---

## Config change

Add `McpRefreshIntervalMinutes` to `GlobalDefaults` in `src/Eru.Domain/Config.fs`:

```fsharp
type GlobalDefaults = {
    Branch                    : string option
    CommitOnPull              : bool option
    McpRefreshIntervalMinutes : int option    // NEW — default 60
}
```

`System.Text.Json` + the existing `OptionConverter` handles missing fields as `None` — no serialisation changes needed.

Expose the resolved value on `EffectiveConfig`:

```fsharp
type EffectiveConfig = {
    Sources                   : SourceConfig list
    CommitOnPull              : bool
    StateFile                 : string
    McpRefreshIntervalMinutes : int           // NEW — resolved from global defaults, default 60
}
```

Update `Config.merge` to read and default the new field:

```fsharp
McpRefreshIntervalMinutes =
    globalCfg
    |> Option.bind (fun g -> g.Defaults)
    |> Option.bind (fun d -> d.McpRefreshIntervalMinutes)
    |> Option.defaultValue 60
```

---

## Cache population

Reuses the existing `FetchRemoteContent` dep — no new adapter needed.

For each `CollectionFileRef` in `EffectiveConfig`-resolved collections:

1. Resolve the matching `SourceConfig` from `EffectiveConfig.Sources` by `CollectionFileRef.Source`
2. Resolve the branch to use (in order of precedence):
   - `SourceConfig.Branch` if `Some`
   - `GlobalConfig.Defaults |> Option.bind (fun d -> d.Branch)` if `Some`
   - `"HEAD"` — tells `GitAdapter` to omit `--branch` and let the remote decide
3. Call `deps.FetchRemoteContent sourceConfig.Url branch collectionFileRef.RemotePath`
4. Write content to `{collectionCachePath()}/{sourceName}/{remotePath}` (create parent dirs as needed)

On fetch error: log to stderr, leave any existing cached file in place (keep stale).

---

## Refresh behaviour

- **Single interval** for all sources — `EffectiveConfig.McpRefreshIntervalMinutes`
- **Proactive timed refresh** — a background `IHostedService` fires a `PeriodicTimer` on the configured interval; it re-fetches all collection files regardless of access patterns
- **Serve stale** — tool calls always read from disk; a refresh in progress never blocks a tool call
- **Keep stale on failure** — if a source is unreachable during a refresh, the existing cached file is untouched and the server carries on; errors are logged to stderr only

### `src/Eru.Mcp/CollectionCacheService.fs`

`GlobalConfig` is still required alongside `EffectiveConfig` because `.Collections` lives there and
the global-default branch fallback is read from `GlobalConfig.Defaults.Branch`.
`EffectiveConfig` provides the fully-merged `.Sources` list and the resolved `.McpRefreshIntervalMinutes`.

```fsharp
type CollectionCacheService(deps: Deps, globalCfg: GlobalConfig, effectiveCfg: EffectiveConfig) =
    inherit BackgroundService()

    let cacheRoot = Paths.collectionCachePath()

    let globalDefaultBranch =
        globalCfg.Defaults
        |> Option.bind (fun d -> d.Branch)
        |> Option.defaultValue "HEAD"   // "HEAD" → GitAdapter omits --branch, remote decides

    let syncAll () =
        globalCfg.Collections
        |> List.iter (fun col ->
            col.Files
            |> List.iter (fun f ->
                match effectiveCfg.Sources |> List.tryFind (fun s -> s.Name = f.Source) with
                | None -> eprintfn "eru: collection cache: unknown source '%s'" f.Source
                | Some src ->
                    let branch =
                        src.Branch |> Option.defaultValue globalDefaultBranch
                    match src.Url with
                    | None -> eprintfn "eru: collection cache: source '%s' has no URL configured" f.Source
                    | Some url ->
                    match deps.FetchRemoteContent url branch f.RemotePath with
                    | Error e -> eprintfn "eru: collection cache: fetch failed for %s/%s: %s" f.Source f.RemotePath e
                    | Ok content ->
                        let dest = IO.Path.Combine(cacheRoot, f.Source, f.RemotePath.Replace('/', IO.Path.DirectorySeparatorChar))
                        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName dest) |> ignore
                        IO.File.WriteAllText(dest, content)))

    override _.ExecuteAsync(ct) =
        task {
            syncAll ()   // populate on startup
            use timer = new PeriodicTimer(TimeSpan.FromMinutes(float effectiveCfg.McpRefreshIntervalMinutes))
            while! timer.WaitForNextTickAsync(ct) do
                syncAll ()
        }
```

---

## Files to create / modify

| File | Change |
|------|--------|
| `src/Eru.Domain/Config.fs` | Add `McpRefreshIntervalMinutes` to `GlobalDefaults` and `EffectiveConfig`; update `Config.merge` |
| `src/Eru.Adapters/Paths.fs` | Add `collectionCachePath()` |
| `src/Eru.Mcp/CollectionCacheService.fs` | NEW — `BackgroundService` implementation |

---

## Verification

```bash
dotnet build
dotnet test   # Config.merge tests must still pass; add a test for McpRefreshIntervalMinutes defaulting to 60

# Start the MCP server and confirm cache is populated:
eru mcp
ls ~/.cache/eru/collections/

# Confirm stale-on-failure: take a source offline and wait for a refresh tick — cached files should remain
```
