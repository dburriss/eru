---
status: done
---

# Plan: Unified cache and search index

## Context

Several existing plans (`search.md`, `mcp-search-index.md`, `collection-cache.md`) solved immediate problems independently. The result is an inconsistent state:

- **CLI `eru search`** reads collections from config and the lock file only — ignores the manifest cache, ignores frontmatter
- **MCP `search_knowledge`** reads frontmatter live on every call — slow, only works for files already on disk
- **`eru source files`** is the only read command that hits the network — an awkward exception
- **Loose files** (added via `eru add`) are invisible to search unless already pulled; no tags or description
- **Manifest cache** has file metadata (paths, tags, descriptions) but search doesn't use it
- **Frontmatter tags** are parsed at MCP search time, not stored anywhere persistent

The goal of this plan is a single coherent model: **sync populates a local index; all read commands query that index**. No read command requires network access. Search is consistent between CLI and MCP.

---

## Principles

1. **Sync is the only network moment** for metadata and content. Everything else reads from disk.
2. **The cache is the index** — manifest metadata, file content, and frontmatter tags all land in the cache during sync.
3. **Loose files are first-class** — lock file entries get optional `Tags` and `Description` fields so they are searchable on equal footing with collection files.
4. **Collection membership is never stored in the index** — it is derived at search time by joining against config (which is always current).
5. **Tags are merged from four sources**: manifest entry, collection config entry, file frontmatter, and lock file user-supplied tags.

---

## Cache layout

```
~/.cache/eru/
  sources/
    <name>/
      manifest.json              ← existing: source manifest
      index.json                 ← NEW: keyed metadata index
      files/
        <content-hash>           ← NEW: raw cached file content
  content/
    <file-hash>.json             ← existing: FileWordIndex for MCP full-text search
```

The `sources/<name>/` directory is the unit of invalidation: syncing one source only rewrites that source's directory. Removing a source means deleting its directory.

---

## Metadata index format

`~/.cache/eru/sources/<name>/index.json`

A JSON object keyed by `remotePath` (the path within the source repo, after any `BasePath` strip). `SourceName` is implicit from which file is being read.

```fsharp
// key: remotePath
type IndexEntry = {
    Tags         : string list
    Description  : string option
    LocalPath    : string option    // set if the file is in eru.lock (i.e. in the repo)
    CacheRelPath : string option    // relative path under sources/<name>/files/, set if content cached
    ContentHash  : string option    // sha256:<hash> of cached content, for invalidation
}
```

Example:

```json
{
  "docs/adr-template.md": {
    "tags": ["adr", "template", "architecture"],
    "description": "ADR template with context and decision sections",
    "localPath": "docs/adr-template.md",
    "cacheRelPath": "files/sha256abc123",
    "contentHash": "sha256:abc123..."
  }
}
```

---

## Tag sources and merge rules

At sync time, for each file, tags are merged from all available sources:

| Source | How obtained | When present |
|---|---|---|
| Manifest entry tags | `ManifestFileRef.Tags` from `manifest.json` | Source has a manifest with this file |
| Collection entry tags | `CollectionFileRef.Tags` from config | Consumer has added this file to a collection |
| Frontmatter tags | `Frontmatter.parse` on cached content | File is content-cached (collection or lock entry); not available for manifest-only files |
| Lock file tags | `LockEntry.Tags` (new field) | File was added with `eru add --tags` |

Final `IndexEntry.Tags` = union of all available sources, deduplicated, lowercase. Manifest-only files (not in any collection or lock) have tags from the manifest entry only — no frontmatter tags until the file is pulled.

Description precedence (first non-empty wins): collection config → manifest entry → frontmatter.

Collection entry tags are NOT stored in the index — they are joined at search time from config. The index stores only manifest-derived and frontmatter-derived tags. This avoids the index going stale when a collection entry is edited.

At search time, tags from the index and tags from matching collection config entries are merged for the result.

---

## Lock file changes

Add two optional fields to `LockEntry` to support tagged loose files:

**Current format:**
```
<localPath>\t<sourceName>:<remotePath>\t<contentHash>
```

**New format:**
```
<localPath>\t<sourceName>:<remotePath>\t<contentHash>[\t<tags>[\t<description>]]
```

- `<tags>` — comma-separated, omitted if empty
- `<description>` — free text, omitted if empty
- Both fields are optional; existing lock files parse without change

```fsharp
type LockEntry = {
    LocalPath   : string
    SourceName  : string
    RemotePath  : string
    ContentHash : string
    Tags        : string list      // NEW — empty list if not present
    Description : string option    // NEW — None if not present
}
```

`eru add --tags foo bar --description "ADR template"` writes these fields for loose files. Files already covered by a manifest or collection entry do not need tags on the lock entry (tags come from the index/config at search time).

---

## Sync behaviour (new)

Sync currently re-fetches only files in the lock file. The new behaviour has two tiers:

- **Manifest files** — metadata only (path, tags, description from `manifest.json`). Content is not fetched. Manifest entries are discoverable in search by path/tags/description but are not full-text searchable until pulled into a collection or the repo.
- **Collection files and lock file entries** — content is fetched and cached. These are full-text searchable via `FileWordIndex`.

