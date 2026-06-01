# Inspecting state and searching

Four commands let you read what eru knows without pulling any files: `source list`, `source view`, `source files`, and `search`. They differ in where they read from and whether they touch the network.

| Command | Network? | Data sources |
|---|---|---|
| `eru source list` | No | Local + global config, manifest cache |
| `eru source view <name>` | No | Local + global config, manifest cache |
| `eru source files [name]` | **Yes** | Manifest cache (patterns) + live `git ls-tree` |
| `eru search` | No | Global config collections, lock file |

---

## `eru source list`

Lists every configured source — both local (`.eru/config.json`) and global (`~/.config/eru/config.json`).

```bash
eru source list
```

**What is shown:**

| Column | What it is |
|---|---|
| Name | Source name as declared in config |
| URL | Remote URL (blank for local-path sources) |
| Branch | Branch or ref to fetch from |
| BasePath | Path prefix stripped before writing local files |
| Scope | `local`, `local → global alias`, or `global` |
| Tags | All distinct tags collected from the cached manifest |

**Where the data comes from:**

- Source metadata (Name, URL, Branch, BasePath, Scope) — read directly from config files on disk. Always current.
- Tags — read from the manifest cache at `~/.cache/eru/sources/<name>/manifest.json`. Can be stale if the source's manifest changed since the last `eru sync`.

If no manifest has been cached for a source, its Tags column is blank.

---

## `eru source view <name>`

Shows full detail for a single source: its config metadata and the file list from its cached manifest.

```bash
eru source view <name>
eru source view <name> --full    # show all manifest entries (default cap: 20)
```

**What is shown:**

Metadata table:

| Field | What it is |
|---|---|
| Name | Source name |
| Scope | `local` or `global` |
| URL | Remote URL (omitted if absent) |
| Branch | Branch or ref (omitted if absent) |
| BasePath | Path prefix (omitted if absent) |

Files table (only shown when a manifest is cached):

| Column | What it is |
|---|---|
| Hash | 8-character SHA-256 of the file path (for referencing) |
| Path | Path or glob pattern as declared in the source's `.eru/manifest.json` |
| Tags | Tags declared on that manifest entry |
| Description | Description declared on that manifest entry |

By default the files table is capped at 20 rows. Pass `--full` to see all entries.

**Where the data comes from:**

- Metadata — local + global config files. Always current.
- Files — manifest cache at `~/.cache/eru/sources/<name>/manifest.json`. Can be stale.

If no manifest is cached, eru prints `"No manifest cached. Run 'eru sync' to fetch source metadata."` and no files table is shown.

---

## `eru source files [name]`

Resolves the concrete, expanded file list for a source by combining the cached manifest patterns with a live listing of the remote repository. This is the only read command that makes a network call.

```bash
eru source files              # all configured sources
eru source files <name>       # a specific source
```

**What is shown:**

| Column | What it is |
|---|---|
| Source | Source name |
| Hash | 8-character SHA-256 of the file path |
| Path | Concrete file path from the remote repository |
| Tags | Tags merged from all matching manifest entries |
| Description | Description from the first matching manifest entry |

Only files whose paths match at least one pattern in the manifest are included. Files in the remote repo that the manifest does not cover are silently excluded.

**Where the data comes from:**

- Manifest patterns — manifest cache (`~/.cache/eru/sources/<name>/manifest.json`). Refreshed by `eru sync`; can be stale between syncs.
- Actual file paths — live `git clone --depth=1` of the remote, followed by `git ls-tree -r`. Always current. Requires network access.

Because the manifest patterns are cached but the file tree is live, the two can temporarily drift if the remote repo gains or loses files between syncs. The matching step always uses the live file tree against the cached patterns.

A spinner is shown while the remote fetch is in progress.

---

## `eru search`

Searches the files you have declared in collections and the files already pulled into the local repo.

```bash
eru search adr                          # substring search across paths and descriptions
eru search --tags template              # filter by tag (AND across multiple tags)
eru search adr --tags template          # combined: term match AND tag filter
```

**What is shown:**

| Column | What it is |
|---|---|
| Source | Source name |
| Path | Remote path of the file |
| Hash | 8-character SHA-256 of the remote path |
| Tags | Tags from the collection entry |
| Local Path | Where the file lives locally, if it has been pulled (blank otherwise) |
| Description | Description from the collection entry |

**Where the data comes from:**

1. **Global config collections** (`~/.config/eru/config.json`) — each file declared in a `CollectionConfig` becomes a search result with its source, path, tags, and description.
2. **Lock file** (`eru.lock`) — every locally pulled file is included. If it also appears in a collection, the lock entry enriches the collection result with a `Local Path`. Lock entries with no matching collection entry appear at the end with no tags or description.

**What is not searched:** The manifest cache is not consulted. `eru search` only knows about files you have explicitly added to a collection or already pulled.

**Filtering:**

- Terms (positional args): OR semantics, case-insensitive substring match against remote path, local path, and description. Omit to match everything.
- `--tags`: AND semantics, case-insensitive exact match. All specified tags must be present.

Results are returned in config order (collections) followed by lock-only entries. There is no relevance ranking.

---

## Data freshness summary

| Data | Refreshed by | Can be stale? |
|---|---|---|
| Source config (URL, branch, etc.) | `eru source add` / manual edit | No — always read live from disk |
| Manifest cache (tags, patterns, descriptions) | `eru sync`, `eru source add` | Yes — until next sync |
| Lock file (pulled files + hashes) | `eru add`, `eru sync` | No — always read live from disk |
| Remote file tree | `eru source files` only | Never cached — always live |
