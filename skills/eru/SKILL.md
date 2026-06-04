---
name: eru
description: Use this skill when the user wants to pull, sync, or manage shared knowledge files with eru. Triggers on "eru add", "eru sync", "eru source", "eru collection", "eru manifest", "eru cache", "eru site", "eru remove", "eru disconnect", "set up eru", "pull a knowledge file", "add a source to eru", "remove a source from eru", "create a manifest", "verify a manifest", "remove from a collection", "prune the cache", "clear the cache", "generate a site", "list source files", or any question about using the eru CLI tool.
---

# eru

`eru` is a CLI tool for sharing knowledge files between projects. Declare where your shared files live (a git repo), pull them in with a single command, and track everything in a lock file so they stay in sync.

## Key concepts

| Concept | Description |
|---|---|
| **Source** | A git repo (or local path) that serves as the canonical origin for shared files |
| **Lock file** | `.eru/eru.lock` — records every pulled file: source, path, and content hash |
| **Source index** | `~/.cache/eru/sources/<name>/index.json` — per-file tags and descriptions merged from manifest + frontmatter; rebuilt by `eru sync` |
| **Collection** | A named group of file references that can be pulled as a unit |
| **Manifest** | `.eru/manifest.json` — published by a knowledge-source repo to declare available files |
| **Tags** | Metadata on files/collections for filtered pulls |
| **Global config** | `~/.config/eru/config.json` — sources and collections shared across all repos |
| **Local config** | `.eru/config.json` — per-repo configuration |

For full argument details see `references/commands.md`.

---

## Commands

### `eru init`

Scaffold a new eru configuration.

```bash
eru init                   # create .eru/config.json in current directory
eru init --global          # create ~/.config/eru/config.json
eru init --force           # overwrite existing config
```

---

### `eru add`

Pull a file or collection from a knowledge source into the current repo. Accepts a bare filename, `source:path` shorthand, a full GitHub/GitLab URL, or an 8-character content hash from `eru source view` or `eru search`.

```bash
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md
eru add knowledge:docs/adr-template.md
eru add a1b2c3d4                           # add by hash
eru add --collection onboarding-docs
eru add --tag devops
eru add knowledge:docs/style-guide.md --dryrun
eru add knowledge:docs/adr.md --target docs/          # write to docs/adr.md
eru add knowledge:docs/adr.md --target docs/custom.md # write to docs/custom.md
```

`--target` semantics: a path ending with `/` is treated as a directory (filename is kept); a path without a trailing slash is used verbatim as the local file path.

---

### `eru search`

Search across all files eru knows about: source index entries, collection entries, and locally pulled files.

```bash
eru search adr template
eru search --tag devops
eru search pipeline --tag ci --tag devops
```

---

### `eru sync`

Re-fetch every file tracked in `.eru/eru.lock`, refresh all source manifests, and rebuild the source index and collection cache. Batches git fetches — one clone per source regardless of how many files are tracked.

```bash
eru sync
eru sync --dryrun
```

Each file is reported as: **current**, **drifted** (overwritten), **missing** (remote gone), or **skipped** (source not configured).

---

### `eru remove`

Delete a local artifact file and its lock entry by path.

```bash
eru remove docs/adr-template.md
eru remove docs/adr-template.md --dryrun
```

---

### `eru disconnect`

Remove a tracked artifact from the lock file without deleting the local file.

```bash
eru disconnect docs/adr-template.md
eru disconnect a1b2c3d4               # disconnect by short hash
eru disconnect docs/adr-template.md --dryrun
```

---

### `eru source`

Manage knowledge sources.

```bash
eru source add https://github.com/my-org/knowledge
eru source add https://github.com/my-org/knowledge --name org-knowledge --branch main --global
eru source list
eru source view knowledge
eru source view knowledge --full
eru source files                          # all sources, from local index
eru source files knowledge               # one source, from local index
eru source files knowledge --refresh     # re-fetch from network, then display
eru source remove knowledge
eru source remove org-knowledge --global
eru source remove knowledge --dryrun
```

`eru source files` reads from the source index cache — no network call unless `--refresh` is passed. Run `eru sync` first if no index has been built.

