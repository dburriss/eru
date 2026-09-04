---
title: eru CLI reference
type: reference
tags: [cli, commands, flags]
---

# eru CLI reference

## Global flags

| Flag | Description |
|---|---|
| `--debug` | Enable verbose output (shows git progress, etc.) |

---

## `eru init`

Scaffold a new eru configuration.

```
eru init [--force] [--global] [<dir>]
```

| Argument / Flag | Description |
|---|---|
| `<dir>` | Directory in which to create the config (default: current directory) |
| `--force` | Overwrite an existing `.eru/config.json` |
| `--global` | Create the global config at `~/.config/eru/config.json` instead |

**Examples**

```bash
eru init                   # create .eru/config.json in the current directory
eru init /path/to/project  # create in a specific directory
eru init --global          # create ~/.config/eru/config.json
eru init --force           # overwrite an existing local config
```

---

## `eru add`

Pull a file or collection from a knowledge source into the current repo and record it in `.eru/eru.lock`.

```
eru add [<remote-path>] [-s <source>] [-c <collection>] [-t <tag>] [-d <target>] [--dryrun] [--global]
```

| Argument / Flag | Description |
|---|---|
| `<remote-path>` | File to pull — bare filename, `source:path`, or a full GitHub/GitLab URL |
| `-s <source>` | Source name fallback when no `source:` prefix is given |
| `-c <collection>` | Pull all files in a named collection (e.g. `name` or `source:name`) |
| `-t <tag>` | Filter by tag; repeat for multiple tags (AND semantics) |
| `-d` / `--target` | Local target path — trailing `/` keeps filename and sets directory; no trailing slash uses path verbatim |
| `--dryrun` | Show what would be pulled without writing anything |
| `--global` | Write any auto-created source entry to the global config |

**Examples**

```bash
# Paste a GitHub URL — source is auto-configured
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md

# Pull by source:path shorthand
eru add knowledge:docs/adr-template.md

# Pull all files in a named collection
eru add --collection onboarding-docs

# Pull everything tagged "devops"
eru add --tag devops

# Preview without writing
eru add knowledge:docs/adr-template.md --dryrun
```

---

## `eru search`

Search across all files eru knows about: manifest-advertised files (from the source index), collection entries, and locally pulled files.

```
eru search [<terms>...] [-t <tag>]
```

| Argument / Flag | Description |
|---|---|
| `<terms>` | Search terms (space-separated) |
| `-t <tag>` | Filter results by tag; repeat for multiple tags |

**Examples**

```bash
eru search adr template
eru search --tag devops
eru search pipeline --tag ci --tag devops
```

---

## `eru sync`

Refresh all source metadata, rebuild the local search index, cache collection content, and update any locally pulled files that have drifted from their upstream source.

```
eru sync [--dryrun]
```

| Flag | Description |
|---|---|
| `--dryrun` | Preview what would change without writing anything |

**Examples**

```bash
eru sync
eru sync --dryrun
```

Each lock file entry is reported as one of: **current**, **drifted** (overwritten), **missing** (remote gone), or **skipped** (source not configured).

`eru sync` also rebuilds `~/.cache/eru/sources/<name>/index.json` for every configured source and pre-caches collection and lock file content for fast offline search.

---

## `eru source`

Manage knowledge sources.

### `eru source add`

Register a git repository or local path as a knowledge source.

```
eru source add <url> [-n <name>] [-b <branch>] [-p <basepath>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<url>` | Git URL or local path of the knowledge source (required) |
| `-n <name>` | Override the derived source name |
| `-b <branch>` | Branch to track |
| `-p <basepath>` | Explicitly set the base path, skipping auto-detection |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru source add https://github.com/my-org/knowledge
eru source add https://github.com/my-org/knowledge -n org-knowledge -b main
eru source add https://github.com/my-org/knowledge -g   # add to global config
```

### `eru source list`

List all configured knowledge sources (merged from global and local config).

```
eru source list
```

### `eru source view`

Show details and available files for a specific source.

```
eru source view <name> [--full]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to inspect (required) |
| `--full` | Show all files without the default 20-entry cap |

