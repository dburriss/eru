# eru

`eru` is a CLI tool for sharing knowledge files between projects. Declare where your shared files live (a git repo), pull them in with a single command, and track everything in a lock file so they stay in sync.

- **Manifests** — knowledge-source repos publish `.eru/manifest.json` to declare available files; consumers pull from it automatically
- **Collections** — group related files into a named set and pull them all with one command
- **Tag-based pulls** — `--tag devops` fetches everything tagged `devops` across all collections
- **Glob patterns** — collection entries can use globs to pull multiple files in one reference (e.g. `docs/*.md`)
- **Lock file** (`.eru/eru.lock`) records every pulled file: its origin, path, and content hash
- **Static site** — `eru site generate` builds a self-contained HTML site for browsing and searching the local knowledge cache; `eru site serve` adds live reload and a search API
- **Dry-run mode** on every write command
- **Global config** (`~/.config/eru/config.json`) for sources and collections shared across all your repos

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- [git](https://git-scm.com/)

## Install

Install the `Eru.Tool` tool:

```bash
dotnet tool install --global Eru.Tool
```

Leave off `--global` to install locally in a repo instead.

Prebuilt self-contained binaries (`linux-x64`, `win-x64`, `osx-x64`) are also attached to each [GitHub release](https://github.com/dburriss/eru/releases) if you'd rather not install the .NET SDK.

Install the skill with [skills.sh](https://www.skills.sh/):

```bash
npx skills add dburriss/eru
```

The skill tells agents how to use the CLI.

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
| `eru source files <name>` | List the files a source exposes |
| `eru source bundle add` | Register a knowledge bundle (manifest or OKF) on a source |
| `eru source remove <name>` | Remove a source |
| `eru collection create <name>` | Create a new collection |
| `eru collection add <name> -f <source:path>` | Add a file reference to a collection |
| `eru remove <path>` | Delete a pulled file and drop it from the lock file |
| `eru disconnect <path>` | Stop tracking a file but keep it on disk |
| `eru browse` | Browse the knowledge cache in an interactive terminal UI |
| `eru cache prune` / `eru cache clear` | Manage the local knowledge cache |
| `eru inbox add/list/remove/default` | Manage inboxes (local, or remote git repos) |
| `eru inbox channel add/list/remove` | Manage channels and their ACP agents |
| `eru inbox send` | Send an item to an inbox |
| `eru inbox process` / `eru inbox watch` | Run inbox items through the channel's agent, once or continuously |
| `eru manifest init` | Create `.eru/manifest.json` in a knowledge-source repo |
| `eru manifest add <path>` | Add a file/glob entry to the manifest |
| `eru manifest remove <path>` | Remove an entry from the manifest |
| `eru manifest validate` | Check all manifest entries resolve to local files (alias: `verify`) |
| `eru site generate [-o <dir>]` | Generate a static HTML site for browsing the local knowledge cache |
| `eru site serve [-p <port>]` | Serve the site locally with live reload and a search API |
| `eru okf validate <path>` | Check a directory tree for OKF §11 conformance |
| `eru okf init <path>` | Create missing OKF `index.md` files |
| `eru okf fix <path>` | Repair a directory tree so it passes OKF validation |
| `eru okf verify <file>` | Record a human/machine verification on a concept file |
| `eru version` | Print the version and git commit |
| `eru mcp` | Start an MCP stdio server for AI agent use |

For full argument details see [docs/reference/cli.md](docs/reference/cli.md).

## Static site

`eru site generate` builds a self-contained HTML site from the local knowledge cache — browse and search all source files offline.

`eru site serve` does the same but also starts a local HTTP server with a live-reload SSE feed and a `/api/search` endpoint. The browser reconnects and reloads automatically whenever the cache is synced. See [docs/reference/site-generation.md](docs/reference/site-generation.md) and [docs/how-to/customize-the-generated-site.md](docs/how-to/customize-the-generated-site.md).

## MCP server

`eru mcp` exposes knowledge search and retrieval to AI agents (Claude, Copilot, Cursor, etc.) over the Model Context Protocol. See [docs/how-to/set-up-the-mcp-server.md](docs/how-to/set-up-the-mcp-server.md).

## Contributing

Run `mise install` for the toolchain, then `dotnet build` and `dotnet test --solution eru.slnx`. CI builds and tests every push and PR; pushing a `v*` tag publishes the NuGet package and GitHub release binaries. See [AGENTS.md](AGENTS.md) for the project layout, conventions and release flow.

## Documentation

Full documentation lives under [docs/](docs/README.md), organised by [Diataxis](https://diataxis.fr/): tutorials, how-to guides, reference, and explanation.
