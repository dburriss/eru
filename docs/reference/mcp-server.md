---
title: MCP server reference
type: reference
tags: [mcp, resources, tools]
---

# MCP server reference

`eru mcp` starts a Model Context Protocol (MCP) stdio server exposing eru's knowledge search and retrieval to AI
agents. See [set up the MCP server](../how-to/set-up-the-mcp-server.md) for configuration and client wiring.

## Knowledge caching

On startup, `eru mcp` runs a full index population: it fetches manifests for all configured sources, rebuilds
`~/.cache/eru/sources/<name>/index.json` with tags and descriptions from the manifest and file frontmatter, and
caches the content of collection and lock file entries under `~/.cache/eru/sources/<name>/files/`. This makes
`search_knowledge` and `read_artifact` fast — no live git fetch is needed for cached files. The cache refreshes
automatically at the interval set by `mcp.refreshIntervalMinutes`.

## Available resources

MCP Resources are URI-addressable data sources that clients can enumerate and read directly. They appear in
`resources/list` or `resources/templates/list` depending on whether the URI is fixed or parameterised.

### `eru://sources`

Lists all configured knowledge sources.

```
sourceName — https://github.com/org/repo [main] (12 docs)
```

### `eru://sources/{name}`

Config details and known collection docs for a specific source. Substitute `{name}` with the source name.

```
# sourceName
URL: https://github.com/org/repo
Branch: main
  docs/guide.md [tags: guide,dotnet] — Getting started guide
  docs/ref.md — API reference
```

### `eru://installed`

All locally pulled artifacts from the lock file (`.eru/eru.lock`), grouped by source. Files no longer present on
disk are marked `[missing]`.

```
## sourceName
  docs/guide.md → knowledge/guide.md
  docs/ref.md → knowledge/ref.md [missing]
```

## Available tools

### `search_knowledge`

Ranked search, best match first, across:

1. **Source index entries** — all files advertised by configured sources, with tags and descriptions from the
   index (populated by the background sync). Files with no locally cached content are matched on path, tags and
   description only and are listed after the ranked files.
2. **Lock file entries** (`.eru/eru.lock`) — files pulled into the current repo but not covered by any source
   index.
3. **Local knowledge directories** — a `knowledge/` directory (any letter case) in the current working directory.

| Parameter | Description |
|---|---|
| `query` | Space-separated search terms. Files are ranked by relevance: rarer terms count more, matches in the path, headings and front matter count extra, and word forms match (`mock` finds `mocked`). Leave empty to list artifacts in path order. |
| `tags` | Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering. |
| `limit` | Optional. Maximum number of files returned (default 15). |
| `mode` | Optional. `lex` (keywords, the default), `sem` (meaning) or `hybrid` (both). Overrides `search.mode`. `sem` and `hybrid` need the embedding model; if it is not installed the call runs `lex` and says so in `warnings`. |

Each result has the source type (`[collection]`, `[lock]`, `[local]`), path, tags, description and up to three
excerpt lines from the best matches, plus a relevance `score`. Scores are only comparable within one result.

The structured result is `{ hits, truncated, warnings }`:

- `hits` — the files, best first. Each hit has `path`, `source`, `sourceName`, `tags`, `description`, `excerpts` and `score` (0 for metadata-only matches).
- `truncated` — `true` when more files matched than `limit` allowed. Raise `limit` or narrow the query.
- `warnings` — why the result may differ from what was asked, for example a semantic mode that fell back to `lex`.

Files matching `search.ignorePatterns` (by default `index.md` and `log.md`) are never searched, so a generated
index does not duplicate the README it lists. See [search settings](config-file.md) and
[search modes](../how-to/set-up-the-mcp-server.md#choose-a-search-mode).

### `read_artifact`

Read the full content of a knowledge artifact. Resolution order:

1. Local file path (relative to CWD or absolute)
2. Lock file `LocalPath` match
3. Source index hit by `sourceName/remotePath` or bare remote path: local file, then cache (`~/.cache/eru/sources/<name>/files/`), then live fetch
4. `sourceName:remotePath` or a path short hash (3-8 hex chars): same local, cache, live-fetch order (shared with `eru print`)

| Parameter | Description |
|---|---|
| `path` | A local file path, `sourceName:remotePath`, a path short hash, or a path from `search_knowledge` results |
