# eru concepts

## Overview

eru organises shared knowledge into three levels of granularity: **loose files**, **collections**, and **manifests**. All three ultimately write files to your local repo and record what was fetched in the lock file.

```
Knowledge sources (remote repos / local paths)
    │  may expose  .eru/manifest.json
    │
    ▼
Consumer config  (.eru/config.json  +  ~/.config/eru/config.json)
    │  collections live here
    │
    ▼
Lock file  (eru.lock)
    │  one entry per pulled file
    │
    ▼
Local repo files
```

---

## Loose files

A loose file is a single remote file fetched on demand with `eru add`. It is the simplest form of knowledge pull — no prior configuration required.

```bash
eru add docs/adr-template.md                   # from the highest-priority source
eru add my-source:docs/adr-template.md         # from a specific source
eru add docs/adr-template.md --target docs/    # write to docs/adr-template.md locally
eru add docs/adr-template.md --target adr.md   # write to adr.md locally
```

After a pull, one entry is appended to `eru.lock`:

```
docs/adr-template.md    my-source:docs/adr-template.md    sha256:<hash>
```

---

## Collections

A collection is a **named, curated list** of remote files stored in config (not in the lock file). Collections let you pull a related set of files in one command without specifying each path individually.

Collections live in either:
- **Local config** — `.eru/config.json` in the consuming repo
- **Global config** — `~/.config/eru/config.json`

### Creating and managing collections

```bash
eru collection create my-collection --description "ADR templates"
eru collection add my-collection --source my-source --path docs/adr-template.md --tags adr template
eru collection remove my-collection --path docs/adr-template.md
```

### Pulling a collection

```bash
eru add --collection my-collection             # pull all files in the collection
eru add --collection my-source:my-collection   # restrict to a specific source
eru add --tags adr template                    # pull by tag across all collections
```

Each file in the collection is fetched and recorded as its own `LockEntry` in `eru.lock`.

---

## Manifests

A manifest is published by a **knowledge-source repo** at `.eru/manifest.json`. It declares which files (or glob patterns) the source makes available, with optional tags and descriptions.

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

### Managing a manifest (in a source repo)

```bash
eru manifest init                                          # create .eru/manifest.json
eru manifest add docs/adr-template.md --tags adr template  # add a path or glob
eru manifest remove docs/adr-template.md                   # remove an entry
eru manifest verify                                        # check all globs resolve to real files
```

### How manifests are fetched

eru fetches and caches the manifest for every configured source:
- **On `eru source add`** — immediately after the source is registered.
- **On every `eru sync`** — at the start of the run, before processing the lock file.

Cached manifests are stored at:
- macOS/Linux: `~/.cache/eru/sources/<source-name>/manifest.json`
- Windows: `%LOCALAPPDATA%\eru\sources\<source-name>\manifest.json`

Once cached, manifest-advertised files become available for tag-based pulls (`eru add --tags`) and `eru source files`.

---

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

---

## When files are pulled

| Command | What is fetched |
|---|---|
| `eru add <path>` | The single remote file, immediately |
| `eru add --collection <name>` | All files in the named collection, immediately |
| `eru add --tags <t>` | All files in any collection matching those tags, immediately |
| `eru source add <url>` | Only the source's `.eru/manifest.json` (no content files) |
| `eru sync` | Re-fetches every file tracked in `eru.lock`; also refreshes all manifest caches |

---

## The lock file

`eru.lock` is committed in the consuming repo. It is the source of truth for what knowledge lives locally and where it came from.

Format — one entry per line, tab-separated:

```
# eru.lock v1
<local-path>\t<source-name>:<remote-path>\t<content-hash>
```

Example:

```
docs/adr-template.md    my-source:KNOWLEDGE/adr-template.md    sha256:a1b2c3...
```

`eru sync` compares the stored hash against the current remote content to detect drift.
