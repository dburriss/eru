# Inspecting state and searching

Four commands let you read what eru knows without pulling any files: `source list`, `source view`, `source files`, and `search`. They differ in where they read from and whether they touch the network.

| Command | Network? | Data sources |
|---|---|---|
| `eru source list` | No | Local + global config, manifest cache |
| `eru source view <name>` | No | Local + global config, manifest cache |
| `eru source files [name]` | No | Source index cache (`index.json`) |
| `eru source files [name] --refresh` | **Yes** | Re-fetches from network, then reads index |
| `eru search` | No | Source index cache, config collections, lock file |

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

Lists all files advertised by a source, reading from the local source index. No network call is made by default.

```bash
eru source files                     # all configured sources
eru source files <name>              # a specific source
eru source files <name> --refresh    # re-fetch from network first, then display
```

**What is shown:**

| Column | What it is |
|---|---|
| Source | Source name |
| Hash | 8-character SHA-256 of the file path |
| Path | File path as recorded in the source index |
| Tags | Tags from the source index (merged from manifest + file frontmatter) |
| Description | Description from the source index |

**Where the data comes from:**

- Default: source index cache at `~/.cache/eru/sources/<name>/index.json`. Rebuilt by `eru sync` and by `--refresh`.
- `--refresh`: triggers a full re-fetch from the network (same as running `eru sync` for that source), then reads the freshly updated index.

If no index has been built for a source, `eru source files` returns an error asking you to run `eru sync`.

---

## `eru search`

Searches all files eru knows about: manifest-advertised files, collection entries, and locally pulled files.

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
| Tags | Merged tags from the source index and collection config |
| Local Path | Where the file lives locally, if it has been pulled (blank otherwise) |
| Description | Description from the source index or collection config |

**Where the data comes from (priority order):**

1. **Source index** (`~/.cache/eru/sources/<name>/index.json`) — all files advertised by each source, with tags merged from manifest entries and file frontmatter. This is the primary data source after `eru sync` has run.
2. **Config collections** — files declared in collection configs but not yet in the index (e.g. before the first sync). Collection-level tags are always joined in at search time.
3. **Lock file** (`eru.lock`) — used to populate `Local Path` for pulled files, and to surface any lock-only entries not covered by the index or collections.

**Filtering:**

- Terms (positional args): OR semantics, case-insensitive substring match against remote path, local path, and description. Omit to match everything.
- `--tags`: AND semantics, case-insensitive exact match. All specified tags must be present.

Run `eru sync` to refresh the source index so that newly manifest-advertised files appear in search results.

---

## Data freshness summary

| Data | Refreshed by | Can be stale? |
|---|---|---|
| Source config (URL, branch, etc.) | `eru source add` / manual edit | No — always read live from disk |
| Manifest cache (tags, patterns, descriptions) | `eru sync`, `eru source add` | Yes — until next sync |
| Source index (`index.json`) | `eru sync`, `eru source files --refresh` | Yes — until next sync |
| Lock file (pulled files + hashes) | `eru add`, `eru sync` | No — always read live from disk |
