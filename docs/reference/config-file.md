---
title: Config file
type: reference
tags: [config, sources, collections, sections, settings]
---

# Config file

eru reads two JSON config files and merges them into a single **effective config** for each run. This page
documents both formats, every field, and the merge/precedence rules. For the lock file format and how local
paths are derived, see [lock file and local path resolution](lock-file-and-config.md).

## The two config files

| | Local config | Global config |
|---|---|---|
| Path | `.eru/config.json` (in the consuming repo) | `~/.config/eru/config.json` (Linux/macOS, or `$XDG_CONFIG_HOME/eru/config.json`); `%APPDATA%\eru\config.json` on Windows |
| Scope | One repo | Shared across all repos on the machine |
| Created by | `eru init` | `eru init --global` |
| Holds | Repo-specific sources, collections, inboxes and settings | Default sources, shared collections, inboxes and fallback settings |

Both are optional — eru runs with sensible built-in defaults if neither exists. Where both are present, fields
are merged as described in [Merge and precedence](#merge-and-precedence) below.

## Top-level shape

Both files have **the same shape**. Settings are grouped into top-level sections; the local file overrides the
global file per key (see [Merge and precedence](#merge-and-precedence)).

```json
{
  "version": 3,
  "site":  { "ignorePatterns": [], "hideEmptyBundles": false },
  "okf":   { "ignorePatterns": [] },
  "mcp":   { "refreshIntervalMinutes": 60 },
  "sync":  { "branch": "main", "commitOnPull": false, "stateFile": "eru.lock" },
  "files": { "blockPatterns": [], "allowPatterns": [], "allowBinaries": false },
  "inbox": { "default": "team", "watchIntervalSeconds": 30, "items": { /* { [name]: InboxConfig } */ } },
  "source": { "items": [ /* SourceConfig[] */ ] },
  "collection": { "items": [ /* CollectionConfig[] */ ] }
}
```

Every section is optional, and so is every key inside one. Section names are singular, and the entities a
section holds (sources, inboxes, collections) always live under `items`, so a setting can later be added to any
section without changing the file shape. There are no `settings`, `defaults`, `sources`, `defaultSources`,
`inboxes`, `defaultInboxes` or `collections` top-level keys any more.

`version` is required in both files. eru currently supports version `3`; a config declaring a higher version
fails to load with `Unsupported {global|local} config version {n} — please upgrade eru`. Older files (versions
1 and 2) are migrated automatically when read — see [Upgrading from earlier versions](#upgrading-from-earlier-versions).

## `SourceConfig`

Describes one knowledge source (a remote repo, or a reference to one defined globally). Held in `source.items`.

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Name` | `name` | Yes | Unique identifier for the source. Used in `<source>:<path>` references (collections, lock file, CLI args). |
| `Url` | `url` | Conditional | Git URL of the source repo. Required in the global config. In the local config, may be omitted if a source of the same `name` exists in the global config (see [source resolution](#source-resolution)). |
| `Branch` | `branch` | No | Branch/ref to fetch from. Falls back to the source's default branch if omitted. |
| `Bundles` | `bundles` | No | Directories the source publishes from, each `{ "path": "...", "kind": "manifest" \| "okf" }` (`path: ""` is the repo root). Manage them with `eru source bundle add/remove`. |

```json
{ "name": "team-knowledge", "url": "https://github.com/my-org/knowledge.git", "branch": "main", "bundles": [ { "path": "KNOWLEDGE", "kind": "manifest" } ] }
```

### Source resolution

A local `source.items` entry may omit `url` to reuse a source already defined in the global config under the same
`name` — the global entry's `url`/`branch`/`bundles` are used as-is. If a local entry has no `url` and no
matching global source exists, config loading fails with `Local source '{name}' has no URL and was not found in
global config`.

Sources are merged by name: the effective source list is every local source (resolved as above), plus every
global source whose name does not appear locally. Duplicate names within a single file (local or global) are
rejected: `Duplicate source name '{name}' in {global|local} config`.

## `CollectionConfig`

A named, curated list of remote files, held in `collection.items`. See [curate a collection](../how-to/curate-a-collection.md) for a
task-oriented walkthrough.

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Name` | `name` | Yes | Collection identifier, used with `eru add --collection <name>`. |
| `Tags` | `tags` | No | Tags applied to the whole collection; matched by `eru add --tags`. |
| `Description` | `description` | No | Free-text description. |
| `Files` | `files` | Yes | List of `CollectionFileRef`. |

### `CollectionFileRef`

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Source` | `source` | Yes | Name of a configured source. Must resolve to a known source in the merged config, or loading fails with `Collection '{name}' references unknown source '{source}'`. |
| `RemotePath` | `remotePath` | Yes | Path in the source repo. Supports gitignore-style globs (`docs/*.md`, `dotnet/**/*.md`). |
| `Tags` | `tags` | No | Tags for this specific file. |
| `Description` | `description` | No | Free-text description. |

```json
{
  "name": "adr-templates",
  "tags": ["adr"],
  "files": [
    { "source": "team-knowledge", "remotePath": "KNOWLEDGE/adr-template.md", "tags": ["template"] }
  ]
}
```

Collections declared in the global config are available to every repo; collections declared locally are
available only in that repo. Both `collection.items` lists are concatenated (global first, then local) in the effective config —
there is no name-based override between the two.

## `InboxConfig`

Describes one inbox — a local filesystem directory `eru inbox send` writes into. An inbox is unrelated
to `SourceConfig`/`sources`: a source is somewhere eru *pulls from*; an inbox is somewhere eru *writes
to*. `inbox.items` (in either file) is a **map keyed by inbox name**, not a list —
this is what lets `eru inbox add`/`eru inbox channel add` register several knowledge directories, each
with its own set of channels, and lets a channel's config grow later (see `InboxChannelConfig` below)
without a breaking schema change.

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Path` | `path` | Yes | Local filesystem directory (e.g. a knowledge repo checkout), checked against the filesystem at `eru inbox add`/`eru inbox send` time — or, if it starts with `http://`, `https://`, `git@` or `ssh://`, the URL of a remote git repo: `eru inbox send` then clones, commits and pushes instead of writing locally, and `inbox process`/`inbox watch` reject it. |
| `Branch` | `branch` | No | Remote inboxes only: branch `eru inbox send` pushes to. Default: the repo's default branch. |
| `RawPath` | `rawPath` | No | Path within `Path` to the raw capture folder. Default: `"inbox/raw"`. |
| `DefaultChannel` | `defaultChannel` | No | Channel `eru inbox send -c` falls back to. Default: `"default"`. |
| `Channels` | `channels` | No | Map of channel name → `InboxChannelConfig`. An entry is only needed for a channel that wants extra config — sending to an unlisted channel name is always allowed. |

### `InboxChannelConfig`

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Description` | `description` | No | Free-text description. |
| `Agent` | `agent` | No | `AgentConfig` — which agent `eru inbox process` uses to curate this channel's raw items. Absent means the channel is skipped by `inbox process`'s default (no `-c`) scope. |

### `AgentConfig`

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Protocol` | `protocol` | Yes | Only `"acp"` ([Agent Client Protocol](https://agentclientprotocol.com)) is supported in v1. |
| `Command` | `command` | Yes | Executable that launches the agent (e.g. `"opencode"`). |
| `Args` | `args` | Yes | Arguments passed to `Command` (e.g. `["acp"]`). |
| `InstructionsPath` | `instructionsPath` | No | Absolute path, or path relative to the inbox's `path`, to a file prepended to every prompt this agent receives (e.g. an `ingestor.md` agent definition) — the raw capture alone doesn't tell a generic ACP agent what to do with it. If absent, `eru inbox process` resolves instructions through a fallback chain: `<inbox path>/.agents/agents/ingestor.md` if it exists, else a convention specific to `Command`'s executable (e.g. `.claude/agents/ingestor.md` for `claude`, `.opencode/agents/ingestor.md` for `opencode`, `.cursor/agents/ingestor.md` for `cursor-agent`, `.codex/agents/ingestor.md` for `codex`, `.github/agents/ingestor.agent.md` for `copilot`) if that matches and exists, else eru's own built-in curation instructions (always available). |
| `Timeout` | `timeout` | No | Idle timeout in seconds: how long `eru inbox process` will wait with *no activity* (no `session/update` notification) from this agent before giving up — not a cap on the total turn length, so a slow-but-still-working agent won't get killed as long as it keeps streaming updates. Default: 120. Set with `--agent-timeout` on `eru inbox channel add`. |

```json
{
  "inbox": {
    "items": {
      "knowledge": {
        "path": "/Users/me/code/knowledge",
        "rawPath": "inbox/raw",
        "defaultChannel": "default",
        "channels": {
          "second-brain": { "agent": { "protocol": "acp", "command": "opencode", "args": ["acp"] } }
        }
      }
    }
  }
}
```

Inboxes merge by key (like `source.items` merge by `Name`): a local inbox of a given name wins outright over
a global inbox of the same name; any global inbox not named locally is appended. Because the typical
workflow is sending from whatever project you're currently in, most inboxes are registered in the
**global** config (`eru inbox add ... -g`) — `eru inbox send` itself never requires a local
`.eru/config.json` to exist.

## Settings sections

The `site`, `okf`, `mcp`, `sync`, `files` and `inbox` sections hold the overridable options. They are valid in
**both** files: a key set in the local file takes precedence over the same key in the global file; a key omitted
(or `null`) locally falls through to the global value, then to eru's built-in default. Each key is resolved
independently, so setting `site.hideEmptyBundles` locally does not hide a `site.ignorePatterns` set globally.

| Section | Key | Built-in default | Description |
|---|---|---|---|
| `sync` | `branch` | — | Currently unused for merge resolution (reserved). |
| `sync` | `commitOnPull` | `false` | Currently unused (reserved): eru does not auto-commit after `eru add`/`sync`, whatever this is set to. |
| `sync` | `stateFile` | `"eru.lock"` | Filename for the lock file, under `.eru/`. |
| `mcp` | `refreshIntervalMinutes` | `60` | How often `eru mcp` refreshes cached manifests/index in the background. |
| `files` | `blockPatterns` | `["*.exe", "*.dll", "*.so", "*.dylib", "*.bin", "*.out", "*.app"]` | Gitignore-style globs; matching paths are refused unless also matched by `allowPatterns`. |
| `files` | `allowPatterns` | `[]` | Gitignore-style globs that override `blockPatterns` for matching paths. |
| `files` | `allowBinaries` | `false` | When `false`, files whose content is detected as binary are refused (unless allow-listed). |
| `site` | `ignorePatterns` | `["index.md", "log.md", "README.md"]` | Gitignore-style globs; matching files are excluded entirely from `eru site generate` output (no listing, no search entry, no page) — see [site generation](site-generation.md). |
| `site` | `hideEmptyBundles` | `false` | When `true`, `eru site generate` omits bundles that list no files (including ones whose files are all matched by `site.ignorePatterns`) from the Bundles tab and the Browse sidebar. Empty bundles are shown, with a count of 0, by default — see [site generation](site-generation.md#bundles). |
| `site` | `home` | `"browse"` | Id of the page written to `index.html` — a built-in id or a custom page id. See [site generation](site-generation.md#pages). |
| `site` | `defaultView` | `"cards"` | View for document lists: `cards`, `list` or `table`. A page's own `view` overrides it — see [views](site-generation.md#views). |
| `site` | `cards.columns` | `3` | Maximum columns (1-6) of the `cards` view; narrow screens and crowded layouts drop to fewer. Set `1` for the old single-column list. |
| `site` | `layout` | refreshed defaults | Site-wide `top`, `left` and `right` slots: an array of items, or `false` to disable the slot. Merges per slot. See [layout and widgets](site-generation.md#layout-slots-and-widgets). |
| `site` | `pages` | built-in pages | Map of page id to a page definition, or `false` to drop a built-in page. Merges by id. See [pages](site-generation.md#pages). |
| `site` | `document` | — | Layout for document pages: `layout`, `open` and `rules` (per-document overrides). `rules` concatenate, global first. See [document pages](site-generation.md#document-pages). |
| `okf` | `ignorePatterns` | `["apm_modules/**", "inbox/**", "node_modules/**"]` | Gitignore-style globs, anchored at the bundle root; matching markdown files are skipped by OKF bundle discovery and `eru okf validate`. Dot-directories (`.github`, `.claude`, …) and `README.md` are always skipped and need no pattern. Use `**/inbox/**` to match nested directories. |
| `inbox` | `default` | — | Name of the inbox `eru inbox send -i` falls back to (set with `eru inbox default <name>`) when more than one inbox is configured. Not needed when exactly one inbox is configured — it's used automatically. |
| `inbox` | `watchIntervalSeconds` | `30` | `eru inbox watch`'s polling fallback interval, in seconds, in case filesystem events are missed. Overridden by that command's own `--interval` flag when passed. |

Notes:

- The `site` keys `layout`, `pages` and `document` are structured values (`false`, arrays and objects). Their shape,
  merge behaviour and the validation errors are documented in [site generation](site-generation.md). A bad value
  fails config loading with a message naming the key path, for example
  `site.layout.top[1]: unknown page id 'nope'`.
- `sync.stateFile`, `sync.branch` and `mcp.refreshIntervalMinutes` used to be valid in only one of the two files
  (`stateFile` local, `branch`/`mcpRefreshIntervalMinutes` global). Sections are now shared, so they are valid in
  either file and merge local-over-global like every other key.
- `files.blockPatterns`/`allowPatterns`/`allowBinaries` govern which files eru will fetch — see
  [`Patterns`](../../src/Eru.Domain/Patterns.fs) for the glob syntax (`*`, `**`, `?`; patterns without `/` match
  filename only).

```json
// global config
{
  "version": 3,
  "sync":  { "branch": "main", "commitOnPull": false },
  "mcp":   { "refreshIntervalMinutes": 60 },
  "files": { "blockPatterns": ["*.exe", "*.dll"], "allowPatterns": [], "allowBinaries": false },
  "site":  { "ignorePatterns": ["index.md", "log.md", "README.md"], "hideEmptyBundles": false },
  "okf":   { "ignorePatterns": ["apm_modules/**", "inbox/**", "node_modules/**"] },
  "inbox": { "watchIntervalSeconds": 30 },
  "source": { "items": [] },
  "collection": { "items": [] }
}
```

```json
// local config
{
  "version": 3,
  "sync":  { "stateFile": "eru.lock" },
  "files": { "allowPatterns": ["vendor/**/*.dll"] },
  "site":  { "ignorePatterns": ["index.md", "log.md", "README.md"] },
  "okf":   { "ignorePatterns": ["apm_modules/**", "inbox/**", "node_modules/**"] },
  "source": { "items": [] },
  "collection": { "items": [] }
}
```

## Merge and precedence

`eru` loads both files (either may be absent) and combines them into an `EffectiveConfig` used for the rest of
the run:

1. **Validate** — check `version` on each present file, then check for duplicate source names within each file's `source.items`, then check every global source has a `url`.
2. **Resolve sources** — for each local source, use it directly if it has a `url`; otherwise look it up by name in the global sources (error if not found). Append any global sources not already named locally.
3. **Validate collections** — every `CollectionFileRef.Source` (global and local `collection.items` combined) must match a resolved source name.
4. **Resolve settings** — for each key in the [settings sections](#settings-sections): the local value if set, else the global value if set, else the built-in default. Scalars and pattern lists resolve per key; a list set locally replaces (does not extend) the global list.
5. **Inboxes** — `inbox.items` merge by name: a local inbox wins outright over a global one of the same name; global inboxes not named locally are appended. `inbox.default` and `inbox.watchIntervalSeconds` resolve like any other key.
6. **Collections** — concatenate global `collection.items`' files, then local ones, into `EffectiveConfig.Collections` (no name-based override).

After this merge, `Config.withManifests` optionally layers in files advertised by each source's cached
`.eru/manifest.json` (skipping any `(source, remotePath)` pair already present from config) — see
[manifests](../explanation/concepts.md#manifests).

## Creating a config file

`eru init` scaffolds a local config at `"version": 3` with every section present: setting keys are `null` (falling
back to defaults), except `site.ignorePatterns` and `okf.ignorePatterns`, which are scaffolded populated.
`eru init --global` scaffolds a global config with `site`, `okf` and `files` populated from eru's built-in
defaults. New configs created implicitly (for example by `eru source add --global` when no global file exists)
are also written as version 3. See the [CLI reference](cli.md#eru-init) for flags.

## Upgrading from earlier versions

Version 3 is a breaking format change, but eru migrates older files automatically when it reads them. The
migrated result is held in memory and written back in the v3 format only the next time eru saves that file
(for example `eru source add` or `eru inbox default`); a read-only command such as `eru sync` leaves the file on
disk untouched.

| Version 2 key | Version 3 key |
|---|---|
| `defaults.siteIgnorePatterns` / `settings.siteIgnorePatterns` | `site.ignorePatterns` |
| `siteHideEmptyBundles` | `site.hideEmptyBundles` |
| `okfIgnorePatterns` | `okf.ignorePatterns` |
| `mcpRefreshIntervalMinutes` | `mcp.refreshIntervalMinutes` |
| `branch` | `sync.branch` |
| `commitOnPull` | `sync.commitOnPull` |
| `stateFile` | `sync.stateFile` |
| `blockPatterns`, `allowPatterns`, `allowBinaries` | `files.blockPatterns`, `files.allowPatterns`, `files.allowBinaries` |
| `defaultInbox` | `inbox.default` |
| `inboxWatchIntervalSeconds` | `inbox.watchIntervalSeconds` |
| `inboxes` (local) / `defaultInboxes` (global) | `inbox.items` |
| `sources` (local) / `defaultSources` (global) | `source.items` |
| `collections` | `collection.items` |

The `defaults` (global) and `settings` (local) blocks are gone. Version 1 files (with `basePath` on sources) are
migrated through version 2 (`basePath` becomes a single `manifest` bundle) to version 3 in one step.

See also: [lock file and local path resolution](lock-file-and-config.md), [eru concepts](../explanation/concepts.md),
[CLI reference](cli.md).