---

### `eru collection`

Manage collections — curated groups of file references.

```bash
eru collection create onboarding-docs --description "Files every new engineer needs"
eru collection create adr-pack --tag adr --tag docs --global
eru collection add onboarding-docs --file knowledge:docs/adr-template.md
eru collection add adr-pack --file knowledge:KNOWLEDGE/adr/template.md --tag adr --global
eru collection remove onboarding-docs --file knowledge:docs/adr-template.md
eru collection remove adr-pack --file knowledge:KNOWLEDGE/adr/template.md --global
eru collection remove onboarding-docs --file knowledge:docs/old.md --dryrun
```

Removing the last file from a collection also removes the collection entry itself.

---

### `eru manifest`

Manage `.eru/manifest.json` in a knowledge-source repo. Use in the repo that *publishes* knowledge, not the one that consumes it.

```bash
eru manifest init
eru manifest add "docs/*.md" --tag docs --description "All documentation"
eru manifest add "README.md" --tag meta
eru manifest verify           # exits 1 if any entry resolves to no local files
eru manifest remove "README.md"
eru manifest init --force     # overwrite existing manifest
```

Paths support gitignore-style globs. `verify` expands each entry against local files.

---

### `eru cache`

Manage the local knowledge cache.

```bash
eru cache prune          # list orphaned cache files and prompt before deleting
eru cache prune --force  # delete without prompting

eru cache clear          # delete entire cache (~/.cache/eru/) with confirmation prompt
eru cache clear --force  # delete without prompting
eru cache clear --dryrun # preview what would be removed
```

Orphans accumulate when files are removed from a manifest or when sources are deleted. Run `eru sync` after `eru cache clear` to rebuild from all configured sources.

---

### `eru site`

Generate a self-contained static HTML site for browsing and searching the local knowledge cache.

```bash
eru site generate                          # generate into ./cache-site/
eru site generate -o /tmp/my-site --open  # write to custom dir and open in browser
eru site generate --custom-css ~/theme.css # apply a custom stylesheet on every run
```

The site is fully navigable as plain HTML with no JavaScript. JS adds in-place search and checkbox facet filtering as an optional enhancement.

---

### `eru mcp`

Start an MCP stdio server exposing `search_knowledge`, `read_artifact`, and `refresh_knowledge` tools plus MCP resources to AI agents.

```bash
eru mcp
```

For client configuration (Claude Code, Claude Desktop, VS Code) see `references/mcp.md`.

---

## Output format

All commands that produce output accept `--output` (short: `-o`):

| Value | Description |
|---|---|
| `table` | Formatted Spectre.Console table with column headers (default) |
| `text` | Plain-text style for piping and terminal use |
| `json` | Machine-readable JSON for scripting |

---

## Common workflows

### Publish a manifest (knowledge-source repo)

```bash
eru manifest init
eru manifest add "docs/*.md" --tag docs
eru manifest add "templates/**/*.yaml" --tag templates
eru manifest verify
```

### New consumer repo setup

```bash
eru init
eru source add https://github.com/my-org/knowledge --global
eru add knowledge:docs/adr-template.md
```

### Pull by URL (zero config)

```bash
eru add https://github.com/my-org/knowledge/blob/main/docs/guide.md
# eru auto-registers the source and records the file in .eru/eru.lock
```

### Pull by hash

```bash
eru search adr            # find the hash in results
eru add a1b2c3d4          # pull by 8-char hash
```

### Pull an entire collection

```bash
eru add --collection onboarding-docs
```

### Keep files up to date

```bash
eru sync
```

### Preview before committing

```bash
eru add knowledge:docs/style-guide.md --dryrun
eru sync --dryrun
```

### Inspect what's available without pulling

```bash
eru source list
eru source files knowledge
eru search adr --tag template
```

### Remove a file from tracking

```bash
eru remove docs/old-guide.md          # delete file and lock entry
eru disconnect docs/old-guide.md      # remove from lock only, keep file
```

### Clean up stale cache entries

```bash
eru cache prune           # remove orphaned files only
eru cache clear --force   # wipe the entire cache, then run eru sync to rebuild
```
