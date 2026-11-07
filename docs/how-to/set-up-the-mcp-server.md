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

## Wire it up for GitHub Copilot

Add to `.github/mcp.json`. Without the .NET tool installed, `dnx` runs it on demand:

```json
{
  "mcpServers": {
    "eru": {
      "type": "local",
      "tools": ["*"],
      "command": "dnx",
      "args": ["Eru.Tool", "--", "mcp"]
    }
  }
}
```

The same `dnx` command and args work for Claude Code's `.mcp.json` (with `"type": "stdio"`).

## Pass settings through the client

Every config setting can be overridden with an `ERU__` environment variable (see
[environment variables](../reference/environment-variables.md)), so a client can configure the server
without touching a config file. Put them in the server's `env` block:

```json
{
  "mcpServers": {
    "eru": {
      "command": "eru",
      "args": ["mcp"],
      "env": {
        "ERU__SEARCH__MODE": "hybrid",
        "ERU__MCP__REFRESHINTERVALMINUTES": "15"
      }
    }
  }
}
```

The same `env` key works in VS Code's `.vscode/mcp.json` and in Claude Code's `.mcp.json`. The values take effect
on start and survive the background refresh. A bad value stops the server at startup with a message on stderr
naming the variable.

## Install the skills, agent and MCP server with APM

eru ships a root `apm.yml`, so a project that uses [APM](https://github.com/microsoft/apm) can install the
`eru`, `semantic-search` and `organizing-documentation` skills, the `ingestor` agent and the `eru` MCP server in one step:

```bash
apm install dburriss/eru
```

To pin a version, append a tag or commit SHA (`dburriss/eru#v0.13.1`). The MCP entry runs `dnx Eru.Tool -- mcp`,
so it needs the .NET 10 SDK.

## Choose a search mode

`search_knowledge` ranks files by keywords (BM25) by default, which needs nothing installed. For questions and
paraphrases ("how do I recover when a call keeps failing" finding a page about retries), switch to semantic or
hybrid search, which also uses a small local embedding model:

```bash
eru config set search.mode '"hybrid"' --global
```

A client can also pass `mode` on a single call. The model (`bge-small-en-v1.5`, about 33 MB) is downloaded once,
by the server in the background after startup, never during a search; until it is there, `sem` and `hybrid`
calls run the keyword search and say so in the result's `warnings`. To control that:

- `FERRET_OFFLINE=1` — never download. Put the model in place yourself and point `FERRET_MODEL_DIR` at the
  directory holding `model_quantized.onnx` and `vocab.txt`.
- `FERRET_MODEL_URL` — download from a mirror instead of Hugging Face (checksums are still verified).

Search indexes live in `~/.cache/eru/ferret` and are rebuilt on demand; `eru cache clear` removes them.
To keep generated files such as `index.md` out of results, adjust `search.ignorePatterns`
(see the [config reference](../reference/config-file.md)).

## Tune the collection cache refresh interval

The server merges the same global (`~/.config/eru/config.json`) and local (`.eru/config.json`) configuration as
the rest of eru. One field controls how often it re-fetches collection files:

```json
{
  "mcp": { "refreshIntervalMinutes": 60 }
}
```

Default is 60 minutes. Several servers share one sync, so the interval is per machine, not per server. On startup the server always runs a full index population regardless of this setting — it
fetches manifests for all configured sources, rebuilds the source index, and caches collection/lock file content
so `search_knowledge` and `read_artifact` are fast without a live git fetch.

## Pause the refresh when a server is idle

An `eru mcp` server that nobody calls still refreshes the cache every interval. To pause that after a quiet
spell, set an idle timeout in minutes (`0`, the default, never pauses):

```json
{
  "mcp": { "idleTimeoutMinutes": 480 }
}
```

The server stays connected; the next request resumes the refresh.

## Fix "N entries indexed" dropping back to 0

If `eru sync` reports `710 entries indexed` and a later run reports `0`, an `eru mcp` server from an older install
is probably still running and overwriting the index. List and stop them, then sync again:

```bash
ps -eo pid,ppid,lstart,command | grep '[e]ru mcp'
pkill -f 'eru mcp'
eru sync
```

Current versions refuse to overwrite an index written by a newer version and exit when a newer eru starts, so this
only applies to servers started before that. `pkill` also stops the server your current client is using; reconnect
the client afterwards.
