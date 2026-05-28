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

## Collection caching

On startup, `eru mcp` pre-fetches every file referenced in your collections from their upstream sources and stores them in a local cache. This makes `search_knowledge` and `read_artifact` fast — no live git fetch is needed for cached files. The cache refreshes automatically at the interval set by `mcpRefreshIntervalMinutes`.

## Available tools

### `search_knowledge`

Full-text search across:

1. **Cached collection files** — files pre-fetched from configured collections
2. **Lock file entries** (`.eru/eru.lock`) — files pulled into the current repo
3. **Local knowledge directories** — `knowledge/` and `KNOWLEDGE/` in the current working directory

| Parameter | Description |
|---|---|
| `query` | Space-separated search terms (OR semantics). Leave empty to list all artifacts. |
| `tags` | Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering. |

Results include the source type (`[collection]`, `[lock]`, `[local]`), path, tags, and a content excerpt from the first matching line.

### `read_artifact`

Read the full content of a knowledge artifact. Resolution order:

1. Local file path (relative to CWD or absolute)
2. Lock file `LocalPath` match
3. Collection cache hit
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
