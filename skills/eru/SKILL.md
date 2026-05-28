---
name: eru
description: Use this skill when the user wants to pull, sync, or manage shared knowledge files with eru. Triggers on "eru add", "eru sync", "eru source", "eru collection", "eru manifest", "set up eru", "pull a knowledge file", "add a source to eru", "remove a source from eru", "create a manifest", "verify a manifest", "remove from a collection", or any question about using the eru CLI tool.
---

# eru

`eru` is a CLI tool for sharing knowledge files between projects. Declare where your shared files live (a git repo), pull them in with a single command, and track everything in a lock file so they stay in sync.

## Key concepts

| Concept | Description |
|---|---|
| **Source** | A git repo (or local path) that serves as the canonical origin for shared files |
| **Lock file** | `.eru/eru.lock` — records every pulled file: source, path, and content hash |
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

Pull a file or collection from a knowledge source into the current repo.

```bash
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md
eru add knowledge:docs/adr-template.md
eru add --collection onboarding-docs
eru add --tag devops
eru add knowledge:docs/style-guide.md --dryrun
```

---

### `eru search`

Search across configured knowledge sources and the lock file.

```bash
eru search adr template
eru search --tag devops
eru search pipeline --tag ci --tag devops
```

---

### `eru sync`

Re-fetch every file tracked in `.eru/eru.lock` and overwrite anything that has drifted.

```bash
eru sync
eru sync --dryrun
```

Each file is reported as: **current**, **drifted** (overwritten), **missing** (remote gone), or **skipped** (source not configured).

---

### `eru source`

Manage knowledge sources.

```bash
eru source add https://github.com/my-org/knowledge
eru source add https://github.com/my-org/knowledge --name org-knowledge --branch main --global
eru source list
eru source view knowledge
eru source view knowledge --full
eru source remove knowledge
eru source remove org-knowledge --global
eru source remove knowledge --dryrun
```

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

### `eru mcp`

Start an MCP stdio server exposing `search_knowledge` and `read_artifact` to AI agents.

```bash
eru mcp
```

For client configuration (Claude Code, Claude Desktop, VS Code) see `references/mcp.md`.

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