This keeps sync fast regardless of manifest size. Collections are the explicit opt-in for content caching. If you want a manifest-advertised file full-text searchable, add it to a collection.

**Steps:**

1. For each configured source:
   a. Fetch and cache `manifest.json` (existing)
   b. Rebuild `sources/<name>/index.json` from scratch using manifest metadata (path, tags, description). No content fetch. Stale index entries from removed manifest files vanish automatically.
2. For each collection file ref in config: fetch content, write to `sources/<name>/files/<contentHash>`, parse frontmatter, merge frontmatter tags into the index entry. Create the index entry if the file is not in the manifest.
3. For each lock file entry not covered by a manifest or collection: fetch content from source, cache it, update the source's index with `LocalPath`.
4. After all fetches, set `LocalPath` on index entries that appear in the lock file.
5. Build/update `FileWordIndex` entries in `~/.cache/eru/content/` for all newly cached content files (pre-warms MCP full-text search).

On fetch error: log to stderr, leave existing cached file and index entry in place (serve stale).

---

## Search behaviour (new)

### CLI `eru search`

Replaces the current config + lock file join with an index-based query:

1. For each source, read `sources/<name>/index.json`
2. Read config to get collection membership and collection-side tags
3. Read lock file for `LocalPath` (lock file is always current; `LocalPath` in index may be stale between syncs)
4. Merge: for each index entry, union index tags with matching collection config tags; set `LocalPath` from lock file lookup
5. Apply query filters (terms, tags)
6. Return results

### MCP `search_knowledge`

Replaces live `readFrontmatter` calls:

1. Build candidate list from index entries (all sources) + lock file orphans
2. Tags and description come from the index — no live file reads for metadata
3. Full-text search still uses `FileWordIndex` (existing, unchanged)
4. `CandidateFile.Tags` and `.Description` populated from index, not from live frontmatter

The `readFrontmatter` call in `McpTools.fs` is removed. Frontmatter tags are in the index after sync.

---

## `eru source files` change

Currently the only read command that hits the network (live `git ls-tree`). Change to:

- Default: read from `sources/<name>/index.json` (cache) — no network
- `--refresh` flag: trigger a manifest + content re-fetch for that source, then display

This removes the "only command that uses the network" exception and makes all read commands uniform.

---

## Files to create / modify

| File | Change |
|---|---|
| `src/Eru.Domain/LockFile.fs` | Add `Tags` and `Description` to `LockEntry`; update parse and write |
| `src/Eru.Adapters/Paths.fs` | Add `sourceFilesDir(sourceName)` → `~/.cache/eru/sources/<name>/files/` |
| `src/Eru.Adapters/SourceIndexAdapter.fs` | NEW — read/write `sources/<name>/index.json`; `IndexEntry` type |
| `src/Eru.Domain/Sync.fs` | Extend sync to fetch all manifest + collection + lock-orphan files into cache; parse frontmatter; write index |
| `src/Eru.Domain/Search.fs` | Replace config+lock join with index-based query; merge collection config tags at search time |
| `src/Eru.Mcp/McpTools.fs` | Remove live `readFrontmatter` calls; populate `CandidateFile` from index |
| `src/Eru.Mcp/CollectionCacheService.fs` | Replace per-source fetch logic with a call to the shared sync logic; becomes "sync on a timer" |
| `src/Eru.Cli/SourceFilesCli.fs` | Change to read from index by default; add `--refresh` flag |
| `src/Eru.Adapters/Eru.Adapters.fsproj` | Add `SourceIndexAdapter.fs` |
| `src/Eru.Cli/CacheCli.fs` | NEW — `eru cache prune` command; scans for content files not referenced by any index entry, prints list, prompts confirmation before deleting |

---

## Decisions

1. **CollectionCacheService** — kept, not retired. Long-running MCP sessions (e.g. Claude Desktop) need automatic cache refresh without the user running `eru sync`. The service is refactored to call the shared sync logic rather than owning its own fetch path. It becomes "sync on a timer" — one code path, automatic freshness.

2. **Sync scope** — manifest files are metadata-only (no content fetch). Content is only fetched for collection entries and lock file entries. No `--metadata-only` flag needed; collections are the explicit scoping mechanism. If you want a manifest-advertised file full-text searchable, add it to a collection.

3. **Cache eviction** — sync rebuilds `sources/<name>/index.json` from scratch on each run; stale index entries (files removed from the manifest) vanish automatically. Orphaned content files in `sources/<name>/files/` accumulate but are not deleted by sync. A new `eru cache prune` command scans for content files not referenced by any current index entry, shows what it will delete, and requires confirmation before acting.

---

## Verification

```bash
dotnet build
dotnet test

# After eru sync, confirm index files exist
ls ~/.cache/eru/sources/

# CLI search uses index (no network)
eru search adr
eru search --tags template

# MCP search returns tags from frontmatter (not live file read)
# Run eru mcp and call search_knowledge, verify tags match file frontmatter

# Loose file with tags
eru add docs/my-note.md --tags local notes
eru search --tags notes    # should find it

# source files reads from cache
eru source files my-source            # no network
eru source files my-source --refresh  # hits network, updates cache

# cache prune: shows orphaned content files and prompts before deleting
eru cache prune
```
