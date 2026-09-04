---
title: Set up the MCP server
type: how-to
tags: [mcp, agent, integration]
---

# Set up the MCP server

`eru mcp` starts a Model Context Protocol (MCP) stdio server, exposing eru's knowledge search and retrieval to AI
agents (Claude, Copilot, Cursor, etc.). See the [MCP server reference](../reference/mcp-server.md) for the
resources and tools it exposes.

## Start the server manually

```bash
eru mcp
```

The server reads from stdin and writes to stdout using the MCP protocol.

## Wire it up in Claude Desktop

Add to `claude_desktop_config.json`:

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

## Wire it up in VS Code

Add to `.vscode/mcp.json`:

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

## Tune the collection cache refresh interval

The server merges the same global (`~/.config/eru/config.json`) and local (`.eru/config.json`) configuration as
the rest of eru. One field controls how often it re-fetches collection files:

```json
{
  "mcpRefreshIntervalMinutes": 60
}
```

Default is 60 minutes. On startup the server always runs a full index population regardless of this setting — it
fetches manifests for all configured sources, rebuilds the source index, and caches collection/lock file content
so `search_knowledge` and `read_artifact` are fast without a live git fetch.
