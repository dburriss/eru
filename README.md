# eru

`eru` is a CLI tool for sharing knowledge files between projects. Instead of copying files by hand or maintaining a monorepo, you declare where your shared files live (a git repo), pull them in with a single command, and track everything in a lock file so they can stay in sync.

## Features

- **Pull files from git repos** into any project, by path, collection, or tag
- **GitHub / GitLab URL shorthand** — paste a file URL and eru auto-configures the source
- **Lock file** (`eru.lock`) records every pulled file: its origin, path, and content hash
- **Sync** checks all tracked files against their sources and re-fetches anything that has drifted
- **Collections** group related files in a global catalogue so teams can pull curated sets
- **Tag-based pulls** — `--tag devops` fetches everything tagged `devops` across all collections
- **Dry-run mode** on every write command
- **Global config** (`~/.config/eru/config.json`) for sources shared across all your repos

---

## Getting started

### Install

```bash
dotnet tool install --global eru
```

### 1. Initialise your repo

```bash
eru init
```

This creates `eru.json` in the current directory — the local config that lists sources and settings.

### 2. Pull a file

The fastest path is to paste a GitHub or GitLab file URL directly:

```bash
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md
```

That's it. `eru` registers the source automatically, downloads the file, and records it in `eru.lock`.

You can also pull by source name and path once a source is configured:

```bash
eru add knowledge:docs/adr-template.md
```

Or pull an entire curated collection:

```bash
eru add --collection onboarding-docs
```

### 3. Keep files up to date

```bash
eru sync
```

Fetches every file in `eru.lock`, compares content hashes, and overwrites anything that has drifted.

---

## Common commands

| Command | What it does |
|---|---|
| `eru init` | Scaffold `eru.json` in the current directory |
| `eru init --global` | Create the global config at `~/.config/eru/config.json` |
| `eru source add <url>` | Register a git repo as a knowledge source |
| `eru add <path>` | Pull a file by `source:path`, bare filename, or full URL |
| `eru add --collection <name>` | Pull all files in a named collection |
| `eru add --tag <tag>` | Pull all files matching a tag (repeat for AND) |
| `eru add --dryrun <path>` | Preview what would be pulled |
| `eru search <terms>` | Search sources and the lock file |
| `eru sync` | Re-fetch all tracked files and update drifted ones |
| `eru sync --dryrun` | Preview what sync would change |

---

## How it works

### Config

Two config files drive `eru`. They are merged at runtime — local settings win where they overlap.

**`~/.config/eru/config.json`** (global, optional) — shared sources and collections used across all your repos:

```json
{
  "version": 1,
  "defaultSources": [
    {
      "name": "knowledge",
      "url": "https://github.com/my-org/knowledge",
      "branch": "main",
      "basePath": "KNOWLEDGE"
    }
  ],
  "collections": [
    {
      "name": "adr-pack",
      "tags": ["adr"],
      "files": [
        { "source": "knowledge", "remotePath": "KNOWLEDGE/adr/template.md", "tags": [] },
        { "source": "knowledge", "remotePath": "KNOWLEDGE/adr/log.md",      "tags": [] }
      ]
    }
  ]
}
```

**`eru.json`** (local, per-repo) — overrides and project-specific sources:

```json
{
  "version": 1,
  "sources": [
    { "name": "knowledge" }
  ],
  "settings": {
    "stateFile": "eru.lock"
  }
}
```

A local source entry with only a `name` inherits the URL from the matching global source. This lets teams update a URL in one place.

### Pulling a file

When you run `eru add https://github.com/my-org/knowledge/blob/main/KNOWLEDGE/adr/template.md`:

1. The URL is parsed to extract the repo URL (`https://github.com/my-org/knowledge`), branch (`main`), and remote path (`KNOWLEDGE/adr/template.md`).
2. If no source named `knowledge` exists yet, `eru` checks the repo's top-level entries for a `KNOWLEDGE` directory and creates a source with that `basePath` automatically.
3. The file content is fetched via `git` (using `git archive` / blob reads — no full clone required).
4. The content is written to the local path, stripping the source `basePath` prefix so it lands at `adr/template.md`.
5. A content hash is computed and a new entry is appended to `eru.lock`.

### The lock file

`eru.lock` is a plain-text, tab-separated file that you commit alongside your code:

```
# eru.lock v1
adr/template.md	knowledge:KNOWLEDGE/adr/template.md	4a8f2c1d...
adr/log.md	knowledge:KNOWLEDGE/adr/log.md	7b3e9f0a...
```

Each line records three fields: local path, `sourceName:remotePath` origin, and a content hash. This is the source of truth `eru sync` uses to detect drift.

### Syncing

`eru sync` iterates every entry in `eru.lock`:

- **current** — hash matches upstream, nothing to do
- **drifted** — hash differs, file is overwritten and the lock entry is updated
- **missing** — remote file no longer exists
- **skipped** — source is no longer configured

The result is printed per file, followed by a summary count.
