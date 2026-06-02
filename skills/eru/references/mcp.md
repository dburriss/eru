# eru MCP setup

`eru mcp` starts a stdio MCP server exposing knowledge search, retrieval, and sync tools plus URI-addressable resources to AI agents.

## MCP client configuration

### Claude Code — project scope (`.mcp.json` in project root, committed to version control)

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

### Claude Code — user scope (`~/.claude.json`, all projects)

Add under the top-level `mcpServers` key:

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

## Available tools

| Tool | Description |
|---|---|
| `search_knowledge` | Full-text search across source index, lock file entries, and local `knowledge/` dirs. Returns structured hits (path, source, tags, description, excerpts) plus a text summary. Params: `query` (space-separated terms, OR semantics), `tags` (comma-separated, AND semantics) |
| `read_artifact` | Read a knowledge artifact by local path, `sourceName:remotePath`, or a path from search results |
| `refresh_knowledge` | Trigger an on-demand sync of the knowledge cache without waiting for the next timer tick. Returns immediately — sync runs in background. Returns a summary or `"A knowledge refresh is already in progress."` if one is already running |

## Available resources

MCP Resources are URI-addressable and appear in `resources/list` or `resources/templates/list`.

| Resource URI | Description |
|---|---|
| `eru://sources` | Lists all configured knowledge sources with URL, branch, and doc count |
| `eru://sources/{name}` | Config details and collection docs for a specific source |
| `eru://installed` | All locally pulled artifacts from the lock file, grouped by source; missing files are flagged `[missing]` |

## Collection caching

The server pre-fetches all collection files on startup and refreshes on the configured interval (default 60 min). Configure in `~/.config/eru/config.json` or `.eru/config.json`:

```json
{ "mcpRefreshIntervalMinutes": 60 }
```

Concurrent syncs are deduplicated — if a sync is already in progress, `refresh_knowledge` returns immediately with a status message rather than starting a second parallel sync.

## Logging

Warnings and errors from background syncs are written to `~/.cache/eru/mcp-YYYYMMDD.log` (XDG-aware; `%LOCALAPPDATA%\eru\` on Windows). Daily rotation, 7-day retention. Console output is suppressed so it does not corrupt the JSON-RPC stdio stream.
