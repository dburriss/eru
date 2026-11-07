---
title: Environment variables
type: reference
tags: [config, environment, settings, mcp]
---

# Environment variables

Every key in the [settings sections](config-file.md#settings-sections) of the config file can be overridden with an
`ERU__` environment variable. The variable is the key's path with each segment separated by `__`, and names are
case-insensitive. An environment value takes precedence over the local and the global config file, and applies to
every command, including `eru mcp` and `eru browse`. How values are typed, how lists and structured settings are
written, and what happens on a bad value are described in
[config file: environment variables](config-file.md#environment-variables).

## Settings

| Config key | Environment variable | Value |
|---|---|---|
| `sync.branch` | `ERU__SYNC__BRANCH` | string (reserved, currently unused) |
| `sync.commitOnPull` | `ERU__SYNC__COMMITONPULL` | `true` or `false` (reserved, currently unused) |
| `sync.stateFile` | `ERU__SYNC__STATEFILE` | string |
| `mcp.refreshIntervalMinutes` | `ERU__MCP__REFRESHINTERVALMINUTES` | whole number |
| `mcp.idleTimeoutMinutes` | `ERU__MCP__IDLETIMEOUTMINUTES` | whole number (`0` = never pause) |
| `files.blockPatterns` | `ERU__FILES__BLOCKPATTERNS__0`, `__1`, … | list of globs |
| `files.allowPatterns` | `ERU__FILES__ALLOWPATTERNS__0`, `__1`, … | list of globs |
| `files.allowBinaries` | `ERU__FILES__ALLOWBINARIES` | `true` or `false` |
| `search.mode` | `ERU__SEARCH__MODE` | `lex`, `sem` or `hybrid` |
| `search.ignorePatterns` | `ERU__SEARCH__IGNOREPATTERNS__0`, `__1`, … | list of globs |
| `okf.ignorePatterns` | `ERU__OKF__IGNOREPATTERNS__0`, `__1`, … | list of globs |
| `okf.fixTypes` | `ERU__OKF__FIXTYPES__0__PATTERN`, `ERU__OKF__FIXTYPES__0__TYPE`, … | list of `{ pattern, type }` |
| `okf.defaultType` | `ERU__OKF__DEFAULTTYPE` | string |
| `site.ignorePatterns` | `ERU__SITE__IGNOREPATTERNS__0`, `__1`, … | list of globs |
| `site.hideEmptyBundles` | `ERU__SITE__HIDEEMPTYBUNDLES` | `true` or `false` |
| `site.showSyncStatus` | `ERU__SITE__SHOWSYNCSTATUS` | `true` or `false` |
| `site.theme` | `ERU__SITE__THEME` | theme name |
| `site.home` | `ERU__SITE__HOME` | page id |
| `site.defaultView` | `ERU__SITE__DEFAULTVIEW` | `cards`, `list`, `table` or `rows` |
| `site.cards.columns` | `ERU__SITE__CARDS__COLUMNS` | whole number |
| `site.layout.nav`, `top`, `toolbar`, `left`, `right`, `footer` | `ERU__SITE__LAYOUT__NAV__0`, … | list of items |
| `site.pages.<id>` | `ERU__SITE__PAGES__<ID>__…` | structured; the page id is lower-cased |
| `site.document` | `ERU__SITE__DOCUMENT__LAYOUT__…`, `ERU__SITE__DOCUMENT__RULES__0__MATCH__…` | structured |
| `inbox.default` | `ERU__INBOX__DEFAULT` | inbox name |
| `inbox.watchIntervalSeconds` | `ERU__INBOX__WATCHINTERVALSECONDS` | whole number |

`source`, `collection` and `inbox.items` are not settings and can only be set in the config file; an `ERU__`
variable for one of them is ignored with a warning. The exception is the per-source settings below.

## Per-source settings

A source's own overrides use `ERU__SOURCE__<NAME>__<SECTION>__<KEY>`. They adjust a source that is already in a
config file; they cannot create one or set its `url`, `branch` or `bundles`.

| Per-source key | Environment variable | Value |
|---|---|---|
| `enabled` | `ERU__SOURCE__<NAME>__ENABLED` | `true` or `false` (`false` disables the source) |
| `files.blockPatterns` | `ERU__SOURCE__<NAME>__FILES__BLOCKPATTERNS__0`, `__1`, … | list of globs |
| `files.allowPatterns` | `ERU__SOURCE__<NAME>__FILES__ALLOWPATTERNS__0`, `__1`, … | list of globs |
| `files.allowBinaries` | `ERU__SOURCE__<NAME>__FILES__ALLOWBINARIES` | `true` or `false` |
| `search.ignorePatterns` | `ERU__SOURCE__<NAME>__SEARCH__IGNOREPATTERNS__0`, `__1`, … | list of globs |
| `okf.ignorePatterns` | `ERU__SOURCE__<NAME>__OKF__IGNOREPATTERNS__0`, `__1`, … | list of globs |
| `site.ignorePatterns` | `ERU__SOURCE__<NAME>__SITE__IGNOREPATTERNS__0`, `__1`, … | list of globs |

`<NAME>` is matched case-insensitively against the configured source names, with `-` and `.` read as `_`, so
`ERU__SOURCE__MY_DOCS__FILES__ALLOWBINARIES=true` targets the source `my-docs`. A name that matches no source is
ignored, and one that matches two sources (`Docs` and `docs`) is an error. See
[config file: per-source settings](config-file.md#per-source-settings).

## Alias

| Environment variable | Same as |
|---|---|
| `ERU_SEARCH_MODE` | `ERU__SEARCH__MODE` (the `ERU__` form wins if both are set) |

## Other variables eru reads

These are separate from the `ERU__` settings.

| Variable | Purpose |
|---|---|
| `ERU_SEARCH_BACKEND` | Deprecated: `ck` means `hybrid`, anything else `lex`, with a warning. Use `search.mode`. |
| `FERRET_OFFLINE` | Set to `1` to never download the semantic-search model. |
| `FERRET_MODEL_DIR` | Directory holding `model_quantized.onnx` and `vocab.txt`. |
| `FERRET_MODEL_URL` | Download the model from a mirror instead of Hugging Face. |
| `XDG_CONFIG_HOME` | Base directory for the global config (`$XDG_CONFIG_HOME/eru/config.json`). |
| `XDG_CACHE_HOME` | Base directory for eru's caches. |

See also: [config file](config-file.md), [set up the MCP server](../how-to/set-up-the-mcp-server.md).