**Examples**

```bash
eru source view knowledge
eru source view knowledge --full
```

### `eru source files`

List all files advertised by a source, reading from the local source index. No network call by default.

```
eru source files [<name>] [--refresh]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source. Omit to list files for all configured sources. |
| `--refresh` | Fetch fresh metadata from the source before displaying |

**Examples**

```bash
eru source files                     # all sources, from local index
eru source files knowledge           # one source, from local index
eru source files knowledge --refresh # re-fetch from network, then display
```

If no index has been built yet, run `eru sync` first.

### `eru source remove`

Remove a knowledge source from the config.

```
eru source remove <name> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to remove (required) |
| `-g` | Remove from global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru source remove old-source
eru source remove shared-knowledge -g --dryrun
```

---

## `eru collection`

Manage collections — curated groups of file references that can be pulled as a unit.

### `eru collection create`

Create a new empty collection.

```
eru collection create <name> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the new collection (required) |
| `-t <tag>` | Tag for the collection; repeat for multiple tags |
| `-d <description>` | Short description of the collection |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru collection create onboarding-docs -d "Files every new engineer needs"
eru collection create adr-pack -t adr -t docs -g
```

### `eru collection add`

Add a file reference to an existing collection.

```
eru collection add <collection> -f <source:remotePath> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection to add to (required) |
| `-f <source:path>` | File reference as `source:remotePath` — e.g. `knowledge:docs/guide.md` (required) |
| `-t <tag>` | Tag for this file reference; repeat for multiple tags |
| `-d <description>` | Short description of the file reference |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru collection add onboarding-docs -f knowledge:docs/adr-template.md
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/template.md -t adr
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/log.md -t adr -g
```

### `eru collection remove`

Remove a file reference from a collection.

```
eru collection remove <collection> -f <source:remotePath> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection (required) |
| `-f <source:path>` | File reference to remove as `source:remotePath` (required) |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru collection remove onboarding-docs -f knowledge:docs/old-guide.md
eru collection remove adr-pack -f knowledge:KNOWLEDGE/adr/template.md --dryrun
```

---

## `eru manifest`

Manage the `.eru/manifest.json` for a knowledge-source repo. The manifest declares which files the source exposes to consumers. Use these commands in the repo that *publishes* knowledge, not the repo that consumes it.

### `eru manifest init`

Create a new empty manifest.

```
eru manifest init [--force]
```

| Flag | Description |
|---|---|
| `--force` | Overwrite an existing `.eru/manifest.json` |

**Examples**

```bash
eru manifest init           # creates .eru/manifest.json with { version: 1, files: [] }
eru manifest init --force   # overwrite an existing manifest
```

### `eru manifest add`

Add a file or glob entry to the manifest.

```
eru manifest add <path> [-t <tag>] [-d <description>] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | File path or gitignore-style glob (e.g. `docs/*.md`, `templates/**/*.yaml`) (required) |
| `-t <tag>` | Tag for the entry; repeat for multiple tags |
| `-d <description>` | Short description of the entry |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru manifest add "README.md" -t meta
eru manifest add "docs/*.md" -t docs -d "All documentation"
eru manifest add "templates/**/*.yaml" -t templates --dryrun
```

### `eru manifest remove`

Remove an entry from the manifest by exact path match.

```
eru manifest remove <path> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Exact path to remove (required) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru manifest remove "README.md"
eru manifest remove "docs/*.md" --dryrun
```

### `eru manifest verify`

Resolve every manifest entry against local files and report any that match nothing. Exits with code 1 if any entries are unresolved.

```
eru manifest verify
```

**Examples**

```bash
eru manifest verify   # exits 0 if all entries resolve, 1 otherwise
```

Glob patterns are expanded against the current directory tree. An entry like `docs/*.md` must match at least one local file to pass.

---

## `eru remove`

Delete a locally pulled file from disk and remove its entry from `.eru/eru.lock`.

```
eru remove <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or path short-hash of the file to remove (required) |
| `--dryrun` | Preview without deleting |

**Examples**

```bash
eru remove docs/adr-template.md
eru remove a1b2c3d4               # remove by short hash
eru remove docs/adr-template.md --dryrun
```

---

## `eru disconnect`

Remove a file's entry from `.eru/eru.lock` without deleting the local file. Use this when you want to keep the file on disk but stop eru tracking it.

```
eru disconnect <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or path short-hash of the file to disconnect (required) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru disconnect docs/adr-template.md
eru disconnect a1b2c3d4               # disconnect by short hash
eru disconnect docs/adr-template.md --dryrun
```

---

## `eru browse`

Open an interactive TUI for browsing sources and tracked files.

```
eru browse
```

No arguments.

---

## `eru cache`

Manage the local knowledge cache.

### `eru cache prune`

Remove orphaned content files from the cache — files that exist on disk but are no longer referenced by any source index entry.

```
eru cache prune [--force]
```

| Flag | Description |
|---|---|
| `--force` | Skip the confirmation prompt and delete immediately |

**Examples**

```bash
eru cache prune          # list orphans and prompt before deleting
eru cache prune --force  # delete without prompting
```

Orphans accumulate when files are removed from a manifest or when sources are deleted. `eru cache prune` is safe to run at any time; it only removes files not referenced by the current index.

### `eru cache clear`

Delete the entire local cache — all source indices, cached content, search index, and collection data under `~/.cache/eru/`.

```
eru cache clear [--dryrun] [--force]
```

| Flag | Description |
|---|---|
| `--dryrun` | List what would be deleted without deleting anything |
| `--force` | Skip the confirmation prompt and delete immediately |

**Examples**

```bash
eru cache clear --dryrun    # preview what would be removed
eru cache clear             # prompt before deleting
eru cache clear --force     # delete without prompting
```

Run `eru sync` after clearing to rebuild the cache from all configured sources.

---

## `eru site`

Generate a self-contained static HTML site for browsing and searching the local knowledge cache.

### `eru site generate`

```
eru site generate [-o <dir>] [--open] [--custom-css <path>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the generated site into |
| `--open` | off | Open `index.html` in the default browser after generation |
| `--custom-css <path>` | — | Path to a CSS file copied into the site on every run and loaded after `style.css` |

**Examples**

```bash
# Generate into the default ./cache-site/ directory
eru site generate

# Generate into a custom directory and open the browser
eru site generate -o /tmp/my-site --open

# Apply a custom theme on every run
eru site generate --custom-css ~/themes/company.css
```

The site is fully navigable as plain HTML with no JavaScript. JS adds in-place search and checkbox facet filtering as an optional enhancement. See [site generation reference](site-generation.md) and [customize the generated site](../how-to/customize-the-generated-site.md) for details.

### `eru site serve`

Generate the site and start a local HTTP server with live reload and a full-text search API.

```
eru site serve [-o <dir>] [-p <port>] [--open] [--sync-interval <minutes>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the site into |
| `-p` / `--port` | `5173` | HTTP port to listen on |
| `--open` | off | Open the browser automatically when the server starts |
| `--sync-interval <minutes>` | `15` | Minutes between background cache syncs |

**Examples**

```bash
# Serve on the default port
eru site serve

# Custom port, open browser automatically
eru site serve -p 8080 --open

# Custom output directory with a faster sync interval
eru site serve -o ./docs-site/ --sync-interval 5
```

The server exposes three endpoints in addition to the static site files:

| Endpoint | Description |
|---|---|
| `GET /api/search?q=<terms>` | Full-text search returning JSON `{ hits: [...] }` |
| `GET /api/sync` | Trigger an immediate background sync + site rebuild (returns 202) |
| `GET /api/events` | SSE stream — sends `data: rebuild` after every successful sync |

The browser connects to `/api/events` automatically and reloads the page on each `rebuild` event. Press `Ctrl+C` to stop the server.

---

## `eru mcp`

Start an MCP stdio server that exposes eru's knowledge search and retrieval capabilities to AI agents.

```
eru mcp
```

No arguments. See the [MCP server reference](mcp-server.md) and [set up the MCP server](../how-to/set-up-the-mcp-server.md) for configuration and tool details.
