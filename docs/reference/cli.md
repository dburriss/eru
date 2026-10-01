---
title: eru CLI reference
type: reference
tags: [cli, commands, flags]
---

# eru CLI reference

## Global flags

| Flag | Description |
|---|---|
| `--debug` | Enable verbose output (shows git progress, etc.) |

---

## `eru init`

Scaffold a new eru configuration.

```
eru init [--force] [--global] [<dir>]
```

| Argument / Flag | Description |
|---|---|
| `<dir>` | Directory in which to create the config (default: current directory) |
| `--force` | Overwrite an existing `.eru/config.json` |
| `--global` | Create the global config at `~/.config/eru/config.json` instead |

**Examples**

```bash
eru init                   # create .eru/config.json in the current directory
eru init /path/to/project  # create in a specific directory
eru init --global          # create ~/.config/eru/config.json
eru init --force           # overwrite an existing local config
```

---

## `eru add`

Pull a file or collection from a knowledge source into the current repo and record it in `.eru/eru.lock`.

```
eru add [<remote-path>] [-s <source>] [-c <collection>] [-t <tag>] [-d <target>] [--dryrun] [--global]
```

| Argument / Flag | Description |
|---|---|
| `<remote-path>` | File to pull — bare filename, `source:path`, or a full GitHub/GitLab URL |
| `-s <source>` | Source name fallback when no `source:` prefix is given |
| `-c <collection>` | Pull all files in a named collection (e.g. `name` or `source:name`) |
| `-t <tag>` | Filter by tag; repeat for multiple tags (AND semantics) |
| `-d` / `--target` | Local target path — trailing `/` keeps filename and sets directory; no trailing slash uses path verbatim |
| `--dryrun` | Show what would be pulled without writing anything |
| `--global` | Write any auto-created source entry to the global config |

**Examples**

```bash
# Paste a GitHub URL — source is auto-configured
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md

# Pull by source:path shorthand
eru add knowledge:docs/adr-template.md

# Pull all files in a named collection
eru add --collection onboarding-docs

# Pull everything tagged "devops"
eru add --tag devops

# Preview without writing
eru add knowledge:docs/adr-template.md --dryrun
```

---

## `eru search`

Search across all files eru knows about: manifest-advertised files (from the source index), collection entries, and locally pulled files.

```
eru search [<terms>...] [-t <tag>]
```

| Argument / Flag | Description |
|---|---|
| `<terms>` | Search terms (space-separated) |
| `-t <tag>` | Filter results by tag; repeat for multiple tags |

**Examples**

```bash
eru search adr template
eru search --tag devops
eru search pipeline --tag ci --tag devops
```

---

## `eru sync`

Refresh all source metadata, rebuild the local search index, cache collection content, and update any locally pulled files that have drifted from their upstream source.

```
eru sync [--dryrun]
```

| Flag | Description |
|---|---|
| `--dryrun` | Preview what would change without writing anything |

**Examples**

```bash
eru sync
eru sync --dryrun
```

Each lock file entry is reported as one of: **current**, **drifted** (overwritten), **missing** (remote gone), or **skipped** (source not configured).

`eru sync` also rebuilds `~/.cache/eru/sources/<name>/index.json` for every configured source and pre-caches collection and lock file content for fast offline search.

---

## `eru source`

Manage knowledge sources.

### `eru source add`

Register a git repository or local path as a knowledge source.

