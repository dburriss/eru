# eru command reference

Full argument details for all `eru` commands.

## Global flags

| Flag | Description |
|---|---|
| `--debug` | Enable verbose output (shows git progress, etc.) |
| `-o <format>` / `--output <format>` | Output format: `table` (default), `text`, or `json` — available on all commands that produce output |

---

## `eru init`

```
eru init [--force] [--global] [<dir>]
```

| Argument / Flag | Description |
|---|---|
| `<dir>` | Directory in which to create the config (default: current directory) |
| `--force` | Overwrite an existing config |
| `--global` | Create the global config at `~/.config/eru/config.json` |

---

## `eru add`

```
eru add [<remote-path>] [-s <source>] [-c <collection>] [-t <tag>] [-d <target>] [--dryrun] [--global]
```

| Argument / Flag | Description |
|---|---|
| `<remote-path>` | File to pull — bare filename, `source:path`, full GitHub/GitLab URL, or 8-character hash from `eru search`/`eru source view` |
| `-s <source>` | Source name fallback when no `source:` prefix is given |
| `-c <collection>` | Pull all files in a named collection (e.g. `name` or `source:name`) |
| `-t <tag>` | Filter by tag; repeat for multiple tags (AND semantics) |
| `-d <target>` | Local target path — trailing `/` keeps filename and sets directory; no trailing slash uses path verbatim |
| `--dryrun` | Show what would be pulled without writing anything |
| `--global` | Write any auto-created source entry to the global config |

`eru add` searches through all configured sources when resolving a bare filename or hash.

---

## `eru search`

```
eru search [<terms>...] [-t <tag>]
```

| Argument / Flag | Description |
|---|---|
| `<terms>` | Search terms (space-separated, OR semantics, case-insensitive substring match) |
| `-t <tag>` | Filter results by tag; repeat for multiple tags (AND semantics) |

---

## `eru sync`

```
eru sync [--dryrun]
```

| Flag | Description |
|---|---|
| `--dryrun` | Preview what would change without writing anything |

Performs one git clone per source (not per file), refreshes all manifest caches, rebuilds source indexes, and caches collection and lock file content.

---

## `eru remove`

