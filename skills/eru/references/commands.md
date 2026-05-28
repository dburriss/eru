# eru command reference

Full argument details for all `eru` commands.

---

## `eru init`

```
eru init [--force] [--global] [<dir>]
```

| Argument / Flag | Description |
|---|---|
| `<dir>` | Directory in which to create the config (default: current directory) |
| `--force` | Overwrite an existing config |
| `--global` | Create the global config at `~/.config/eru/config.json` |

---

## `eru add`

```
eru add [<remote-path>] [-s <source>] [-c <collection>] [-t <tag>] [-d <target>] [--dryrun] [--global]
```

| Argument / Flag | Description |
|---|---|
| `<remote-path>` | File to pull — bare filename, `source:path`, or a full GitHub/GitLab URL |
| `-s <source>` | Source name fallback when no `source:` prefix is given |
| `-c <collection>` | Pull all files in a named collection |
| `-t <tag>` | Filter by tag; repeat for multiple tags (AND semantics) |
| `-d <target>` | Local directory to write files into |
| `--dryrun` | Show what would be pulled without writing anything |
| `--global` | Write any auto-created source entry to the global config |

---

## `eru search`

```
eru search [<terms>...] [-t <tag>]
```

| Argument / Flag | Description |
|---|---|
| `<terms>` | Search terms (space-separated) |
| `-t <tag>` | Filter results by tag; repeat for multiple tags |

---

## `eru sync`

```
eru sync [--dryrun]
```

| Flag | Description |
|---|---|
| `--dryrun` | Preview what would change without writing anything |

---

## `eru source add`

```
eru source add <url> [-n <name>] [-b <branch>] [-p <basepath>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<url>` | Git URL or local path of the knowledge source (required) |
| `-n <name>` | Override the derived source name |
| `-b <branch>` | Branch to track |
| `-p <basepath>` | Explicitly set the base path, skipping auto-detection |
| `-g` | Write to global config |
| `--dryrun` | Show what would be added without writing anything |

## `eru source list`

No arguments.

## `eru source view`

```
eru source view <name> [--full]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to inspect (required) |
| `--full` | Show all files without the default 20-entry cap |

---

## `eru collection create`

```
eru collection create <name> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the new collection (required) |
| `-t <tag>` | Tag for the collection; repeat for multiple tags |
| `-d <description>` | Short description of the collection |
| `-g` | Write to global config |
| `--dryrun` | Show what would be created without writing anything |

## `eru collection add`

```
eru collection add <collection> -f <source:path> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection to add to (required) |
| `-f <source:path>` | File reference as `source:remotePath` — e.g. `knowledge:docs/guide.md` (required) |
| `-t <tag>` | Tag for this file reference; repeat for multiple tags |
| `-d <description>` | Short description of the file reference |
| `-g` | Write to global config |
| `--dryrun` | Show what would be added without writing anything |

---

## `eru manifest init`

```
eru manifest init [--force]
```

| Flag | Description |
|---|---|
| `--force` | Overwrite an existing `.eru/manifest.json` |

## `eru manifest add`

```
eru manifest add <path> [-t <tag>] [-d <description>] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | File path or gitignore-style glob (e.g. `docs/*.md`, `templates/**/*.yaml`) (required) |
| `-t <tag>` | Tag for the entry; repeat for multiple tags |
| `-d <description>` | Short description of the entry |
| `--dryrun` | Preview without writing |

## `eru manifest remove`

```
eru manifest remove <path> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Exact path to remove (required) |
| `--dryrun` | Preview without writing |

## `eru manifest verify`

```
eru manifest verify
```

No arguments. Exits 0 if all entries resolve to at least one local file, 1 otherwise.
