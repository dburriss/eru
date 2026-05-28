# eru MCP setup

`eru mcp` starts a stdio MCP server exposing `search_knowledge` and `read_artifact` to AI agents.

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
| `search_knowledge` | Full-text search across cached collections, lock file entries, and local `knowledge/` dirs. Params: `query` (space-separated terms), `tags` (comma-separated, AND semantics) |
| `read_artifact` | Read a knowledge artifact by local path, `sourceName:remotePath`, or lock file path |

## Collection caching

The server pre-fetches all collection files on startup and refreshes on the configured interval (default 60 min). Configure in `~/.config/eru/config.json` or `.eru/config.json`:

```json
{ "mcpRefreshIntervalMinutes": 60 }
```
