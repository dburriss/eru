# eru command reference

Full argument details for all `eru` commands.

## Global flags

| Flag | Description |
|---|---|
| `--debug` | Enable verbose output (shows git progress, etc.) |
| `-o <format>` / `--output <format>` | Output format: `table` (default), `text`, or `json` — available on all commands that produce output |

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
| `<remote-path>` | File to pull — bare filename, `source:path`, full GitHub/GitLab URL, or 8-character hash from `eru search`/`eru source view` |
| `-s <source>` | Source name fallback when no `source:` prefix is given |
| `-c <collection>` | Pull all files in a named collection (e.g. `name` or `source:name`) |
| `-t <tag>` | Filter by tag; repeat for multiple tags (AND semantics) |
| `-d <target>` | Local target path — trailing `/` keeps filename and sets directory; no trailing slash uses path verbatim |
| `--dryrun` | Show what would be pulled without writing anything |
| `--global` | Write any auto-created source entry to the global config |

`eru add` searches through all configured sources when resolving a bare filename or hash.

---

## `eru search`

```
eru search [<terms>...] [-t <tag>]
```

| Argument / Flag | Description |
|---|---|
| `<terms>` | Search terms (space-separated, OR semantics, case-insensitive substring match) |
| `-t <tag>` | Filter results by tag; repeat for multiple tags (AND semantics) |

---

## `eru sync`

```
eru sync [--dryrun]
```

| Flag | Description |
|---|---|
| `--dryrun` | Preview what would change without writing anything |

Performs one git clone per source (not per file), refreshes all manifest caches, rebuilds source indexes, and caches collection and lock file content.

---

## `eru remove`

```
eru remove <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or 8-character path short-hash of the artifact to delete (required) |
| `--dryrun` | Show what would be removed without deleting anything |

Deletes the local file and removes its entry from `.eru/eru.lock`.

---

## `eru disconnect`

```
eru disconnect <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or 8-character path short-hash of the artifact to disconnect (required) |
| `--dryrun` | Show what would be removed without modifying anything |

Removes the lock file entry without touching the local file.

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

## `eru source remove`

```
eru source remove <name> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to remove (required) |
| `-g` | Remove from global config |
| `--dryrun` | Show what would be removed without writing anything |

## `eru source list`

No arguments. Lists all configured sources (local + global), with tags from the manifest cache.

## `eru source view`

```
eru source view <name> [--full]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to inspect (required) |
| `--full` | Show all files without the default 20-entry cap |

Files table columns: **Hash** (8-char SHA-256 of the path, usable with `eru add`), **Path**, **Tags**, **Description**.

## `eru source files`

```
eru source files [<name>] [--refresh]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source. Omit to list files for all configured sources. |
| `--refresh` | Fetch fresh metadata from the source before displaying |

Reads from the source index cache (`~/.cache/eru/sources/<name>/index.json`). No network call unless `--refresh` is passed. Run `eru sync` first if no index has been built.

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

## `eru collection remove`

```
eru collection remove <collection> -f <source:path> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection (required) |
| `-f <source:path>` | File reference to remove as `source:remotePath` (required) |
| `-g` | Write to global config |
| `--dryrun` | Show what would be removed without writing anything |

If removing the file leaves the collection empty, the collection entry itself is also removed.

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

---

## `eru cache prune`

```
eru cache prune [--force]
```

| Flag | Description |
|---|---|
| `--force` | Skip the confirmation prompt and delete immediately |

Removes orphaned content files from the cache — files on disk no longer referenced by any source index entry. Safe to run at any time.

---

## `eru cache clear`

```
eru cache clear [--dryrun] [--force]
```

| Flag | Description |
|---|---|
| `--dryrun` | List what would be deleted without deleting anything |
| `--force` | Skip the confirmation prompt and delete immediately |

Deletes the entire local cache (`~/.cache/eru/`) — all source indices, cached content, search index, and collection data. Run `eru sync` after clearing to rebuild.

---

## `eru site generate`

```
eru site generate [-o <dir>] [--open] [--custom-css <path>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the generated site into |
| `--open` | off | Open `index.html` in the default browser after generation |
| `--custom-css <path>` | — | Path to a CSS file copied into the site and loaded after `style.css` |

Generates a self-contained static HTML site for browsing and searching the local knowledge cache. Fully navigable without JavaScript; JS adds in-place search and facet filtering as an optional enhancement.
