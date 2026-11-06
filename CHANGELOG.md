# Changelog

## [Unreleased]

### Changed

- **Breaking:** the `top` layout slot is renamed `nav`. A config that puts page ids or links in `top` now fails with a message pointing to `nav`; rename the key (also `ERU__SITE__LAYOUT__TOP__*` to `ERU__SITE__LAYOUT__NAV__*`). `top` is now a full-width widget band.
- **Breaking:** the `query` widget is no longer valid in `main`; it goes in `toolbar`, `left` or `right`. Browse now shows `title` in `top`, the query line in `toolbar` and `list` in `main`.
- **Breaking:** the generated site has a new terminal look: monospace type, square corners, hairline borders and an orange accent. The CSS variable names are unchanged, so a `custom.css` that sets `--color-*`, `--font-family` or `--radius` still applies, but overrides of component classes may need a second look.
- **Breaking:** Browse is now a query line above a table of name, source and tags, with no sidebar. Set `layout.left` to `["facets"]` to bring the sidebar back, or `defaultView` to `cards` for the old cards.

### Added

- `eru source disable <name>` and `eru source enable <name>` switch a source off and on without removing it. A disabled source is not synced, searched, served over MCP or shown in the site, and collection files pointing at it are skipped. It is also the `enabled` field of a source entry and `ERU__SOURCE__<NAME>__ENABLED`, and `eru source list` shows its status. `-g` edits the global config; otherwise a url-less local entry is used so only the current repo is affected.
- Site themes: `site.theme` (`terminal` by default, or `ledger`) switches the look of the generated site. A theme is a folder with a `theme.json` (default `site` settings) and a `theme.css`; custom themes go in `.eru/themes/<name>/` or `~/.config/eru/themes/<name>/`. Your config and `custom.css` still win. `ERU__SITE__THEME` and `eru config set site.theme` work too. See [themes](docs/reference/site-generation.md#themes).
- `eru theme list` shows the built-in and installed themes and which one is active.
- A `rows` view: one bordered row per document with its description, source, tags and sync state. It is the `ledger` theme's default.
- New layout slots `top`, `toolbar` and `footer`: full-width bands above and below the columns. Every page footer links to the eru docs by default; set `footer` to replace it or `"footer": false` to remove it. Widgets are valid in more slots (`breadcrumbs`, `title`, `stale` in `top`; `meta`, `verified`, `generated`, `description`, `tags`, `cli`, `links` in `top` and `footer`).
- The query line can be moved: `query` is valid in `toolbar`, `left` and `right` (once per page), and any page can opt in.
- A `query` widget: type `tag:`, `source:` or `status:` to filter with a completion list and live counts, exclude with `-tag:x`, click a source or tag in the table to add it, and share the filtered view through `?q=` in the address. Press `/` to focus it and `j`, `k` and enter to move through rows.
- The sync state of each file shows as a glyph before its name (`●` pulled, `◐` cached, `○` index-only), and rows carry a `data-sync` attribute.

### Fixed

- A `--color-*` override under `:root` in `custom.css` now takes effect. The theme colours were set on `body`, which shadowed any `:root` rule; they now sit on `<html>` with no extra specificity, and the theme class is stamped on both `<html>` and `<body>`. Per-theme overrides under `body.theme-light` and `body.theme-dark` keep working.
- A `pages.browse` override with a `table` block no longer needs `"view": "table"`. Browse renders as a table by default, but validation judged it as cards and rejected the block.

## [0.15.1] - 2026-11-05

### Changed

- The facet counts in the Browse sidebar now follow the filters: each count is the number of documents that match the search text and the selection in the other facets, so choosing a source narrows the tag, type and bundle counts while its own siblings keep theirs. Values with no matches are dimmed, and the File Extensions facet now shows counts too. This was meant for 0.15.0 but missed that release. See [facets](docs/reference/site-generation.md#layout-slots-and-widgets)

## [0.15.0] - 2026-11-05

### Added

- Every config setting can be overridden with an environment variable that mirrors its path in the config file: `ERU__` plus the key path joined with `__` (`search.mode` is `ERU__SEARCH__MODE`, `mcp.refreshIntervalMinutes` is `ERU__MCP__REFRESHINTERVALMINUTES`), with list items indexed (`ERU__FILES__BLOCKPATTERNS__0`). The environment overrides the local and global config in the CLI, `eru browse` and `eru mcp`, so an MCP client can pass settings in its `env` block. An invalid value stops the command at startup and names the variable; `eru config get` and `eru config view` show such a value with origin `env`, and it is never written to a config file. See [environment variables](docs/reference/config-file.md#environment-variables)

- Per-source settings: a `source.items` entry can carry its own `files` (`blockPatterns`, `allowPatterns`, `allowBinaries`), `search.ignorePatterns`, `okf.ignorePatterns` and `site.ignorePatterns`, which replace the global value for that source only in sync, add, read, search and the generated site. Read and write them with `eru config get|set|view --source <name>`, or override a configured source for one process with `ERU__SOURCE__<NAME>__<SECTION>__<KEY>` variables. A local entry without a `url` merges its settings key by key over the global entry's. See [per-source settings](docs/reference/config-file.md#per-source-settings)

### Changed

- `ERU_SEARCH_MODE` is now an alias for `ERU__SEARCH__MODE`, so an invalid value is reported at startup by every command instead of only by `eru search`

## [0.14.0] - 2026-11-02

### Added

- Ranked search powered by [Ferret](https://github.com/dburriss/ferret). `eru search` with terms and the MCP `search_knowledge` tool now rank files by content (BM25: rarer terms count more, matches in the path, headings and front matter count extra, word forms match, so `mock` finds `mocked`) and return the best first. One engine now serves `eru search`, the MCP tool and the `eru serve` `/api/search` endpoint, so their rules no longer differ
- `search_knowledge` takes an optional `limit` (default 15) and `mode`, caps each file at three excerpt lines, and returns `truncated`, per-hit `score` and `warnings` in its structured result. `eru search` gains `--limit` and `--mode lex|sem|hybrid` and prints scores
- Optional semantic and hybrid search (`sem`, `hybrid`) with a local embedding model (`bge-small-en-v1.5`, about 33 MB, no service). Choose with `search.mode`, `ERU_SEARCH_MODE`, `--mode` or the MCP `mode` parameter. The model is never downloaded during an MCP search: the server fetches it in the background, and until then `sem` and `hybrid` run `lex` and say so in `warnings`. `eru search --mode hybrid` runs `lex` first and then downloads the model for next time. `FERRET_OFFLINE=1`, `FERRET_MODEL_DIR` and `FERRET_MODEL_URL` control the download
- `search.ignorePatterns` config (default `["index.md", "log.md"]`): matching files are never searched, so a generated `index.md` no longer duplicates its README. READMEs stay searchable, unlike `site.ignorePatterns`. `search.mode` (default `lex`) and `search.ignorePatterns` are managed with `eru config`
- `eru browse` Files pane: pressing Enter on a search filter now runs the same ranked content search in the background and lists the best matches first (`#tag` filters and the instant as-you-type narrowing are unchanged)
- `eru source add --scan --exclude <glob>` (repeatable) skips OKF bundles whose directory matches the glob; excluding the root bundle registers its nested bundles instead
- `okf.fixTypes` (an ordered list of `{pattern, type}` globs) and `okf.defaultType` config settings: `eru okf fix` types untyped files by glob, including READMEs, and `--default-type` now overrides a matching rule
- Root `apm.yml` so eru can be installed as an [APM](https://github.com/microsoft/apm) package (`apm install dburriss/eru`): the `eru`, `semantic-search` and `organizing-documentation` skills plus the `ingestor` agent and the `eru` MCP server

### Changed

- The built-in search backends (simple scan, per-file word index and the `ck` process) are removed in favour of Ferret. `ERU_SEARCH_BACKEND` is deprecated and only mapped: `ck` now means `hybrid`, any other value `lex`, with a warning on stderr. `eru sync` no longer builds a per-file word index; search indexes are built on first use and by the MCP server in the background, under `~/.cache/eru/ferret` (`eru cache clear` removes them and the old `~/.cache/eru/index`)
- `eru search <terms>` now matches file content, not only path and description, and its `--output json` entries gain `path`, `hash`, `score` and `excerpts` (`sourceName` and `remotePath` are `null` for local files). The MCP `search_knowledge` structured result is now `{ hits, truncated, warnings }` in camelCase, as its output schema already advertised, instead of PascalCase `Hits`
- Release binaries carry ONNX Runtime for semantic search (about 45 MB more per platform; it is unpacked on first run)
- A `README.md` with a frontmatter `type` is now a concept: `eru okf validate`, `fix` and `verify` handle it like any other concept, it is listed in generated catalogs and discovered with its metadata. A README without a `type` is still skipped. `eru site` still hides READMEs by default through `site.ignorePatterns`

### Fixed

- Files in a local `knowledge/` directory were listed twice by search on case-insensitive file systems (macOS, Windows)
- Table output no longer crashes (`Could not find color or style 'Go'`) when a path, message or description contains square brackets, e.g. every `eru okf validate` broken-link warning; table cells are now rendered literally in all commands

## [0.13.1] - 2026-10-12

### Fixed

- `eru site` rewrites relative Markdown links in document bodies (`explanation.md`, `./x.md#anchor`, `../other/x.md`) to the generated page; links to unindexed files, external links and code are left as written

## [0.13.0] - 2026-10-12

### Added

- `eru config` inspects and manages configuration: `list` shows the global and local config files with their paths and versions, `view` shows the effective config with the origin (`local`, `global` or `default`) of each value, `get <key>` shows one setting, `set <key> <json-value> [--global] [--unset]` edits a setting (validated before writing, files left untouched on error) and `migrate [--local|--global] [--dryrun]` rewrites an older config as version 3, keeping a `.bak` of the original
- Configurable generated site. Every page is built from four slots (`top`, `left`, `right`, `main`) of widgets, set under `site.layout` (site-wide) and per page; a slot is an ordered list or `false`. Widgets: `breadcrumbs`, `title`, `meta`, `verified`, `generated`, `description`, `tags`, `stale`, `cli`, `content`, `list`, `toc`, `related`, `graph`, `facets`, `links`
- `site.pages` defines custom pages at `pages/<id>/index.html`: a `filter` (by `source`, `bundle`, `type`, `tag`, `status`, `ext`, `path` globs, `stale`, with `not`) with `sort`, `group`, `limit` and `open` (`page`, `resource` or `source`), a `content` page rendering a repo Markdown file (`content.file`) or an indexed document (`content.doc`), or both. The built-in pages (`browse`, `sources`, `tags`, `types`, `bundles`) can be dropped with `false` or overridden by a page of the same id, and `site.home` picks the page at `index.html`
- Three list views, `cards`, `list` and `table`, chosen with `site.defaultView` or a page's `view`; `site.cards.columns` (1-6) sets the card grid's maximum columns. Table headers sort when JavaScript is available
- `site.document` configures document pages: `layout` for every document and `rules` that match on `type`, `tag`, `bundle`, `source` and the other filter fields, so different documents get different layouts. The `verified` widget shows the trust tier with each verifier and date; `generated` shows who or what generated a document and when
- `eru site serve` rebuilds when a local Markdown file used by a content page changes
- Site config errors (unknown page or widget, a widget in a slot it is not valid in, a bad view or column, a missing or out-of-repo content file) fail with a message naming the key; a failed rebuild writes nothing, so `eru site serve` keeps serving the last good build

### Changed

- **Breaking:** the generated site has refreshed defaults. Every page gets a facet column on the left and, when it has headings or links, a table of contents on the right; lists are a card grid of up to three columns; the top nav is built from the pages that exist. Document pages are rebuilt from widgets, and the default document layout shows the link graph at the top of the page and the linked-document lists at the bottom. To get close to the old look, set `site.layout.left` and `site.layout.right` to `false` and `site.cards.columns` to `1`
- Browse filtering is driven by markup instead of per-list ids: any element marked `data-doc` is filterable, `data-<facet>` attributes hold its values as JSON arrays and `data-facet="<name>"` on a sidebar list makes it a filter. Types and bundles are now checkbox facets on Browse. Custom CSS or scripts that relied on `#file-list`, `#file-count` or the `source-filters`, `ext-filters` and `tag-filters` ids need updating
- The page-level `SiteFeatures` flags `TagPages` and `SourcePages` are removed from `GenerateOptions`; drop pages with `site.pages.<id>: false` instead (`types` and `bundles` were previously tied to `TagPages`)

- **Breaking:** config files are now version 3. Settings are grouped into top-level sections, shared by `~/.config/eru/config.json` and `.eru/config.json`, and the `settings` / `defaults` blocks are removed. Section names are singular and the entities a section holds always live under `items`. Version 1 and 2 files are migrated automatically when read and rewritten as version 3 only on the next save (read-only commands leave the file untouched); newly created configs are written as version 3

  | Version 2 | Version 3 |
  |---|---|
  | `siteIgnorePatterns` | `site.ignorePatterns` |
  | `siteHideEmptyBundles` | `site.hideEmptyBundles` |
  | `okfIgnorePatterns` | `okf.ignorePatterns` |
  | `mcpRefreshIntervalMinutes` | `mcp.refreshIntervalMinutes` |
  | `branch`, `commitOnPull`, `stateFile` | `sync.branch`, `sync.commitOnPull`, `sync.stateFile` |
  | `blockPatterns`, `allowPatterns`, `allowBinaries` | `files.blockPatterns`, `files.allowPatterns`, `files.allowBinaries` |
  | `defaultInbox`, `inboxWatchIntervalSeconds` | `inbox.default`, `inbox.watchIntervalSeconds` |
  | `inboxes` / `defaultInboxes` | `inbox.items` |
  | `sources` / `defaultSources` | `source.items` |
  | `collections` | `collection.items` |

- Every settings section is now valid in both the global and the local file, merging local-over-global per key. `sync.stateFile` was previously local-only, and `sync.branch` and `mcp.refreshIntervalMinutes` global-only
- `eru inbox default` writes `inbox.default`

### Fixed

- Reading a version 1 config (sources with `basePath`) migrates `basePath` to a `manifest` bundle instead of dropping it

## [0.12.0] - 2026-10-09

### Added

- `eru print <source:path|hash>` prints a document's contents to stdout (`-o json` for `{source, path, hash, content}`). It resolves from the source indexes, so documents need not be added first, and reads the local file, then the cache, then a live fetch. A 3-8 character path hash that matches several documents errors and lists the candidates

## [0.11.1] - 2026-10-08

### Fixed

- `eru sync` re-runs OKF bundle discovery when a source's index has no entries, even if the remote HEAD SHA is unchanged; previously an empty index with a current SHA stayed empty (`0 entries indexed`) until the remote moved. This recovers indexes left empty by builds from before "Do not empty index on failed sync"

## [0.11.0] - 2026-10-08

### Added

- `eru site generate` adds a **Bundles** tab after Types: `bundles/index.html` lists every registered bundle with its kind and file count, and `bundles/<source>/<path>/index.html` lists one bundle's files. A file appears under its most specific bundle only. Bundles are named `<source>` (root) or `<source>/<path>`, so nested bundles are namespaced by their parents. The Browse sidebar gains a Bundles section and `data/documents.json` carries a `bundle` field
- `siteHideEmptyBundles` setting (default `false`): when `true`, `eru site generate` leaves bundles with no listed files out of the Bundles tab and Browse sidebar
- `eru source add --scan --nested` keeps nested OKF bundles instead of letting a root bundle cover them

- `eru sync` reports the index size per source (`norms: 69 entries indexed (okf:/)`) and warns on stderr when a source indexed nothing, saying whether bundles are missing or OKF concept files lack a `type`; `--output json` gains an `indexes` array

### Changed

- `eru sync` no longer walks an OKF bundle that is nested under another OKF bundle it already walked

### Fixed

- `eru sync` no longer empties the index of OKF-bundle sources: the first sync after adding one populated it, but every later sync wiped it while the unchanged remote HEAD skipped rediscovery, so search found nothing. Discovered entries now persist between syncs, are pruned only when the remote changes and the re-walk succeeds, and a failed re-walk keeps the previous entries

## [0.10.0] - 2026-10-05

### Added

- `eru inbox default <name>` sets `settings.defaultInbox` (`-g` for global).
- `eru okf validate` warns on broken links in concept and `index.md` files: `broken-link`, `broken-image`, `broken-wikilink` and `broken-anchor` (heading missing from the target note; GitHub-style and generated-site anchors both accepted), each with its line number and the link as written. They never change the exit code unless `--strict-links` is given, which reports them as violations. The ingestor template now writes self-contained notes and rewrites links that point outside the bundle

### Changed

- `eru inbox channel add` no longer copies the agent onto a `default` channel; it sets the inbox's `defaultChannel` to the new channel when none is set
- `eru inbox process` archives tracked items with `git mv` (FsForge 0.0.1 `GitOps.moveFile`), so the archive shows as staged renames; untracked items and non-git directories still get a plain move

## [0.9.1] - 2026-10-02

### Fixed

- Path handling on Windows: domain code built and split `/`-separated bundle and remote paths with `System.IO.Path`, which emits `\` on Windows and broke 69 tests in the release build; it now uses `/`-only helpers, and a test guards against reintroducing `Path.Combine`/`GetDirectoryName`/`GetFileName` in `Eru.Domain`

## [0.9.0] - 2026-10-02

### Added

- `eru source add --scan` detects OKF bundles (any `index.md` with `okf_version`; a root marker covers nested ones) and falls back to the `knowledge/` convention. **Breaking:** without `--scan`, `eru source add` no longer auto-detects the `knowledge/` bundle; register bundles with `eru source bundle add`
- OKF bundle discovery (and so `eru site generate`) now indexes only concept files that have a non-empty `type` frontmatter; untyped markdown is skipped
- `eru okf verify <file>` records a `{ by, at }` confirmation in a concept file's `verified` list (`--by` defaults to `human:<git user.email>`, `--at` to now); `eru okf fix` now strips a malformed `verified` (placeholders like `unknown`/`false`, entries missing `by`/`at`), keeping valid entries; `eru okf validate` warns on placeholder `verified` values and `by: unknown`, pointing at `fix` and `verify`
- OKF v0.2 alignment: `eru okf init`/`fix` write `okf_version: "0.2"`; `eru okf validate` reports non-fatal v0.2 shape warnings (`generated`, `verified`, `stale_after`, `status`, `sources`, legacy `timestamp`, `log.md` order); eru reads `sources` and falls back to a legacy `timestamp` when `generated` is absent; the ingestor agent template now writes spec-shaped `generated`/`sources` and omits `verified` until a human reviews; `eru inbox send` quotes `resource` in its frontmatter
- `eru okf init <path>` creates missing `index.md` files (root with `okf_version`, plus a catalog table per folder), never overwriting; `eru okf fix <path>` repairs a tree so `eru okf validate` passes (index frontmatter, missing concept frontmatter/`type`, non-ISO `log.md` date headings, missing indexes). Both write by default; `--dry-run` previews, `--default-type` sets the type for untyped concepts
- `eru source bundle add` warns when a bundle is auto-detected as `manifest` but has no `.eru/manifest.json`, listing the ways to fix it (add `okf_version` to `index.md`, pass `--kind okf`, or create a manifest); `eru source add` does the same for a detected `KNOWLEDGE/` bundle
- `eru okf validate`: the bundle-root `index.md` frontmatter message now explains that it should contain only `okf_version`, which is what makes eru detect an OKF bundle
- `README.md` and dot-directories (`.github`, `.claude`, `.agents`, …) are no longer treated as OKF concepts: they are skipped by `eru okf validate` and OKF bundle discovery
- `siteIgnorePatterns` now defaults to `index.md`, `log.md` and `README.md` (configs with an explicit value are unchanged)
- `okfIgnorePatterns` setting (default `apm_modules/**`, `inbox/**`, `node_modules/**`) to skip paths during OKF bundle discovery and `eru okf validate`
- `eru version` command prints the version and the git commit it was built from
- Added description field to the manifest
- Remote git inboxes: `eru inbox add <name> <git-url> [--branch]` registers a repo as an inbox and `eru inbox send` clones it shallowly, commits the item and pushes (via FsForge); remote inboxes are send-only
- Added `eru site serve` command to serve the generated static site locally with live reload on changes to the cache
- `eru okf validate <path>` command to check a directory tree for OKF §11 conformance (parseable frontmatter with a non-empty `type`, valid `index.md`/`log.md` structure); never flags unknown types, unknown extra keys, broken cross-links, or missing optional fields
- Generated site now surfaces OKF frontmatter: a trust-tier badge per file (`unverified`/`machine-confirmed`/`human-reviewed`, derived from `verified[].by`) and a new "Types" filter/browse page alongside the existing Sources and Tags
- Added `SiteIgnorePattern` with default values of "index.md" and "log.md" to ignore OKF structure files when generating site pages
- OKF bundle discovery on sources
- `eru inbox` commands for handling inboxes/channels and sending to inboxes
- `eru inbox process` command to run pending inbox items through their channel's configured ACP agent, archiving processed items; supports `--inbox`, `--channel`, `--item`, `--all`, `--dryrun`, and `--output`; `--debug` also reports per-item agent handshake timings (initialize/session-new/prompt)
- `eru inbox watch` command to watch an inbox's raw directory and automatically run `inbox process` on new items, debounced and also polled on a configurable interval (`--interval`, default from config); supports `--dryrun` and `--debug`
- `--agent-timeout` option on `eru inbox channel add` to bound how long an ACP agent call may run; the timeout is a liveness check (reset on any agent activity) rather than a fixed deadline
- ACP agent output is streamed as it arrives instead of only being shown once the call completes
- `eru inbox process` now falls back further when a channel has no `--agent-instructions` configured and `<inbox>/.agents/agents/ingestor.md` doesn't exist: it checks a tool-specific convention path derived from `--agent-command` (e.g. `.claude/agents/ingestor.md` for `claude`), and finally falls back to eru's own built-in curation instructions — so a channel's agent always receives some curation instructions
- `--append <text|@file>` (repeatable) on `eru inbox process` and `eru inbox watch` adds text to the agent prompt after the resolved instructions and before the item; a leading `@` reads a file, `@@` is a literal `@`

### Changed

- Built-in ingestor instructions (`agents/ingestor.agent.md`) now produce a valid OKF bundle: folder `index.md` files get no frontmatter, the root `index.md` carries only `okf_version`, `README.md` is treated as outside the bundle, and the agent uses `eru okf init` for new folders and `eru okf validate`/`fix` as a final self-check
- Built-in ingestor instructions (`agents/ingestor.agent.md`) no longer loop until `inbox/raw/` is empty; the agent processes only the items it is told to (by `eru`, a harness prompt, or a workflow) and asks if none are specified
- Built-in ingestor instructions (`agents/ingestor.agent.md`) no longer include a commit step, so they stay neutral across environments; use `--append` to add commit/PR behaviour
- `eru manifest verify` renamed to `eru manifest validate`; `verify` is kept as a backward-compatible alias
- `eru add` gives a clearer error when passed a bare repo URL, suggesting `eru source add <url>` followed by `eru add <source>:<path>`
- Unhandled exceptions now print a concise `eru: unexpected error: <message>` instead of a raw stack trace; pass `--debug` to see the full stack trace

### Fixed

- OKF bundle discovery no longer fails when a source has no branch configured
- Pinned the test runner via `global.json` (`Microsoft.Testing.Platform`) to fix `dotnet test` discovery

## [0.8.0] - 2026-06-03

### Added

- `eru add` now supports using hashes instead of the file path to add files to local repository
- `--output <format>` flag (short: `-o`) on every command and subcommand that produces output — accepted values are `table` (default), `text`, and `json`
  - `table` renders results as a Spectre.Console table with column headers
  - `text` preserves the original plain-text style for piping and terminal use
  - `json` emits machine-readable JSON for scripting and tool integration
- `eru remove` command to delete a local artifact file and its lock entry by path; supports `--dryrun` and `--output` flags
- `eru disconnect` command to remove a tracked artifact from the lock file without deleting the local file; supports `--dryrun` and `--output` flags
- `eru cache prune` command to remove orphaned content files not referenced by any source index; supports `--force` to skip confirmation
- `eru cache clear` command to delete all cached indexes and files (`sources/`, `index/`, and `collections/` directories under `~/.cache/eru/`); supports `--dryrun`, `--force`, and `--output` flags
- `eru browse` command to interactively browse sources and tracked files in the terminal
- MCP schema updates and fixes
- `eru site generate` command to create a static HTML site for browsing the local cache with search and filtering; supports `--output` to specify the output directory (default: `./cache-site/`) and `--open` to open the generated site in the default browser immediately after generation
 
### Changed

- `eru sync` now performs one git clone per source instead of one per lock entry — all paths from the same repository are fetched in a single blobless sparse-checkout call, reducing network I/O from O(files) to O(sources); the same batching applies to the MCP knowledge cache sync in `KnowledgeSyncService`
- `eru add --target` (short: `-d`) now supports two modes: a path ending with `/` is treated as a target directory and keeps only the source filename (e.g. `--target docs/` + `shared/adr.md` → `docs/adr.md`); a path without a trailing slash is used directly as the local file path (e.g. `--target docs/custom.md` or `--target bin/mybinary`). **Breaking:** `--target docs` (no slash) previously wrote to `docs/<filename>` — add a trailing slash to preserve that behaviour.
- Default output for all list and query commands (`search`, `sync`, `source list`, `source view`, `source files`, `add`) is now a formatted table instead of plain text — pass `--output text` to restore the previous behaviour
- Search results now return structured data in addition to the existing text output — the `search_knowledge` MCP tool returns an array of hits with path, source, tags, description, and excerpts; clients can parse the structured content or fall back to the text as before
- `eru add` searches through all sources for hash or file path
- `eru source files` table output now shows **Hash** as the first column
- `eru.lock` now lives at `.eru/eru.lock` instead of the repository root, keeping all eru state inside the `.eru/` directory alongside `config.json` and `manifest.json`; existing `eru.lock` files at the repo root are migrated automatically on the first command that reads the lock file

### Fixed

- `eru source files <name>` no longer crashes with `NullReferenceException` when a manifest entry has no `tags` field — `System.Text.Json` was deserializing absent or null list fields as `null` rather than `[]`; manifests are now normalized on load so all consumers see empty lists instead

## [0.7.1] - 2026-05-29

### Fixed

- MCP resources now declare `text/plain` MIME type — previously the SDK defaulted to `application/octet-stream`, causing many MCP clients and inspectors to not display the `eru://sources`, `eru://sources/{name}`, and `eru://installed` resources

## [0.7.0] - 2026-05-29

### Added

- Serilog rolling-file logging for the MCP server — warnings and errors from background syncs are written to `~/.cache/eru/mcp-YYYYMMDD.log` (XDG-aware; `%LOCALAPPDATA%\eru\` on Windows); daily rotation, 7-day retention

### Changed

- `refresh_knowledge` MCP tool now returns immediately instead of blocking until all git fetches complete — the sync runs on a background thread and any errors are written to the MCP log file rather than returned inline
- `CollectionCacheService` timer ticks also go through the new background-sync path, freeing the timer thread from blocking on network I/O
- Concurrent sync calls are deduplicated — if a sync is already in progress (from the timer or a previous `refresh_knowledge` call), the tool returns `"A knowledge refresh is already in progress."` rather than starting a second parallel sync

### Fixed

- Git operations no longer prompt for credentials — `GIT_TERMINAL_PROMPT=0` and `GIT_ASKPASS=echo` are now set for all git invocations, preventing interactive auth prompts from blocking or corrupting the JSON-RPC stdio channel
- MCP server checks source accessibility at startup — runs `git ls-remote` against each configured source URL before the host starts and writes a warning to stderr if any source is unreachable (e.g. due to missing credentials), surfacing auth failures immediately rather than on the first hanging tool call

## [0.6.0] - 2026-05-29

### Added

- MCP Resources for browsing sources and installed artifacts — three new resources exposed by `eru mcp`:
  - `eru://sources` lists all configured knowledge sources with URL, branch, and collection doc count
  - `eru://sources/{name}` returns config details and collection docs for a specific source
  - `eru://installed` lists all locally pulled artifacts from the lock file, grouped by source; missing files are flagged `[missing]`

## [0.5.1] - 2026-05-29

### Fixed

- MCP server no longer writes log output to stdout — default console logging providers are cleared at startup so ASP.NET Core log lines no longer corrupt the JSON-RPC stdio stream

## [0.5.0] - 2026-05-29

### Added

- `refresh_knowledge` MCP tool — triggers an on-demand sync of the knowledge cache without waiting for the next timer tick; returns a summary of sources refreshed, files cached, and any errors

### Fixed

- MCP server now incorporates cached source manifests into the effective config at startup — manifest files were previously invisible to `search_knowledge` and `CollectionCacheService` even after `eru source add` had cached them on disk
- `CollectionCacheService` now rebuilds the effective config on every refresh cycle: re-reads global and local config, re-fetches `.eru/manifest.json` from each source, and re-applies manifests — sources added after the MCP server starts and remote manifest changes are picked up automatically on the next tick

## [0.4.0] - 2026-05-29

### Added

- YAML frontmatter support in knowledge files — `description` and `tags` fields in a `---` block are automatically picked up by the `search_knowledge` MCP tool; applies to cached collection files, lock-file entries, and local `knowledge/` directories; configured description takes precedence over frontmatter; tags are merged (union, deduplicated)
- `eru source remove <name>` — remove a named source from local config (or global with `--global`); supports `--dryrun`
- `eru collection remove <collection> -f <source:path>` — remove a file reference from a collection; if it was the last file the collection entry is also removed; supports `--global` and `--dryrun`
- Indexed word search for `search_knowledge` MCP tool — per-file inverted index stored at `~/.cache/eru/index/` invalidated by content hash; all matching lines returned as excerpts (OR semantics) instead of just the first hit
- `SimpleScan` baseline backend for `search_knowledge` — reads files directly without indexing; selectable via `ERU_SEARCH_BACKEND=simple`
- `ck` semantic + keyword search backend for `search_knowledge` — uses `ck --hybrid` per candidate file; selectable via `ERU_SEARCH_BACKEND=ck`; requires `ck` to be installed and on PATH
- `IndexService` — background service that pre-builds the search index at MCP server startup and on each refresh tick; activates for `ERU_SEARCH_BACKEND=indexed` (word index) and `ERU_SEARCH_BACKEND=ck` (vector index); indexing runs in parallel across files and directories

## [0.3.0] - 2026-05-28

### Added

- `eru manifest init` — create `.eru/manifest.json` in a knowledge-source repo
- `eru manifest add <path>` — add a file or glob entry to the manifest with optional tags and description
- `eru manifest remove <path>` — remove an entry from the manifest by exact path
- `eru manifest verify` — resolve all manifest entries against local files; exits 1 if any match nothing

## [0.2.0] - 2026-05-28

### Added

- `--dryrun` flag for `collection create`, `collection add`, and `source add` — previews what would be written without modifying any config file

## [0.1.0] - 2026-05-28

### Added

- Initial release of the `eru` CLI tool
