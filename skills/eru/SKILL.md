---
name: eru
description: Use this skill when the user wants to pull, sync, or manage shared knowledge files with eru. Triggers on "eru add", "eru sync", "eru source", "eru collection", "set up eru", "pull a knowledge file", "add a source to eru", or any question about using the eru CLI tool.
---

# eru

`eru` is a CLI tool for sharing knowledge files between projects. Declare where your shared files live (a git repo), pull them in with a single command, and track everything in a lock file so they stay in sync.

## Key concepts

| Concept | Description |
|---|---|
| **Source** | A git repo (or local path) that serves as the canonical origin for shared files |
| **Lock file** | `.eru/eru.lock` — records every pulled file: source, path, and content hash |
| **Collection** | A named group of file references that can be pulled as a unit |
| **Tags** | Metadata on files/collections for filtered pulls |
| **Global config** | `~/.config/eru/config.json` — sources and collections shared across all repos |
| **Local config** | `.eru/config.json` — per-repo configuration |

## Commands

### `eru init`

Scaffold a new eru configuration.

```
eru init [--global] [--force] [<dir>]
```

```bash
eru init                   # create .eru/config.json in current directory
eru init --global          # create ~/.config/eru/config.json
eru init --force           # overwrite existing config
eru init /path/to/project  # create in a specific directory
```

---

### `eru add`

Pull a file or collection from a knowledge source into the current repo.

```
eru add [<remote-path>] [-s <source>] [-c <collection>] [-t <tag>] [-d <target>] [--dryrun] [--global]
```

| Flag | Description |
|---|---|
| `<remote-path>` | Bare filename, `source:path`, or full GitHub/GitLab URL |
| `-s <source>` | Source name fallback when no `source:` prefix is given |
| `-c <collection>` | Pull all files in a named collection |
| `-t <tag>` | Filter by tag (repeat for multiple — AND semantics) |
| `-d <target>` | Local directory to write files into |
| `--dryrun` | Preview without writing |
| `--global` | Write any auto-created source to global config |

```bash
# Paste a GitHub URL — source is auto-configured
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md

# Pull by source:path shorthand
eru add knowledge:docs/adr-template.md

# Pull all files in a collection
eru add --collection onboarding-docs

# Pull everything tagged "devops"
eru add --tag devops

# Preview without writing
eru add knowledge:docs/adr-template.md --dryrun
```

---

### `eru search`

Search across configured knowledge sources and the lock file.

```
eru search [<terms>...] [-t <tag>]
```

```bash
eru search adr template
eru search --tag devops
eru search pipeline --tag ci --tag devops
```

---

### `eru sync`

Re-fetch every file tracked in `.eru/eru.lock` and overwrite anything that has drifted.

```
eru sync [--dryrun]
```

```bash
eru sync
eru sync --dryrun   # preview what would change
```

Each file is reported as: **current**, **drifted** (overwritten), **missing** (remote gone), or **skipped** (source not configured).

---

### `eru source`

Manage knowledge sources.

```
eru source add <url> [-n <name>] [-b <branch>] [-p <basepath>] [-g]
eru source list
eru source view <name> [--full]
```

| Flag | Description |
|---|---|
| `-n <name>` | Override the derived source name |
| `-b <branch>` | Branch to track |
| `-p <basepath>` | Explicitly set the base path |
| `-g` | Write to global config |
| `--full` | Show all files (no 20-entry cap) on `view` |

```bash
eru source add https://github.com/my-org/knowledge
eru source add https://github.com/my-org/knowledge -n org-knowledge -b main -g
eru source list
eru source view knowledge
eru source view knowledge --full
```

---

### `eru collection`

Manage collections — curated groups of file references.

```
eru collection create <name> [-t <tag>] [-d <description>] [-g]
eru collection add <collection> -f <source:path> [-t <tag>] [-d <description>] [-g]
```

```bash
eru collection create onboarding-docs -d "Files every new engineer needs"
eru collection create adr-pack -t adr -t docs -g

eru collection add onboarding-docs -f knowledge:docs/adr-template.md
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/template.md -t adr -g
```

---

### `eru mcp`

Start an MCP stdio server exposing knowledge search and retrieval to AI agents.

```
eru mcp
```

No arguments. See the MCP section below for wiring it up.

---

## Common workflows

### New repo setup

```bash
eru init
eru source add https://github.com/my-org/knowledge -g
eru add knowledge:docs/adr-template.md
```

### Pull by URL (zero config)

```bash
eru add https://github.com/my-org/knowledge/blob/main/docs/guide.md
# eru auto-registers the source and records the file in .eru/eru.lock
```

### Pull an entire collection

```bash
eru add --collection onboarding-docs
```

### Keep files up to date

```bash
eru sync
```

### Preview before committing

```bash
eru add knowledge:docs/style-guide.md --dryrun
eru sync --dryrun
```

---

## MCP server

`eru mcp` exposes two tools over the Model Context Protocol:

| Tool | What it does |
|---|---|
| `search_knowledge` | Full-text search across cached collections, lock file entries, and local `knowledge/` dirs |
| `read_artifact` | Read the full content of a knowledge artifact by local path, `source:path`, or lock file entry |

**Claude Desktop** (`claude_desktop_config.json`):

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

**VS Code** (`.vscode/mcp.json`):

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

The server pre-fetches all collection files on startup and refreshes them every 60 minutes (configurable via `mcpRefreshIntervalMinutes` in config).
