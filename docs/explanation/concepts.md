---
title: eru concepts
type: explanation
tags: [architecture, source, collection, manifest, lock-file]
---

# eru concepts

## Overview

eru organises shared knowledge into three levels of granularity: **loose files**, **collections**, and
**manifests**. All three ultimately write files to your local repo and record what was fetched in the lock file.

```
Knowledge sources (remote repos / local paths)
    │  may expose  .eru/manifest.json
    │
    ▼
Consumer config  (.eru/config.json  +  ~/.config/eru/config.json)
    │  collections live here
    │
    ▼
Lock file  (.eru/eru.lock)
    │  one entry per pulled file
    │
    ▼
Local repo files
```

---

## Loose files

A loose file is a single remote file fetched on demand with `eru add`. It is the simplest form of knowledge pull —
no prior configuration required. See [pull files and collections](../how-to/pull-files-and-collections.md) for
the commands.

After a pull, one entry is appended to `.eru/eru.lock` — see the
[lock file reference](../reference/lock-file-and-config.md) for its exact format.

---

## Collections

A collection is a **named, curated list** of remote files stored in config (not in the lock file). Collections
let you pull a related set of files in one command without specifying each path individually.

Collections live in either:
- **Local config** — `.eru/config.json` in the consuming repo
- **Global config** — `~/.config/eru/config.json`

Each file in a collection is fetched and recorded as its own lock entry when pulled. See
[curate a collection](../how-to/curate-a-collection.md) for how to create and manage one.

---

## Manifests

A manifest is published by a **knowledge-source repo** at `.eru/manifest.json`. It declares which files (or glob
patterns) the source makes available, with optional tags and descriptions:

```json
{
  "version": 1,
  "files": [
    { "path": "docs/*.md", "tags": ["adr", "template"], "description": "ADR templates" },
    { "path": "scripts/setup.sh", "tags": ["tooling"] }
  ]
}
```

A manifest is a source-side concern; a collection is a consumer-side concern. The key difference:

| | Manifest | Collection |
|---|---|---|
| Lives in | Source repo at `.eru/manifest.json` | Consumer config (local or global) |
| Who writes it | The knowledge source owner | The consumer |
| Purpose | Advertises what the source offers | Curates a list to pull |

eru fetches and caches the manifest for every configured source:
- **On `eru source add`** — immediately after the source is registered.
- **On every `eru sync`** — at the start of the run, before processing the lock file.

Once cached, manifest-advertised files become available for tag-based pulls (`eru add --tags`),
`eru source files`, and `eru search`. See [publish a manifest](../how-to/publish-a-manifest.md) for how to author
one, and the [lock file reference](../reference/lock-file-and-config.md) for cache locations and freshness.

---

## The lock file

`.eru/eru.lock` is committed in the consuming repo and is the source of truth for what knowledge lives locally and
where it came from. `eru sync` compares each entry's stored hash against the current remote content to detect
drift. See the [lock file reference](../reference/lock-file-and-config.md) for the exact format and resolution
rules.