```
eru source add <url> [-n <name>] [-b <branch>] [-p <basepath>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<url>` | Git URL or local path of the knowledge source (required) |
| `-n <name>` | Override the derived source name |
| `-b <branch>` | Branch to track |
| `-p <basepath>` | Explicitly set the base path, skipping auto-detection |
| `--branch <branch>` | Remote inbox only: branch `inbox send` pushes to (default: the repo's default branch). Must be the default branch or a branch that doesn't exist yet — an existing non-default branch is refused so it can't be overwritten. |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Remote inboxes.** With a URL, `eru inbox send` shallow-clones the repo, writes the item under `<rawPath>/<channel>/`, commits it (`inbox: add <slug> (<channel>)`) and pushes — one commit per send. It needs `git` and `gh` on `PATH`; GitHub HTTPS URLs authenticate through your `gh` login, other hosts use whatever git is already configured with. Remote inboxes are send-only: `eru inbox process` and `eru inbox watch` reject them (run those in the repo, e.g. with [gh-aw](../how-to/generate-docs-from-inbox-with-gh-aw.md)).

**Examples**

```bash
eru source add https://github.com/my-org/knowledge
eru source add https://github.com/my-org/knowledge -n org-knowledge -b main
eru source add https://github.com/my-org/knowledge -g   # add to global config
```

### `eru source list`

List all configured knowledge sources (merged from global and local config).

```
eru source list
```

### `eru source view`

Show details and available files for a specific source.

```
eru source view <name> [--full]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to inspect (required) |
| `--full` | Show all files without the default 20-entry cap |

**Examples**

```bash
eru source view knowledge
eru source view knowledge --full
```

### `eru source files`

List all files advertised by a source, reading from the local source index. No network call by default.

```
eru source files [<name>] [--refresh]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source. Omit to list files for all configured sources. |
| `--refresh` | Fetch fresh metadata from the source before displaying |

**Examples**

```bash
eru source files                     # all sources, from local index
eru source files knowledge           # one source, from local index
eru source files knowledge --refresh # re-fetch from network, then display
```

If no index has been built yet, run `eru sync` first.

### `eru source bundle add`

Register a bundle (a directory of knowledge within a source) on an existing source.

```
eru source bundle add <source> <path> [-k manifest|okf] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<source>` `<path>` | Source name and bundle path (`.` or `/` for the repo root) |
| `-k` / `--kind` | Bundle kind: `manifest` or `okf`. Auto-detected when omitted |
| `--dryrun` | Preview without writing |

**Kind auto-detection.** A bundle is detected as `okf` only when its root `index.md` frontmatter contains `okf_version`; otherwise it is registered as `manifest`.

If the kind was auto-detected as `manifest` but the bundle has no `.eru/manifest.json`, eru prints a warning, because the bundle would publish no files. The bundle is still registered. To fix it, do one of:

- add `okf_version` to the bundle root `index.md` frontmatter,
- re-run with `--kind okf`, or
- create a manifest (`eru manifest init`).

The warning is not shown when `--kind` is passed explicitly. `eru source add` prints the same warning when it detects a `KNOWLEDGE/` bundle that has neither `okf_version` nor a manifest.

### `eru source remove`

Remove a knowledge source from the config.

```
eru source remove <name> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to remove (required) |
| `-g` | Remove from global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru source remove old-source
eru source remove shared-knowledge -g --dryrun
```

---

## `eru inbox`

Configure inboxes — local filesystem directories `eru inbox send` writes captured messages, files, and
URLs into — and send content into them. An inbox is unrelated to eru's `source` concept: a source is
somewhere eru *pulls from*; an inbox is somewhere eru *writes to*. Because the typical workflow is
running `eru inbox send` while working in some other project, `-g`/`--global` (writing to
`~/.config/eru/config.json`) is the flag you'll reach for most — `inbox send` itself needs no local
`.eru/config.json` and works purely off the global config.

### `eru inbox add`

Register a local directory — or a remote git repo — as an inbox.

```
eru inbox add <name> <path-or-url> [--raw-path <path>] [--default-channel <channel>] [--branch <branch>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name for the inbox (required) |
| `<path-or-url>` | Local filesystem directory to write into, or a git repo URL (`https://…`, `git@…`, `ssh://…`) for a remote inbox (required) |
| `--raw-path <path>` | Path within the directory to the raw capture folder (default: `inbox/raw`) |
| `--default-channel <channel>` | Channel `inbox send` falls back to when `-c` is omitted (default: `default`) |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru inbox add knowledge ~/code/knowledge -g
eru inbox add knowledge https://github.com/acme/knowledge -g   # remote: send commits and pushes
eru inbox add knowledge ~/code/knowledge --default-channel second-brain -g --dryrun
```

### `eru inbox list`

List all configured inboxes (merged from global and local config).

```
eru inbox list
```

### `eru inbox remove`

```
eru inbox remove <name> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the inbox to remove (required) |
| `-g` | Remove from global config |
| `--dryrun` | Preview without writing |

### `eru inbox channel add`

Register a channel on an existing inbox — only needed for a channel that wants extra config (a
description, or an agent for `eru inbox process` to curate its raw items with); sending to an
unregistered channel name works regardless.

```
eru inbox channel add <inbox> <channel> [--agent-protocol acp] [--agent-command <cmd>]
                                         [--agent-args <arg> ...] [--agent-instructions <path>]
                                         [-d <description>] [--dryrun]
```

`--agent-protocol` defaults to `acp` (the only supported value) if any `--agent-*` flag is given.
`--agent-command` is required to configure an agent; `--agent-args` may be repeated.

`--agent-instructions <path>` (absolute, or relative to the inbox's directory) points at a file whose
content is prepended to every prompt sent to this channel's agent — typically an agent/subagent
definition like Claude Code's `ingestor.md`, since the bare raw capture alone tells a generic ACP agent
nothing about how it's expected to curate it. If omitted, `eru inbox process` resolves instructions
through a fallback chain, using whichever tier finds a file first:

1. `<inbox>/.agents/agents/ingestor.md`, if it exists.
2. A tool-specific convention keyed by `--agent-command`'s executable name — e.g. `.claude/agents/ingestor.md`
   for `claude`, `.opencode/agents/ingestor.md` for `opencode`, `.cursor/agents/ingestor.md` for
   `cursor-agent`, `.codex/agents/ingestor.md` for `codex`, `.github/agents/ingestor.agent.md` for
   `copilot` — checked relative to the inbox's directory.
3. Eru's own built-in curation instructions, bundled with eru itself — this tier always succeeds, so
   every channel with an agent configured gets *some* curation instructions even with no `.agents/`,
   `.claude/`, etc. set up in the inbox.

An *explicitly* configured `--agent-instructions <path>` that doesn't resolve to a file **is** an error
(this fallback chain only applies when `--agent-instructions` is omitted).

Since `inbox send` falls back to the literal channel `"default"` whenever no `-c` is given, configuring
an agent on any *other* channel also wires that same agent onto `default` — but only if `default` isn't
already configured with one of its own (never overwrites an explicit choice). This keeps items sent
without `-c` from silently falling outside every channel `inbox process` knows to look at.

**Examples**

```bash
eru inbox channel add knowledge second-brain --agent-command opencode --agent-args acp
```

### `eru inbox channel list`

```
eru inbox channel list <inbox>
```

### `eru inbox channel remove`

```
eru inbox channel remove <inbox> <channel> [--dryrun]
```

### `eru inbox send`

Send a message, a local file, or a URL into a configured inbox.

```
eru inbox send [<content>] [-i <inbox>] [-c <channel>] [-t <title>] [-n <note>] [--as message|file|url] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<content>` | Message text, a local file path, or a URL. Reads stdin if omitted |
| `-i <inbox>` | Inbox to send into — auto-resolved when only one is configured |
| `-c <channel>` | Channel within the inbox (default: the inbox's default channel, or `default`) |
| `-t <title>` | Explicit filename slug, overriding the auto-derived one |
| `-n <note>` | Extra context text folded into a message/url capture's body |
| `--as` | Force content-type classification: `message`, `file`, or `url` |
| `--dryrun` | Preview the resolved target path without writing |

A message or URL capture is written as a `.md` file with YAML frontmatter (`type: raw`, `resource`,
`generated`); a file capture is copied verbatim alongside a `<name>.meta.json` sidecar
(`captured_at`, `original_url`) so an existing file's own content is never modified.

**Examples**

```bash
eru inbox send "ripgrep --hidden still respects .gitignore"
eru inbox send https://example.com/some-article -n "why this matters"
eru inbox send ./notes.md -c second-brain
pbpaste | eru inbox send
eru inbox send "quick note" --dryrun
```

### `eru inbox process`

Curate a raw inbox item via its channel's configured agent, over the [Agent Client
Protocol](https://agentclientprotocol.com), then archive it. Requires at least one channel in scope to
have an agent configured (`eru inbox channel add ... --agent-command <cmd>`).

```
eru [--debug] inbox process [<name>] [-i <inbox>] [-c <channel>] [--all] [--dryrun] [--append <text|@file>]...
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Process this specific item instead of the oldest — matched by exact filename or by filename stem |
| `-i <inbox>` | Inbox to process — auto-resolved when only one is configured |
| `-c <channel>` | Restrict to one channel (default: every channel of the inbox with an agent configured) |
| `--all` | Process every pending item in scope, oldest first, stopping at the first failure |
| `--dryrun` | Show which item(s) and agent(s) would be used, without spawning anything or moving files |
| `--append <text|@file>` | Extra text added to the agent's prompt after the resolved instructions and before the item. Repeatable; pieces are joined in order. A leading `@` reads the value from a file (relative to the current directory), e.g. `@.github/flavours/action.md`; use `@@` for a literal leading `@` |
| top-level `--debug` | Also include each item's agent handshake timings (`initialize`/`session/new`/`prompt`, in ms) in the output |

"Pending" means "still under `<inbox>`'s raw folder" — there's no separate status field. Items across
every channel in scope are pooled and sorted oldest-first by filename (capture filenames are
timestamp-prefixed, so this is also chronological order). On success, the raw item (and its
`.meta.json` sidecar, if any) is moved from `.../raw/<channel>/` to `.../archive/<channel>/`, mirroring
the layout a human-run curation pass already produces by hand.

Without `-c`, only channels with an agent configured are in scope — a raw item sitting in some other
channel (including `default`, which `inbox send` falls back to with no `-c`; see `inbox channel add`'s
auto-wiring behavior above) is invisible to the default scope. When that leaves nothing to process,
the output says so explicitly (e.g. `"3 item(s) pending in channel(s) with no agent configured:
default (3)."`) instead of implying the inbox is genuinely empty.

Each item spawns a brand-new agent process and ACP session — there's no reuse across items in an
`--all` batch. Running with the top-level `--debug` flag (`eru --debug inbox process ...`) breaks
down where that per-item time actually goes (`initialize`, `session/new`, `prompt`), which is mostly
useful for telling fixed handshake overhead apart from the agent's own thinking/tool-use time on a
slow batch.

**Examples**

```bash
eru inbox process --dryrun
eru inbox process
eru inbox process ripgrep-tips -c second-brain
eru inbox process --all
eru inbox process --all --append "Commit one change set per item."
```

The built-in curation instructions say nothing about committing. Use `--append` to add what your
environment needs (commit per item, open a PR, don't commit). An unreadable `@file` fails the run
before any item is processed, including with `--dryrun`.

### `eru inbox watch`

Keeps an inbox under observation and runs the same "process every pending item" logic as
`eru inbox process --all` automatically whenever new items show up, so there's no need to run
`process` by hand after every `inbox send`. It's the long-running counterpart to `inbox process`
— it adds no selection, archiving, or agent-invocation logic of its own.

```
eru [--debug] inbox watch [-i <inbox>] [-c <channel>] [--interval <seconds>] [--dryrun] [--append <text|@file>]...
```

| Argument / Flag | Description |
|---|---|
| `-i <inbox>` | Inbox to watch — auto-resolved when only one is configured |
| `-c <channel>` | Restrict to one channel (default: every channel of the inbox with an agent configured) |
| `--interval <seconds>` | Polling fallback period, in case filesystem events are missed (default: `30`, or `inboxWatchIntervalSeconds` from [config](config-file.md)) |
| `--dryrun` | On every trigger, log what would be processed without spawning an agent or moving files |
| `--append <text|@file>` | Extra text added to every prompt after the resolved instructions and before the item. Repeatable; pieces are joined in order. A leading `@` reads the value from a file (relative to the current directory), e.g. `@.github/flavours/action.md`; use `@@` for a literal leading `@` |

On start, resolves the inbox/channel scope once (failing fast on the same errors `inbox process`
would), then prints what it's watching and blocks. A single `FileSystemWatcher` rooted one level
above the resolved channels' raw directories reacts to new items immediately; a `PeriodicTimer`
polls as a fallback in case filesystem events are missed on some filesystems/OSes. A short
(~500ms) quiet period after the last filesystem event debounces a burst of activity into one
processing pass, so a still-being-written file doesn't get picked up mid-write.

Unlike a bare `inbox process --all` call, a failure partway through a batch is logged but does
**not** stop the watch loop — the failing item stays in `raw/`, is retried on the next trigger,
and watching continues. This is a long-running foreground command; backgrounding or daemonizing
it (a process supervisor, a `launchd`/`systemd` unit, etc.) is left to you. `Ctrl+C` shuts it down
gracefully, letting any in-flight batch finish its current item first.

**Examples**

```bash
eru inbox watch -c eru --interval 10
eru inbox watch --dryrun
eru inbox watch --append @.github/flavours/local.md
```

---

## `eru collection`

Manage collections — curated groups of file references that can be pulled as a unit.

### `eru collection create`

Create a new empty collection.

```
eru collection create <name> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the new collection (required) |
| `-t <tag>` | Tag for the collection; repeat for multiple tags |
| `-d <description>` | Short description of the collection |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru collection create onboarding-docs -d "Files every new engineer needs"
eru collection create adr-pack -t adr -t docs -g
```

### `eru collection add`

Add a file reference to an existing collection.

```
eru collection add <collection> -f <source:remotePath> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection to add to (required) |
| `-f <source:path>` | File reference as `source:remotePath` — e.g. `knowledge:docs/guide.md` (required) |
| `-t <tag>` | Tag for this file reference; repeat for multiple tags |
| `-d <description>` | Short description of the file reference |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru collection add onboarding-docs -f knowledge:docs/adr-template.md
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/template.md -t adr
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/log.md -t adr -g
```

### `eru collection remove`

Remove a file reference from a collection.

```
eru collection remove <collection> -f <source:remotePath> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection (required) |
| `-f <source:path>` | File reference to remove as `source:remotePath` (required) |
| `-g` | Write to global config (`~/.config/eru/config.json`) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru collection remove onboarding-docs -f knowledge:docs/old-guide.md
eru collection remove adr-pack -f knowledge:KNOWLEDGE/adr/template.md --dryrun
```

---

## `eru manifest`

Manage the `.eru/manifest.json` for a knowledge-source repo. The manifest declares which files the source exposes to consumers. Use these commands in the repo that *publishes* knowledge, not the repo that consumes it.

### `eru manifest init`

Create a new empty manifest.

```
eru manifest init [--force]
```

| Flag | Description |
|---|---|
| `--force` | Overwrite an existing `.eru/manifest.json` |

**Examples**

```bash
eru manifest init           # creates .eru/manifest.json with { version: 1, files: [] }
eru manifest init --force   # overwrite an existing manifest
```

### `eru manifest add`

Add a file or glob entry to the manifest.

```
eru manifest add <path> [-t <tag>] [-d <description>] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | File path or gitignore-style glob (e.g. `docs/*.md`, `templates/**/*.yaml`) (required) |
| `-t <tag>` | Tag for the entry; repeat for multiple tags |
| `-d <description>` | Short description of the entry |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru manifest add "README.md" -t meta
eru manifest add "docs/*.md" -t docs -d "All documentation"
eru manifest add "templates/**/*.yaml" -t templates --dryrun
```

### `eru manifest remove`

Remove an entry from the manifest by exact path match.

```
eru manifest remove <path> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Exact path to remove (required) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru manifest remove "README.md"
eru manifest remove "docs/*.md" --dryrun
```

### `eru manifest validate`

Resolve every manifest entry against local files and report any that match nothing. Exits with code 1 if any entries are unresolved. `verify` is kept as an alias.

```
eru manifest validate
```

**Examples**

```bash
eru manifest validate   # exits 0 if all entries resolve, 1 otherwise
eru manifest verify      # alias for the above
```

Glob patterns are expanded against the current directory tree. An entry like `docs/*.md` must match at least one local file to pass.

---

## `eru remove`

Delete a locally pulled file from disk and remove its entry from `.eru/eru.lock`.

```
eru remove <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or path short-hash of the file to remove (required) |
| `--dryrun` | Preview without deleting |

**Examples**

```bash
eru remove docs/adr-template.md
eru remove a1b2c3d4               # remove by short hash
eru remove docs/adr-template.md --dryrun
```

---

## `eru disconnect`

Remove a file's entry from `.eru/eru.lock` without deleting the local file. Use this when you want to keep the file on disk but stop eru tracking it.

```
eru disconnect <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or path short-hash of the file to disconnect (required) |
| `--dryrun` | Preview without writing |

**Examples**

```bash
eru disconnect docs/adr-template.md
eru disconnect a1b2c3d4               # disconnect by short hash
eru disconnect docs/adr-template.md --dryrun
```

---

## `eru browse`

Open an interactive TUI for browsing sources and tracked files.

```
eru browse
```

No arguments.

---

## `eru cache`

Manage the local knowledge cache.

### `eru cache prune`

Remove orphaned content files from the cache — files that exist on disk but are no longer referenced by any source index entry.

```
eru cache prune [--force]
```

| Flag | Description |
|---|---|
| `--force` | Skip the confirmation prompt and delete immediately |

**Examples**

```bash
eru cache prune          # list orphans and prompt before deleting
eru cache prune --force  # delete without prompting
```

Orphans accumulate when files are removed from a manifest or when sources are deleted. `eru cache prune` is safe to run at any time; it only removes files not referenced by the current index.

### `eru cache clear`

Delete the entire local cache — all source indices, cached content, search index, and collection data under `~/.cache/eru/`.

```
eru cache clear [--dryrun] [--force]
```

| Flag | Description |
|---|---|
| `--dryrun` | List what would be deleted without deleting anything |
| `--force` | Skip the confirmation prompt and delete immediately |

**Examples**

```bash
eru cache clear --dryrun    # preview what would be removed
eru cache clear             # prompt before deleting
eru cache clear --force     # delete without prompting
```

Run `eru sync` after clearing to rebuild the cache from all configured sources.

---

## `eru site`

Generate a self-contained static HTML site for browsing and searching the local knowledge cache.

### `eru site generate`

```
eru site generate [-o <dir>] [--open] [--custom-css <path>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the generated site into |
| `--open` | off | Open `index.html` in the default browser after generation |
| `--custom-css <path>` | — | Path to a CSS file copied into the site on every run and loaded after `style.css` |

**Examples**

```bash
# Generate into the default ./cache-site/ directory
eru site generate

# Generate into a custom directory and open the browser
eru site generate -o /tmp/my-site --open

# Apply a custom theme on every run
eru site generate --custom-css ~/themes/company.css
```

The site is fully navigable as plain HTML with no JavaScript. JS adds in-place search and checkbox facet filtering as an optional enhancement. See [site generation reference](site-generation.md) and [customize the generated site](../how-to/customize-the-generated-site.md) for details.

### `eru site serve`

Generate the site and start a local HTTP server with live reload and a full-text search API.

```
eru site serve [-o <dir>] [-p <port>] [--open] [--sync-interval <minutes>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the site into |
| `-p` / `--port` | `5173` | HTTP port to listen on |
| `--open` | off | Open the browser automatically when the server starts |
| `--sync-interval <minutes>` | `15` | Minutes between background cache syncs |

**Examples**

```bash
# Serve on the default port
eru site serve

# Custom port, open browser automatically
eru site serve -p 8080 --open

# Custom output directory with a faster sync interval
eru site serve -o ./docs-site/ --sync-interval 5
```

The server exposes three endpoints in addition to the static site files:

| Endpoint | Description |
|---|---|
| `GET /api/search?q=<terms>` | Full-text search returning JSON `{ hits: [...] }` |
| `GET /api/sync` | Trigger an immediate background sync + site rebuild (returns 202) |
| `GET /api/events` | SSE stream — sends `data: rebuild` after every successful sync |

The browser connects to `/api/events` automatically and reloads the page on each `rebuild` event. Press `Ctrl+C` to stop the server.

---

## `eru okf`

Check a directory tree for conformance with the Open Knowledge Format (OKF) spec.

### `eru okf validate`

Walk a directory tree and report violations of OKF §11 conformance: every concept `.md` file must have parseable YAML frontmatter with a non-empty `type`, and `index.md`/`log.md` must follow the §8/§9 structure where present. Does not flag unknown types, unknown extra keys, broken cross-links, or missing optional fields — those are explicitly permitted by the spec.

The bundle-root `index.md` frontmatter should contain only `okf_version`. That key is what makes eru detect a directory as an OKF bundle (see [`eru source bundle add`](#eru-source-bundle-add)).

**What is not a concept file.** These are skipped, not validated or counted:

- `index.md` and `log.md` (validated against their own rules instead).
- `README.md`, at any depth and case-insensitively. READMEs are human prose, not concepts.
- Anything under a dot-directory (`.git`, `.github`, `.claude`, `.agents`, …). Always skipped, not configurable.
- Paths matching `okfIgnorePatterns` (default `apm_modules/**`, `inbox/**`, `node_modules/**`), read from the local/global config. See the [config file reference](config-file.md). Patterns are relative to `<path>`.

The same rules apply when eru discovers files in a remote OKF bundle.

```
eru okf validate <path>
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Directory to validate (required) |
| `-o` / `--output` | Output format: table (default), text, json |

**Examples**

```bash
eru okf validate ./my-bundle   # exits 0 if conformant, 1 if any violations found
```

### `eru okf init`

Scaffold the OKF index files a bundle is missing. The bundle-root `index.md` is created with `okf_version` frontmatter (which makes eru detect the directory as an OKF bundle); every folder containing concepts gets a catalog `index.md` (a table of concept, type, tags, `stale_after`, plus links to subfolders) with no frontmatter. Existing files are never touched and `log.md` is never created. The same ignore rules as `validate` apply.

```
eru okf init <path> [--dry-run] [-o <format>]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Directory to scaffold (required) |
| `--dry-run` | Report what would be created without writing |
| `-o` / `--output` | Output format: table (default), text, json |

### `eru okf fix`

Repair a directory tree so it passes `eru okf validate`, and create any missing `index.md` files like `init`. Writes happen by default; use `--dry-run` to preview.

| Problem | Repair |
|---|---|
| Root `index.md` has no `okf_version`, or other keys | Frontmatter set to exactly `okf_version` (an existing value is kept) |
| Non-root `index.md` has frontmatter | Frontmatter removed |
| Concept has no frontmatter, or no/empty `type` | `type` added (text edit; other keys and order are preserved) |
| `log.md` date heading is not `YYYY-MM-DD` | Rewritten when the heading parses as a date (invariant culture, so `03/04/2026` is read as March 4) |
| Folder with concepts has no `index.md` | Catalog index created |

Problems that cannot be repaired safely (concept frontmatter that is invalid YAML, `log.md` headings that are not dates) are reported and left untouched; the command then exits 1.

```
eru okf fix <path> [--dry-run] [--default-type <type>] [-o <format>]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Directory to repair (required) |
| `--dry-run` | Report what would change without writing |
| `--default-type` | `type` for concepts that have none (default: `reference`) |
| `-o` / `--output` | Output format: table (default), text, json |

---

## `eru mcp`

Start an MCP stdio server that exposes eru's knowledge search and retrieval capabilities to AI agents.

```
eru mcp
```

No arguments. See the [MCP server reference](mcp-server.md) and [set up the MCP server](../how-to/set-up-the-mcp-server.md) for configuration and tool details.
