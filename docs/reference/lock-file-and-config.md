---
title: Lock file and local path resolution
type: reference
tags: [lock-file, config, paths]
---

# Lock file and local path resolution

## Where files land

The local path for a pulled file is derived from the remote path in two steps:

**Step 1 — Strip the source `BasePath`**

If the source defines a `BasePath` in config (e.g. `"KNOWLEDGE"`), that prefix is removed from the remote path:

```
remote:  KNOWLEDGE/shared/adr.md
result:  shared/adr.md
```

**Step 2 — Apply `--target`**

| `--target` value | Result |
|---|---|
| `docs/` (trailing slash) | Keep the filename, prepend the directory → `docs/adr.md` |
| `docs/custom.md` (has extension) | Use the path verbatim → `docs/custom.md` |
| *(omitted)* | Use the result from step 1 as-is |

All paths are relative to the directory where `eru` is invoked (the repo root).

## When files are pulled

| Command | What is fetched |
|---|---|
| `eru add <path>` | The single remote file, immediately |
| `eru add --collection <name>` | All files in the named collection, immediately |
| `eru add --tags <t>` | All files in any collection matching those tags, immediately |
| `eru source add <url>` | Only the source's `.eru/manifest.json` (no content files) |
| `eru sync` | Re-fetches every file tracked in `.eru/eru.lock`; refreshes all manifest caches; rebuilds source index (`index.json`); caches collection and lock file content |

## The lock file

`.eru/eru.lock` is committed in the consuming repo. It is the source of truth for what knowledge lives locally and
where it came from.

Format — one entry per line, tab-separated. The tags and description fields are optional:

```
# eru.lock v1
<local-path>\t<source-name>:<remote-path>\t<content-hash>[\t<tags>[\t<description>]]
```

| Field | Required | Description |
|---|---|---|
| `<local-path>` | Yes | Path on disk relative to the repo root |
| `<source-name>:<remote-path>` | Yes | Source name and path in the remote repo |
| `<content-hash>` | Yes | `sha256:<hex>` of the file content at pull time |
| `<tags>` | No | Comma-separated tags stored with the entry |
| `<description>` | No | Free-text description stored with the entry |

Example (basic):

```
docs/adr-template.md    my-source:KNOWLEDGE/adr-template.md    sha256:a1b2c3...
```

`eru sync` compares the stored hash against the current remote content to detect drift.

## Manifest cache locations

Cached manifests are stored at:

- macOS/Linux: `~/.cache/eru/sources/<source-name>/manifest.json`
- Windows: `%LOCALAPPDATA%\eru\sources\<source-name>\manifest.json`

After fetching a manifest, `eru sync` also builds a search index at `~/.cache/eru/sources/<source-name>/index.json`.
The index stores per-file tags (merged from manifest entries and file frontmatter) and is the primary data source
for `eru search` and `eru source files`.

See also: [explanation of concepts](../explanation/concepts.md) for how these pieces fit together, and
[data freshness summary](inspecting-state-and-search.md#data-freshness-summary) for how stale each cache can get.
