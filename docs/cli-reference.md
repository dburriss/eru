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
| `-d <target>` | Local directory to write files into |
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

Search across configured knowledge sources and the lock file.

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

Re-fetch every file tracked in `.eru/eru.lock` and overwrite anything that has drifted from its upstream source.

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

Each file is reported as one of: **current**, **drifted** (overwritten), **missing** (remote gone), or **skipped** (source not configured).

---

## `eru source`

Manage knowledge sources.

### `eru source add`

Register a git repository or local path as a knowledge source.

```
eru source add <url> [-n <name>] [-b <branch>] [-p <basepath>] [-g]
```

| Argument / Flag | Description |
|---|---|
| `<url>` | Git URL or local path of the knowledge source (required) |
| `-n <name>` | Override the derived source name |
| `-b <branch>` | Branch to track |
| `-p <basepath>` | Explicitly set the base path, skipping auto-detection |
| `-g` | Write to global config (`~/.config/eru/config.json`) |

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

---

## `eru collection`

Manage collections — curated groups of file references that can be pulled as a unit.

### `eru collection create`

Create a new empty collection.

```
eru collection create <name> [-t <tag>] [-d <description>] [-g]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the new collection (required) |
| `-t <tag>` | Tag for the collection; repeat for multiple tags |
| `-d <description>` | Short description of the collection |
| `-g` | Write to global config (`~/.config/eru/config.json`) |

**Examples**

```bash
eru collection create onboarding-docs -d "Files every new engineer needs"
eru collection create adr-pack -t adr -t docs -g
```

### `eru collection add`

Add a file reference to an existing collection.

```
eru collection add <collection> -f <source:remotePath> [-t <tag>] [-d <description>] [-g]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection to add to (required) |
| `-f <source:path>` | File reference as `source:remotePath` — e.g. `knowledge:docs/guide.md` (required) |
| `-t <tag>` | Tag for this file reference; repeat for multiple tags |
| `-d <description>` | Short description of the file reference |
| `-g` | Write to global config (`~/.config/eru/config.json`) |

**Examples**

```bash
eru collection add onboarding-docs -f knowledge:docs/adr-template.md
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/template.md -t adr
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/log.md -t adr -g
```

---

## `eru mcp`

Start an MCP stdio server that exposes eru's knowledge search and retrieval capabilities to AI agents.

```
eru mcp
```

No arguments. See [docs/mcp.md](mcp.md) for configuration and tool details.
