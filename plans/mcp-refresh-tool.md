# Plan: `refresh_knowledge` MCP tool

## Context

`CollectionCacheService` now rebuilds config and syncs the collection cache on a timer. However, users have no way to trigger an immediate refresh without waiting for the next tick or restarting the MCP server. A `refresh_knowledge` tool would allow an AI agent (or user) to request an on-demand sync.

Additionally, since `KnowledgeTools` holds a frozen `eff` from startup, metadata lookups (tags, descriptions, source list for `sourceName:remotePath`) are stale until restart — even after the timer has refreshed the on-disk cache. Wiring a shared mutable config through a new service fixes this at the same time.

---

## Design

Extract the sync logic out of `CollectionCacheService` into a new singleton `KnowledgeSyncService`. This service owns the authoritative live `EffectiveConfig` and exposes a `Sync()` method that both the timer and the new MCP tool call.

```
KnowledgeSyncService  (singleton, owns live eff + sync logic)
    ├── called by CollectionCacheService  (timer wrapper)
    └── called by KnowledgeTools.Refresh  (on-demand MCP tool)

KnowledgeTools.Search / Read read eff from KnowledgeSyncService
```

---

## Changes

### 1. New `src/Eru.Mcp/KnowledgeSyncService.fs`

New plain class (not a hosted service) registered as a singleton:

```fsharp
type KnowledgeSyncService(deps: Deps, startupEff: EffectiveConfig) =
    let mutable currentEff = startupEff

    member _.CurrentEff = currentEff

    member _.Sync() =
        // Re-read config
        // Re-fetch .eru/manifest.json from each source (same as CollectionCacheService.buildEff)
        // Call Config.withManifests
        // Sync collection file content to cache (same as CollectionCacheService.syncAll body)
        // Update currentEff <- freshEff
        // Return summary: sources refreshed, files cached, errors
```

The `buildEff` and sync body currently in `CollectionCacheService` move here verbatim.

### 2. `src/Eru.Mcp/CollectionCacheService.fs`

Simplify to a thin timer wrapper: inject `KnowledgeSyncService`, call `service.Sync()` on each tick. Remove `buildEff` and `syncAll`.

### 3. `src/Eru.Mcp/McpTools.fs`

- Inject `KnowledgeSyncService` alongside `deps` and `eff`
- In `Search` and `Read`, read `service.CurrentEff` instead of the frozen `eff` — gives live metadata after any refresh (timer or on-demand)
- Add new `[<McpServerTool(Name = "refresh_knowledge")>]` method that calls `service.Sync()` and returns the summary

### 4. `src/Eru.Mcp/McpServer.fs`

Register `KnowledgeSyncService` as a singleton before `CollectionCacheService` and `KnowledgeTools`:

```fsharp
builder.Services
    .AddSingleton<KnowledgeSyncService>()
    ...
```

---

## Key reuse

- `buildEff` logic — `src/Eru.Mcp/CollectionCacheService.fs` — move verbatim into `KnowledgeSyncService.Sync`
- `Config.withManifests` — `src/Eru.Domain/Config.fs:234`
- Remote manifest fetch pattern — `src/Eru.Domain/Sync.fs:57–65`

---

## Verification

1. `dotnet build` — zero errors/warnings
2. Start MCP server, add a new source via CLI
3. Call `refresh_knowledge` — should return a summary listing sources and files synced
4. Call `search_knowledge` — new source's files appear with correct metadata
5. Call `read_artifact newSource:path/to/file` — resolves correctly (live eff now has new source)
6. Confirm timer still works: lower `McpRefreshIntervalMinutes`, wait, verify cache updates without calling the tool
