---
status: done
---
# Plan: filter search to specific sources

## Context

`search_knowledge` (MCP) and `eru search` (CLI) can filter by tag but not by source. With several sources attached, an agent or user can't scope a query to one of them. Every `CandidateFile` already carries `SourceName : string option` (`src/Eru.Search/SearchTypes.fs`). It is `Some name` for cache and lock files and `None` for local `knowledge/` files. The change mirrors how tags are filtered.

## Changes

### 1. Core: `src/Eru.Search/RankedSearch.fs`
- Add `Sources : string list` to `Request`. Empty means no filtering.
- Add `parseSources` (comma-split, trimmed, lower-cased; reuse the logic of `parseTags`) and `hasSource`. Matching is case-insensitive and OR across names. Tags remain AND.
- Apply the filter **before** ranking so `limit` and `Truncated` stay correct:
  - `readableCandidates` (also narrows the set handed to Ferret).
  - The empty-query listing branch in `execute`.
  - The `metadataHits` branch in `execute` (`CandidateBuilder.buildMetadataOnly`).
- Local `knowledge/` files (`SourceName = None`) match the reserved name `local`.
- Validate names against `eff.Sources` plus `local`. An unknown name returns `Error` listing the valid names, so a typo isn't mistaken for "no results".

### 2. MCP: `src/Eru.Mcp/McpTools.fs`
- Add an optional `sources` parameter to `search_knowledge`, comma-separated like `tags`, with `[<Optional; DefaultParameterValue(null: string)>]`. The description points at source names from `eru source list`.
- Pass it into `RankedSearch.Request`.

### 3. CLI: `src/Eru.Cli/Args.fs`, `src/Eru.Cli/SearchCli.fs`
- Add `--source`/`-s` to `SearchArgs`, repeatable like `--tag`.
- Thread it through `LocalSearch.Query` and the ranked `Request` (around `SearchCli.fs:144`).
- Check the `-s` short flag doesn't clash with an existing `SearchArgs` flag.

### 4. Docs, changelog, plan
- `docs/reference/mcp-server.md`, `skills/eru/references/mcp.md`, `skills/eru/SKILL.md`: document `sources` and `--source`.
- `CHANGELOG.md`: add an Unreleased entry.
- **Put this plan in the repo's `plans/` folder** as `plans/search-source-filter.md`. Use the repo's plan format (front matter `status:` then `# Plan: ...`). Write it first with `status: todo`, and set `status: done` once the work is built.

## Decisions (confirmed)
1. Local files are selectable via the reserved name `local`.
2. Unknown source names return an error listing the valid names.
3. There is no separate collection filter. Cached collection files match by their source name.

## Verification
- Add unit tests next to the existing `RankedSearch` tests:
  - One source returns only that source's files.
  - Several sources are OR-ed.
  - `local` selects `knowledge/` files.
  - An unknown source returns an error naming the valid sources.
  - An empty query combined with a source filter lists only that source.
  - `Truncated` and `limit` are correct under filtering.
- Run `dotnet build` and `dotnet test`.
- Manual: `eru search <term> --source <name>` against two attached sources. Then call `search_knowledge` through `eru mcp` with `sources` set and confirm the results match.
