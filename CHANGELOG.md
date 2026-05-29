# Changelog

## [Unreleased]
### Fixed
- MCP resources now declare `text/plain` MIME type — previously the SDK defaulted to `application/octet-stream`, causing many MCP clients and inspectors to not display the `eru://sources`, `eru://sources/{name}`, and `eru://installed` resources

## [0.7.0] - 2026-05-29
### Added
- Serilog rolling-file logging for the MCP server — warnings and errors from background syncs are written to `~/.cache/eru/mcp-YYYYMMDD.log` (XDG-aware; `%LOCALAPPDATA%\eru\` on Windows); daily rotation, 7-day retention

### Changed
- `refresh_knowledge` MCP tool now returns immediately instead of blocking until all git fetches complete — the sync runs on a background thread and any errors are written to the MCP log file rather than returned inline
- `CollectionCacheService` timer ticks also go through the new background-sync path, freeing the timer thread from blocking on network I/O
- Concurrent sync calls are deduplicated — if a sync is already in progress (from the timer or a previous `refresh_knowledge` call), the tool returns `"A knowledge refresh is already in progress."` rather than starting a second parallel sync

### Fixed
- Git operations no longer prompt for credentials — `GIT_TERMINAL_PROMPT=0` and `GIT_ASKPASS=echo` are now set for all git invocations, preventing interactive auth prompts from blocking or corrupting the JSON-RPC stdio channel
- MCP server checks source accessibility at startup — runs `git ls-remote` against each configured source URL before the host starts and writes a warning to stderr if any source is unreachable (e.g. due to missing credentials), surfacing auth failures immediately rather than on the first hanging tool call

## [0.6.0] - 2026-05-29
### Added
- MCP Resources for browsing sources and installed artifacts — three new resources exposed by `eru mcp`:
  - `eru://sources` lists all configured knowledge sources with URL, branch, and collection doc count
  - `eru://sources/{name}` returns config details and collection docs for a specific source
  - `eru://installed` lists all locally pulled artifacts from the lock file, grouped by source; missing files are flagged `[missing]`

## [0.5.1] - 2026-05-29
### Fixed
- MCP server no longer writes log output to stdout — default console logging providers are cleared at startup so ASP.NET Core log lines no longer corrupt the JSON-RPC stdio stream

## [0.5.0] - 2026-05-29
### Added
- `refresh_knowledge` MCP tool — triggers an on-demand sync of the knowledge cache without waiting for the next timer tick; returns a summary of sources refreshed, files cached, and any errors

### Fixed
- MCP server now incorporates cached source manifests into the effective config at startup — manifest files were previously invisible to `search_knowledge` and `CollectionCacheService` even after `eru source add` had cached them on disk
- `CollectionCacheService` now rebuilds the effective config on every refresh cycle: re-reads global and local config, re-fetches `.eru/manifest.json` from each source, and re-applies manifests — sources added after the MCP server starts and remote manifest changes are picked up automatically on the next tick

## [0.4.0] - 2026-05-29
### Added
- YAML frontmatter support in knowledge files — `description` and `tags` fields in a `---` block are automatically picked up by the `search_knowledge` MCP tool; applies to cached collection files, lock-file entries, and local `knowledge/` directories; configured description takes precedence over frontmatter; tags are merged (union, deduplicated)
- `eru source remove <name>` — remove a named source from local config (or global with `--global`); supports `--dryrun`
- `eru collection remove <collection> -f <source:path>` — remove a file reference from a collection; if it was the last file the collection entry is also removed; supports `--global` and `--dryrun`
- Indexed word search for `search_knowledge` MCP tool — per-file inverted index stored at `~/.cache/eru/index/` invalidated by content hash; all matching lines returned as excerpts (OR semantics) instead of just the first hit
- `SimpleScan` baseline backend for `search_knowledge` — reads files directly without indexing; selectable via `ERU_SEARCH_BACKEND=simple`
- `ck` semantic + keyword search backend for `search_knowledge` — uses `ck --hybrid` per candidate file; selectable via `ERU_SEARCH_BACKEND=ck`; requires `ck` to be installed and on PATH
- `IndexService` — background service that pre-builds the search index at MCP server startup and on each refresh tick; activates for `ERU_SEARCH_BACKEND=indexed` (word index) and `ERU_SEARCH_BACKEND=ck` (vector index); indexing runs in parallel across files and directories

## [0.3.0] - 2026-05-28
### Added
- `eru manifest init` — create `.eru/manifest.json` in a knowledge-source repo
- `eru manifest add <path>` — add a file or glob entry to the manifest with optional tags and description
- `eru manifest remove <path>` — remove an entry from the manifest by exact path
- `eru manifest verify` — resolve all manifest entries against local files; exits 1 if any match nothing

## [0.2.0] - 2026-05-28
### Added
- `--dryrun` flag for `collection create`, `collection add`, and `source add` — previews what would be written without modifying any config file

## [0.1.0] - 2026-05-28
### Added
- Initial release of the `eru` CLI tool
