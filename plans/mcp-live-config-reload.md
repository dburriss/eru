---
status: done
---
# Plan: Live config reload for MCP search and collection cache

## Context

The MCP server reads config once at startup and injects a frozen `EffectiveConfig` singleton into both `CollectionCacheService` and `KnowledgeTools`. This means:

- Sources added while the server is running are invisible to both the periodic cache refresh and `search_knowledge` / `read_artifact`.
- The periodic timer in `CollectionCacheService` only re-fetches file content for the collection items that were known at startup — it never re-reads config or re-fetches manifests, so remote manifest changes are also missed.

The fix is to make both components re-read config from disk on each use, and make `CollectionCacheService` also re-fetch remote manifests on each cycle.

---

## Changes

### 1. `src/Eru.Mcp/CollectionCacheService.fs`

Add a `buildEff ()` private binding that rebuilds the effective config from scratch:
- Re-reads global + local config via `deps.ReadGlobalConfig()` / `deps.ReadLocalConfig()`
- Calls `Config.merge`, falling back to the startup `effectiveCfg` on error
- Fetches `.eru/manifest.json` from every source URL (same pattern as `Sync.fs` lines 57–65) and calls `deps.CacheSourceManifest`
- Calls `Config.withManifests deps.ReadCachedManifest`

Change `syncAll ()` to call `buildEff ()` first, then use the returned fresh `eff` for the `.Collections` and `.Sources` lookups instead of `effectiveCfg`.

Keep using `effectiveCfg.McpRefreshIntervalMinutes` for the `PeriodicTimer` (set at startup, not worth reloading on every tick).

## Key reuse

- `Config.withManifests` — `src/Eru.Domain/Config.fs:234` — already exists, used by `Sync.fs`
- Remote manifest fetch pattern — `src/Eru.Domain/Sync.fs:57–65` — copied verbatim into `buildEff`
- `deps.CacheSourceManifest`, `deps.ReadCachedManifest`, `deps.ReadGlobalConfig`, `deps.ReadLocalConfig` — all on `Deps` record, already wired in `AdapterDeps.fs`

---

## Note: `McpTools` not changed

`search_knowledge` and `read_artifact` continue to use the `eff` singleton injected at startup. The timer-driven `CollectionCacheService` is the intended refresh mechanism. A future `refresh_knowledge` MCP tool (see `mcp-refresh-tool.md`) will allow on-demand triggering of the same logic.

---

## Verification

1. `dotnet build` — zero errors/warnings
2. Start MCP server (`eru mcp`)
3. Add a new source via CLI while server is running (`eru source add <url>`)
4. Wait for next refresh cycle (or lower `McpRefreshIntervalMinutes` in config for testing)
5. Call `search_knowledge` — files from the new source's manifest should appear
