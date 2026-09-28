---
title: Config file
type: reference
tags: [config, sources, collections, defaults, settings]
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
| Holds | Repo-specific sources, collections, and settings | Default sources, shared collections, and fallback defaults |

Both are optional — eru runs with sensible built-in defaults if neither exists. Where both are present, fields
are merged as described in [Merge and precedence](#merge-and-precedence) below.

## Top-level shape

**Local config** (`.eru/config.json`):

```json
{
  "version": 1,
  "sources": [ /* SourceConfig[] */ ],
  "collections": [ /* CollectionConfig[] */ ],
  "inboxes": { /* { [name]: InboxConfig } */ },
  "settings": { /* LocalSettings */ }
}
```

**Global config** (`~/.config/eru/config.json`):

```json
{
  "version": 1,
  "defaultSources": [ /* SourceConfig[] */ ],
  "collections": [ /* CollectionConfig[] */ ],
  "defaultInboxes": { /* { [name]: InboxConfig } */ },
  "defaults": { /* GlobalDefaults */ }
}
```

`version` is required in both files. eru currently supports version `1`; a config declaring a higher version
fails to load with `Unsupported {global|local} config version {n} — please upgrade eru`.

## `SourceConfig`

Describes one knowledge source (a remote repo, or a reference to one defined globally).

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Name` | `name` | Yes | Unique identifier for the source. Used in `<source>:<path>` references (collections, lock file, CLI args). |
| `Url` | `url` | Conditional | Git URL of the source repo. Required in the global config. In the local config, may be omitted if a source of the same `name` exists in the global config (see [source resolution](#source-resolution)). |
| `Branch` | `branch` | No | Branch/ref to fetch from. Falls back to the source's default branch if omitted. |
| `BasePath` | `basePath` | No | Prefix stripped from remote paths when computing the local destination path — see [local path resolution](lock-file-and-config.md#where-files-land). |

```json
{ "name": "team-knowledge", "url": "https://github.com/my-org/knowledge.git", "branch": "main", "basePath": "KNOWLEDGE" }
```

### Source resolution

A local `sources` entry may omit `url` to reuse a source already defined in the global config under the same
`name` — the global entry's `url`/`branch`/`basePath` are used as-is. If a local entry has no `url` and no
matching global source exists, config loading fails with `Local source '{name}' has no URL and was not found in
global config`.

Sources are merged by name: the effective source list is every local source (resolved as above), plus every
global source whose name does not appear locally. Duplicate names within a single file (local or global) are
rejected: `Duplicate source name '{name}' in {global|local} config`.

## `CollectionConfig`

A named, curated list of remote files. See [curate a collection](../how-to/curate-a-collection.md) for a
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
available only in that repo. Both lists are concatenated (global first, then local) in the effective config —
there is no name-based override between the two.

## `InboxConfig`

Describes one inbox — a local filesystem directory `eru inbox send` writes into. An inbox is unrelated
to `SourceConfig`/`sources`: a source is somewhere eru *pulls from*; an inbox is somewhere eru *writes
to*. `inboxes` (local) and `defaultInboxes` (global) are both **maps keyed by inbox name**, not lists —
this is what lets `eru inbox add`/`eru inbox channel add` register several knowledge directories, each
with its own set of channels, and lets a channel's config grow later (see `InboxChannelConfig` below)
without a breaking schema change.

| Field | JSON key | Required | Description |
|---|---|---|---|
| `Path` | `path` | Yes | Local filesystem directory (e.g. a knowledge repo checkout). Checked against the filesystem at `eru inbox add`/`eru inbox send` time — never treated as a git URL. |
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
| `InstructionsPath` | `instructionsPath` | No | Absolute path, or path relative to the inbox's `path`, to a file prepended to every prompt this agent receives (e.g. an `ingestor.md` agent definition) — the raw capture alone doesn't tell a generic ACP agent what to do with it. If absent, `eru inbox process` falls back to `<inbox path>/.agents/agents/ingestor.md` if it exists, else proceeds with no instructions. |

```json
{
  "inboxes": {
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
```

Inboxes merge by key (like `sources` merge by `Name`): a local inbox of a given name wins outright over
a global inbox of the same name; any global inbox not named locally is appended. Because the typical
workflow is sending from whatever project you're currently in, most inboxes are registered in the
**global** config (`eru inbox add ... -g`) — `eru inbox send` itself never requires a local
`.eru/config.json` to exist.

## `defaults` (global) and `settings` (local)

Both blocks hold the same set of overridable options. `settings` (local) takes precedence over `defaults`
(global); a field omitted (or `null`) locally falls through to the global value, then to eru's built-in default.

| Field | JSON key | Global-only? | Built-in default | Description |
|---|---|---|---|---|
| `Branch` | `branch` | Yes | — | Currently unused for merge resolution (reserved). |
| `CommitOnPull` | `commitOnPull` | No | `false` | Whether `eru add`/`sync` auto-commit changes after pulling. |
| `StateFile` | `stateFile` | Local-only | `"eru.lock"` | Filename for the lock file, under `.eru/`. |
| `McpRefreshIntervalMinutes` | `mcpRefreshIntervalMinutes` | Yes | `60` | How often `eru mcp` refreshes cached manifests/index in the background. |
| `BlockPatterns` | `blockPatterns` | No | `["*.exe", "*.dll", "*.so", "*.dylib", "*.bin", "*.out", "*.app"]` | Gitignore-style globs; matching paths are refused unless also matched by `AllowPatterns`. |
| `AllowPatterns` | `allowPatterns` | No | `[]` | Gitignore-style globs that override `BlockPatterns` for matching paths. |
| `AllowBinaries` | `allowBinaries` | No | `false` | When `false`, files whose content is detected as binary are refused (unless allow-listed). |
| `SiteIgnorePatterns` | `siteIgnorePatterns` | No | `["index.md", "log.md"]` | Gitignore-style globs; matching files are excluded entirely from `eru site generate` output (no listing, no search entry, no page) — see [site generation](site-generation.md). |
| `DefaultInbox` | `defaultInbox` | No | — | Name of the inbox `eru inbox send -i` falls back to when more than one inbox is configured. Not needed when exactly one inbox is configured — it's used automatically. |

Notes:

- `StateFile` only makes sense per-repo, so it exists only on local `settings`, not global `defaults`.
- `Branch` and `McpRefreshIntervalMinutes` only make sense as a shared default, so they exist only on global
  `defaults` (there is no per-repo override).
- `BlockPatterns`/`AllowPatterns`/`AllowBinaries` govern which files eru will fetch — see
  [`Patterns`](../../src/Eru.Domain/Patterns.fs) for the glob syntax (`*`, `**`, `?`; patterns without `/` match
  filename only).

```json
// global config
{
  "version": 1,
  "defaultSources": [],
  "collections": [],
  "defaults": {
    "branch": "main",
    "commitOnPull": false,
    "mcpRefreshIntervalMinutes": 60,
    "blockPatterns": ["*.exe", "*.dll"],
    "allowPatterns": [],
    "allowBinaries": false,
    "siteIgnorePatterns": ["index.md", "log.md"]
  }
}
```

```json
// local config
{
  "version": 1,
  "sources": [],
  "collections": [],
  "settings": {
    "commitOnPull": true,
    "stateFile": null,
    "blockPatterns": null,
    "allowPatterns": ["vendor/**/*.dll"],
    "allowBinaries": null,
    "siteIgnorePatterns": ["index.md", "log.md"]
  }
}
```

## Merge and precedence

`eru` loads both files (either may be absent) and combines them into an `EffectiveConfig` used for the rest of
the run:

1. **Validate** — check `version` on each present file, then check for duplicate source names within each file, then check every global source has a `url`.
2. **Resolve sources** — for each local source, use it directly if it has a `url`; otherwise look it up by name in the global sources (error if not found). Append any global sources not already named locally.
3. **Validate collections** — every `CollectionFileRef.Source` (global and local collections combined) must match a resolved source name.
4. **Resolve settings** — for each option in the table above: local value if set, else global value if set, else the built-in default. `CommitOnPull` is the one exception with asymmetric fallback (local `settings.commitOnPull` overrides global `defaults.commitOnPull`, not the general list-of-options mechanism, but the effective outcome is the same precedence order).
5. **Collections** — concatenate global collections' files, then local collections' files, into `EffectiveConfig.Collections`.

After this merge, `Config.withManifests` optionally layers in files advertised by each source's cached
`.eru/manifest.json` (skipping any `(source, remotePath)` pair already present from config) — see
[manifests](../explanation/concepts.md#manifests).

## Creating a config file

`eru init` scaffolds a local config with `settings` fields present but `null` (falling back to defaults),
except `siteIgnorePatterns` which is scaffolded populated. `eru init --global` scaffolds a global config with
`defaults` populated from eru's built-in defaults. See the [CLI reference](cli.md#eru-init) for flags.

See also: [lock file and local path resolution](lock-file-and-config.md), [eru concepts](../explanation/concepts.md),
[CLI reference](cli.md).