```
eru remove <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or 8-character path short-hash of the artifact to delete (required) |
| `--dryrun` | Show what would be removed without deleting anything |

Deletes the local file and removes its entry from `.eru/eru.lock`.

---

## `eru disconnect`

```
eru disconnect <target> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<target>` | Local path or 8-character path short-hash of the artifact to disconnect (required) |
| `--dryrun` | Show what would be removed without modifying anything |

Removes the lock file entry without touching the local file.

---

## `eru source add`

```
eru source add <url> [-n <name>] [-b <branch>] [-p <basepath>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<url>` | Git URL or local path of the knowledge source (required) |
| `-n <name>` | Override the derived source name |
| `-b <branch>` | Branch to track |
| `-p <basepath>` | Explicitly set the base path, skipping auto-detection |
| `-g` | Write to global config |
| `--dryrun` | Show what would be added without writing anything |

## `eru source remove`

```
eru source remove <name> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to remove (required) |
| `-g` | Remove from global config |
| `--dryrun` | Show what would be removed without writing anything |

## `eru source list`

No arguments. Lists all configured sources (local + global), with tags from the manifest cache.

## `eru source view`

```
eru source view <name> [--full]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source to inspect (required) |
| `--full` | Show all files without the default 20-entry cap |

Files table columns: **Hash** (8-char SHA-256 of the path, usable with `eru add`), **Path**, **Tags**, **Description**.

## `eru source files`

```
eru source files [<name>] [--refresh]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the source. Omit to list files for all configured sources. |
| `--refresh` | Fetch fresh metadata from the source before displaying |

Reads from the source index cache (`~/.cache/eru/sources/<name>/index.json`). No network call unless `--refresh` is passed. Run `eru sync` first if no index has been built.

---

## `eru inbox add`

```
eru inbox add <name> <path> [--raw-path <path>] [--default-channel <channel>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name for the inbox (required) |
| `<path>` | Local filesystem directory to write into — not a configured eru source (required) |
| `--raw-path <path>` | Path within the directory to the raw capture folder (default: `inbox/raw`) |
| `--default-channel <channel>` | Channel `inbox send` falls back to when `-c` is omitted (default: `default`) |
| `-g` | Write to global config |
| `--dryrun` | Show what would be added without writing anything |

## `eru inbox list`

No arguments. Lists all configured inboxes (local + global) with their path, raw path, default channel, registered channels, and scope.

## `eru inbox remove`

```
eru inbox remove <name> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the inbox to remove (required) |
| `-g` | Remove from global config |
| `--dryrun` | Show what would be removed without writing anything |

## `eru inbox channel add`

```
eru inbox channel add <inbox> <channel> [--agent-protocol acp] [--agent-command <cmd>]
                                         [--agent-args <arg> ...] [--agent-instructions <path>]
                                         [-d <description>] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<inbox> <channel>` | Inbox name and channel name (required) |
| `--agent-protocol <protocol>` | Only `acp` is supported; defaults to `acp` if any `--agent-*` flag is given |
| `--agent-command <cmd>` | Executable that launches the channel's agent (e.g. `opencode`); required to configure an agent |
| `--agent-args <arg>` | Argument to pass the agent's command (repeatable) |
| `--agent-instructions <path>` | File (absolute, or relative to the inbox) prepended to every prompt this agent receives, e.g. an `ingestor.md` agent definition. Default: `<inbox>/.agents/agents/ingestor.md`, if it exists |
| `-d <description>` | Short description of the channel |
| `--dryrun` | Show what would be added without writing anything |

A channel need only be registered here if it wants extra config (a description, or an agent for
`eru inbox process` to curate its raw items) — `inbox send -c <channel>` works against any channel name
without prior registration.

Since `inbox send` falls back to the literal channel `default` when no `-c` is given, configuring an
agent on any other channel also wires that same agent onto `default`, unless `default` already has one
of its own — so items sent without `-c` don't silently fall outside every channel `inbox process` knows
to look at.

## `eru inbox channel list`

```
eru inbox channel list <inbox>
```

Lists the registered channels for `<inbox>`.

## `eru inbox channel remove`

```
eru inbox channel remove <inbox> <channel> [--dryrun]
```

## `eru inbox send`

```
eru inbox send [<content>] [-i <inbox>] [-c <channel>] [-t <title>] [-n <note>] [--as message|file|url] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<content>` | Message text, a local file path, or a URL. Reads stdin if omitted (e.g. `pbpaste \| eru inbox send`) |
| `-i <inbox>` | Name of the configured inbox to send into. Auto-resolved when only one inbox is configured |
| `-c <channel>` | Channel within the inbox (default: the inbox's `--default-channel`, or `default`) |
| `-t <title>` | Explicit filename slug, overriding the auto-derived one |
| `-n <note>` | Extra context text folded into the body of a message/url capture |
| `--as` | Force content-type classification: `message`, `file`, or `url` |
| `--dryrun` | Show the resolved target path without writing anything |

Content-type is auto-detected: an `http(s)://` string is a URL capture; an existing local file path is a file capture; anything else is a plain-text message capture. Message and URL captures are written as `.md` with YAML frontmatter (`type: raw`, `resource`, `generated`); a file capture is copied verbatim alongside a `<name>.meta.json` sidecar (`captured_at`, `original_url`). `inbox send` works with only a global config present — no local `.eru/config.json`/`eru init` is required.

## `eru inbox process`

```
eru inbox process [<name>] [-i <inbox>] [-c <channel>] [--all] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Process this specific item instead of the oldest (exact filename or stem) |
| `-i <inbox>` | Inbox to process — auto-resolved when only one is configured |
| `-c <channel>` | Restrict to one channel (default: every channel with an agent configured) |
| `--all` | Process every pending item in scope, oldest first, stopping at the first failure |
| `--dryrun` | Show which item(s)/agent(s) would be used, without spawning anything or moving files |

Run with the top-level `--debug` flag (`eru --debug inbox process ...`) to also include each item's
agent handshake timings (initialize/session/prompt, ms) in the output.

Hands the oldest (or named) raw item to its channel's configured agent over the Agent Client Protocol
to curate, then archives it (`.../raw/<channel>/` → `.../archive/<channel>/`) on success. Requires at
least one channel in scope to have an `agent` configured via `inbox channel add`.

Without `-c`, only agent-having channels are in scope, so a raw item in some other channel is invisible
to it. When that leaves nothing to process, the message says so explicitly (e.g. `"3 item(s) pending in
channel(s) with no agent configured: default (3)."`) rather than implying the inbox is truly empty.

## `eru inbox watch`

```
eru inbox watch [-i <inbox>] [-c <channel>] [--interval <seconds>] [--dryrun]
```

Long-running counterpart to `inbox process`: watches an inbox's raw directory (via a filesystem
watcher plus a polling fallback, default every 30s, overridable per-run with `--interval` or
persistently via `inboxWatchIntervalSeconds` in config — see
[config-file.md](../../../docs/reference/config-file.md)) and automatically runs
`inbox process --all` whenever new items show up, until interrupted (`Ctrl+C`). A failure partway
through a batch is logged but doesn't stop the watch loop — the item stays in `raw/` and is
retried next trigger. This is a foreground command; background/daemonize it yourself if you want
it always running.

---

## `eru collection create`

```
eru collection create <name> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<name>` | Name of the new collection (required) |
| `-t <tag>` | Tag for the collection; repeat for multiple tags |
| `-d <description>` | Short description of the collection |
| `-g` | Write to global config |
| `--dryrun` | Show what would be created without writing anything |

## `eru collection add`

```
eru collection add <collection> -f <source:path> [-t <tag>] [-d <description>] [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection to add to (required) |
| `-f <source:path>` | File reference as `source:remotePath` — e.g. `knowledge:docs/guide.md` (required) |
| `-t <tag>` | Tag for this file reference; repeat for multiple tags |
| `-d <description>` | Short description of the file reference |
| `-g` | Write to global config |
| `--dryrun` | Show what would be added without writing anything |

## `eru collection remove`

```
eru collection remove <collection> -f <source:path> [-g] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<collection>` | Name of the collection (required) |
| `-f <source:path>` | File reference to remove as `source:remotePath` (required) |
| `-g` | Write to global config |
| `--dryrun` | Show what would be removed without writing anything |

If removing the file leaves the collection empty, the collection entry itself is also removed.

---

## `eru manifest init`

```
eru manifest init [--force]
```

| Flag | Description |
|---|---|
| `--force` | Overwrite an existing `.eru/manifest.json` |

## `eru manifest add`

```
eru manifest add <path> [-t <tag>] [-d <description>] [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | File path or gitignore-style glob (e.g. `docs/*.md`, `templates/**/*.yaml`) (required) |
| `-t <tag>` | Tag for the entry; repeat for multiple tags |
| `-d <description>` | Short description of the entry |
| `--dryrun` | Preview without writing |

## `eru manifest remove`

```
eru manifest remove <path> [--dryrun]
```

| Argument / Flag | Description |
|---|---|
| `<path>` | Exact path to remove (required) |
| `--dryrun` | Preview without writing |

## `eru manifest validate`

```
eru manifest validate
```

No arguments. Exits 0 if all entries resolve to at least one local file, 1 otherwise. `verify` is kept as an alias.

---

## `eru okf validate`

```
eru okf validate <path>
```

Walks `<path>` and reports OKF §11 conformance violations (missing/malformed frontmatter, missing `type`, malformed `index.md`/`log.md`). Exits 0 if conformant, 1 otherwise.

---

## `eru cache prune`

```
eru cache prune [--force]
```

| Flag | Description |
|---|---|
| `--force` | Skip the confirmation prompt and delete immediately |

Removes orphaned content files from the cache — files on disk no longer referenced by any source index entry. Safe to run at any time.

---

## `eru cache clear`

```
eru cache clear [--dryrun] [--force]
```

| Flag | Description |
|---|---|
| `--dryrun` | List what would be deleted without deleting anything |
| `--force` | Skip the confirmation prompt and delete immediately |

Deletes the entire local cache (`~/.cache/eru/`) — all source indices, cached content, search index, and collection data. Run `eru sync` after clearing to rebuild.

---

## `eru site generate`

```
eru site generate [-o <dir>] [--open] [--custom-css <path>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the generated site into |
| `--open` | off | Open `index.html` in the default browser after generation |
| `--custom-css <path>` | — | Path to a CSS file copied into the site and loaded after `style.css` |

Generates a self-contained static HTML site for browsing and searching the local knowledge cache. Fully navigable without JavaScript; JS adds in-place search and facet filtering as an optional enhancement.
