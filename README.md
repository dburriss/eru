# eru

`eru` is a CLI tool for sharing knowledge files between projects. Declare where your shared files live (a git repo), pull them in with a single command, and track everything in a lock file so they stay in sync.

- **Manifests** — knowledge-source repos publish `.eru/manifest.json` to declare available files; consumers pull from it automatically
- **Collections** — group related files into a named set and pull them all with one command
- **Tag-based pulls** — `--tag devops` fetches everything tagged `devops` across all collections
- **Glob patterns** — collection entries can use globs to pull multiple files in one reference (e.g. `docs/*.md`)
- **Lock file** (`.eru/eru.lock`) records every pulled file: its origin, path, and content hash
- **Dry-run mode** on every write command
- **Global config** (`~/.config/eru/config.json`) for sources and collections shared across all your repos

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [git](https://git-scm.com/)

## Install

```bash
dotnet tool install --global Eru.Tool
```

## Quick start

### 1. Initialise your repo

```bash
eru init
```

Creates `.eru/config.json` in the current directory.

### 2. Pull a file

Paste a GitHub or GitLab file URL directly:

```bash
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md
```

eru registers the source automatically, downloads the file, and records it in `.eru/eru.lock`.

Or pull by source name and path once a source is configured:

```bash
eru add knowledge:docs/adr-template.md
```

Or pull an entire curated collection:

```bash
eru add --collection onboarding-docs
```

### 3. Keep files up to date

```bash
eru sync
```

Fetches every file in `.eru/eru.lock`, compares content hashes, and overwrites anything that has drifted.

---

## Commands

| Command | What it does |
|---|---|
| `eru init` | Scaffold `.eru/config.json` in the current directory |
| `eru init --global` | Create the global config at `~/.config/eru/config.json` |
| `eru add <path>` | Pull a file by `source:path`, bare filename, or full URL |
| `eru add --collection <name>` | Pull all files in a named collection |
| `eru add --tag <tag>` | Pull all files matching a tag |
| `eru add --dryrun <path>` | Preview what would be pulled |
| `eru search <terms>` | Search sources and the lock file |
| `eru sync` | Re-fetch all tracked files and update drifted ones |
| `eru sync --dryrun` | Preview what sync would change |
| `eru source add <url>` | Register a git repo as a knowledge source |
| `eru source list` | List configured knowledge sources |
| `eru source view <name>` | Show details and files for a source |
| `eru collection create <name>` | Create a new collection |
| `eru collection add <name> -f <source:path>` | Add a file reference to a collection |
| `eru manifest init` | Create `.eru/manifest.json` in a knowledge-source repo |
| `eru manifest add <path>` | Add a file/glob entry to the manifest |
| `eru manifest remove <path>` | Remove an entry from the manifest |
| `eru manifest verify` | Check all manifest entries resolve to local files |
| `eru mcp` | Start an MCP stdio server for AI agent use |

For full argument details see [docs/cli-reference.md](docs/cli-reference.md).

## MCP server

`eru mcp` exposes knowledge search and retrieval to AI agents (Claude, Copilot, Cursor, etc.) over the Model Context Protocol. See [docs/mcp.md](docs/mcp.md).
