# eru MCP server

`eru mcp` starts a Model Context Protocol (MCP) stdio server, exposing eru's knowledge search and retrieval to AI agents (Claude, Copilot, Cursor, etc.).

## Starting the server

```bash
eru mcp
```

The server reads from stdin and writes to stdout using the MCP protocol. Wire it up in your editor or agent runtime as a stdio MCP server pointing to the `eru mcp` command.

## Configuration

The MCP server merges the same global (`~/.config/eru/config.json`) and local (`.eru/config.json`) configuration as the rest of eru. One additional field controls the collection cache refresh interval:

```json
{
  "mcpRefreshIntervalMinutes": 60
}
```

The default is 60 minutes. The server fetches all configured collection files into a local cache on startup and re-fetches on the configured interval.

## Knowledge caching

On startup, `eru mcp` runs a full index population: it fetches manifests for all configured sources, rebuilds `~/.cache/eru/sources/<name>/index.json` with tags and descriptions from the manifest and file frontmatter, and caches the content of collection and lock file entries under `~/.cache/eru/sources/<name>/files/`. This makes `search_knowledge` and `read_artifact` fast — no live git fetch is needed for cached files. The cache refreshes automatically at the interval set by `mcpRefreshIntervalMinutes`.

## Available resources

MCP Resources are URI-addressable data sources that clients can enumerate and read directly. They appear in `resources/list` or `resources/templates/list` depending on whether the URI is fixed or parameterised.

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

All locally pulled artifacts from the lock file (`.eru/eru.lock`), grouped by source. Files no longer present on disk are marked `[missing]`.

```
## sourceName
  docs/guide.md → knowledge/guide.md
  docs/ref.md → knowledge/ref.md [missing]
```

---

## Available tools

### `search_knowledge`

Full-text search across:

1. **Source index entries** — all files advertised by configured sources, with tags and descriptions from the index (populated by the background sync). Files with no locally cached content are still returned for metadata matches (path, tags, description).
2. **Lock file entries** (`.eru/eru.lock`) — files pulled into the current repo but not covered by any source index.
3. **Local knowledge directories** — `knowledge/` and `KNOWLEDGE/` in the current working directory.

| Parameter | Description |
|---|---|
| `query` | Space-separated search terms (OR semantics). Leave empty to list all artifacts. |
| `tags` | Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering. |

Results include the source type (`[collection]`, `[lock]`, `[local]`), path, tags, and a content excerpt from the first matching line. Tags are read from the source index (merged from manifest + frontmatter) — no live file reads during search.

### `read_artifact`

Read the full content of a knowledge artifact. Resolution order:

1. Local file path (relative to CWD or absolute)
2. Lock file `LocalPath` match
3. Source index cache hit (`~/.cache/eru/sources/<name>/files/`)
4. Live fetch via `sourceName:remotePath` reference

| Parameter | Description |
|---|---|
| `path` | A local file path, `sourceName:remotePath`, or a path from `search_knowledge` results |

## Example MCP client configuration

### Claude Desktop (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "eru": {
      "command": "eru",
      "args": ["mcp"]
    }
  }
}
```

### VS Code (`.vscode/mcp.json`)

```json
{
  "servers": {
    "eru": {
      "type": "stdio",
      "command": "eru",
      "args": ["mcp"]
    }
  }
}
```
