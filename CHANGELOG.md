# Changelog

## [Unreleased]

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
