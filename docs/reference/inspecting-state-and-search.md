---
title: Inspecting state and searching
type: reference
tags: [search, source, cache, freshness]
---

# Inspecting state and searching

Four commands let you read what eru knows without pulling any files: `source list`, `source view`, `source files`, and `search`. They differ in where they read from and whether they touch the network.

| Command | Network? | Data sources |
|---|---|---|
| `eru source list` | No | Local + global config, manifest cache |
| `eru source view <name>` | No | Local + global config, manifest cache |
| `eru source files [name]` | No | Source index cache (`index.json`) |
| `eru source files [name] --refresh` | **Yes** | Re-fetches from network, then reads index |
| `eru search` | No | Source index cache, config collections, lock file |
| `eru print` | Only if not cached | Local file, then source cache, then live fetch |

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
With terms, files are **ranked by content**, best first. With no terms, the known files are listed.

```bash
eru search adr template                 # ranked: files about adr and templates first
eru search retries for AWS calls        # natural-language terms work; rarer words count more
eru search adr --limit 5                # the best five files
eru search "retry failed requests" --mode hybrid   # keywords and meaning together
eru search --tags template              # no terms: list files carrying the tag
eru search adr --tags template          # ranked, but only files tagged template
```

| Flag | Description |
|---|---|
| `--tags`, `-t` | Only files carrying every tag (AND, case-insensitive). Repeat for several. |
| `--limit <n>` | Maximum files returned (default 15). A note on stderr says when more matched. |
| `--mode <lex\|sem\|hybrid>` | `lex` ranks by keywords (default), `sem` by meaning, `hybrid` fuses both. Overrides `search.mode`. |
| `--output`, `-o` | `table` (default), `text` or `json`. |

**What is shown:**

| Column | What it is |
|---|---|
| Score | Relevance within this result (blank for files matched on metadata only) |
| Hash | 8-character SHA-256 of the remote path |
| Path | `source:remotePath` for source files, the repo-relative path otherwise |
| Tags | Merged tags from the source index and collection config |
| Local Path | Where the file lives locally, if it has been pulled (blank otherwise) |
| Description | Description from the source index or collection config |

`--output text` also prints up to three matching lines under each file. `--output json` is an array with
`path`, `sourceName`, `remotePath`, `hash`, `tags`, `description`, `localPath`, `score` and `excerpts`.

**How files are ranked:** BM25 over the file content. Words are lowercased, stop words are dropped and word
forms are matched (`mock` finds `mocked`). Terms are combined with OR, so files matching more and rarer terms
rise to the top. Terms in the path, a heading or the front matter `title`, `description` and `tags` count extra.
The ranking is done by [Ferret](https://github.com/dburriss/ferret); its indexes live in `~/.cache/eru/ferret`
and are rebuilt on demand (`eru cache clear` removes them).

`sem` and `hybrid` embed the files with a small local model (`bge-small-en-v1.5`, about 33 MB) and need no
service. If the model is not installed, `eru search` answers with `lex`, then downloads the model so the next
search is semantic; set `FERRET_OFFLINE=1` to skip the download, `FERRET_MODEL_DIR` to use a copy you already
have, or `FERRET_MODEL_URL` to download from a mirror.

**Where the files come from:**

1. **Source index** (`~/.cache/eru/sources/<name>/index.json`) — all files advertised by each source, with tags merged from manifest entries and file frontmatter. This is the primary data source after `eru sync` has run.
2. **Lock file** (`.eru/eru.lock`) — pulled files, and any lock-only entries not covered by the index.
3. **Local `knowledge/` directory** in the current directory.

Entries with no readable file (advertised but never fetched) cannot be ranked by content; they are matched on
path and description and listed after the ranked files. Files matching `search.ignorePatterns` (default
`index.md` and `log.md`) are skipped. Run `eru sync` to refresh the source index so newly advertised files appear.

---

## Data freshness summary

| Data | Refreshed by | Can be stale? |
|---|---|---|
| Source config (URL, branch, etc.) | `eru source add` / manual edit | No — always read live from disk |
| Manifest cache (tags, patterns, descriptions) | `eru sync`, `eru source add` | Yes — until next sync |
| Source index (`index.json`) | `eru sync`, `eru source files --refresh` | Yes — until next sync |
| Lock file (pulled files + hashes) | `eru add`, `eru sync` | No — always read live from disk |
