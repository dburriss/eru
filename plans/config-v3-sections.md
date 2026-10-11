# Plan: config v2 -> v3 (grouped sections, shared `empty` values)

## Context
`settings` (local) and `defaults` (global) are flat bags with prefixed keys (`siteIgnorePatterns`, `okfIgnorePatterns`, `inboxWatchIntervalSeconds`, `mcpRefreshIntervalMinutes`, ...). Every new `site*` key widens them, and ~19 test literals of `GlobalDefaults`/`LocalSettings` break each time. v3 (breaking) groups settings into top-level sections, drops `settings`/`defaults`, and adds shared `empty` values so fixtures survive new fields. Same migration approach as v1 -> v2 (`Config.fs:588-655`): migrate at the adapter boundary, no write-back until the next explicit save. todo.md:37 is the originating item.

## v3 JSON shape (both files; local overrides global per key)
```json
{
  "version": 3,
  "site":  { "ignorePatterns": [], "hideEmptyBundles": false },
  "okf":   { "ignorePatterns": [] },
  "mcp":   { "refreshIntervalMinutes": 60 },
  "sync":  { "branch": "main", "commitOnPull": false, "stateFile": "..." },
  "files": { "blockPatterns": [], "allowPatterns": [], "allowBinaries": false },
  "inbox":  { "default": "team", "watchIntervalSeconds": 30, "items": { "team": { } } },
  "source": { "items": [ ] },
  "collection": { "items": [ ] }
}
```
Decisions made with the user: all of site/okf/inbox/mcp move; sections are top-level in both files; `settings`/`defaults` removed entirely; inbox/source entities fold into their sections: section names are singular, the entity collection inside is always `items` (inbox: map by name; source: list). `inboxes`/`defaultInboxes`/`sources`/`defaultSources` are removed, so both files use identical keys; all newly created configs write version 3.
`collections` becomes `collection.items` (list, still merged by concatenation, not by name). Rule: every section is singular with entities under `items`, so settings can be added later as an additive change.
**Assumption to confirm:** the leftovers (`branch`, `commitOnPull`, `stateFile`, block/allow patterns) go into `sync` and `files` sections.
Because sections are shared by both files, `mcp`/`branch` (global-only today) and `stateFile` (local-only today) become valid in either file; merge is local-over-global for all. Small loosening, noted in the changelog.

## Changes

### 1. Domain types (`src/Eru.Domain/Config.fs`)
- Add section records `SiteSection`, `OkfSection`, `InboxSection`, `McpSection`, `SyncSection`, `FilesSection` (all fields option), each with `empty`.
- `GlobalConfig` / `LocalConfig`: replace `Defaults` / `Settings` with the six optional section fields. Add `GlobalConfig.empty`, `LocalConfig.empty` (`Version = 3`), `SourceConfig.empty`, and defaults-populated `EffectiveConfig.empty` (reuse `defaultSiteIgnorePatterns` etc., `Config.fs:337-344`). Remove the private `emptyGlobalDefaults`/`emptyLocalSettings` in `InboxDefault.fs:11-34`.
- `InboxSection` gains `Items: Map<string, InboxConfig> option`; add `SourceSection = { Items: SourceConfig list option }` and `CollectionSection = { Items: CollectionConfig list option }`. Merge semantics are unchanged: inboxes and sources merge by name, local wins (`Config.fs:389-440`, `:504-512`), with the existing source URL/duplicate-name validation.
- `supportedVersion = 3` (`Config.fs:355`). `EffectiveConfig` stays flat; only `merge` (`Config.fs:405-549`) changes to read section fields, keeping the same defaults and `resolveOkfIgnorePatterns` (`:346`).

### 2. Migration (the part to get right)
- Keep `*V1` types. Add `SourceConfigV2`-compatible `GlobalConfigV2`/`LocalConfigV2` with the old flat `GlobalDefaults`/`LocalSettings` (renamed `GlobalDefaultsV2`/`LocalSettingsV2`).
- `migrateGlobalV2` / `migrateLocalV2`: field-by-field flat -> section; `Version = 3`. Sections with no populated fields become `None` so output stays tidy.
- Reader flow in `readAndMigrate{Global,Local}Json` (take an extra `deserializeV2`):
  1. Probe `"version"` with `JsonDocument` (v2 -> v3 flat-to-nested will NOT fail deserialization, since System.Text.Json ignores unknown keys, so the current "try current, fall back on Error" trigger can't be used).
  2. `version >= 3` -> current shape (plus null normalisation as today); `> 3` -> existing `checkVersion` error.
  3. `version <= 2` -> try V2 shape; on Error try V1 shape then `migrateV1` then `migrateV2`. This is needed because init/fallbacks stamped `Version = 1` on files that already had the v2 `Bundles` shape, so the version number alone can't pick V1.
- Wire `ConfigAdapter.fs:8-37` to pass the V2 deserializers. No write-back on read.

### 3. Call sites
- Rewrite field accesses: `InboxDefault.fs:53,63` (writes `inbox.default`), `Init.fs:11-45` (JSON scaffold string at `:11` edited by hand; `emptyGlobal`), `Sync.fs:143-153` and `McpServer.fs:26-39` (swap literals for `EffectiveConfig.empty`). Read-side consumers use `EffectiveConfig` and are unaffected.
- Entity commands move to the new paths (~20 domain files touch `DefaultInboxes`/`DefaultSources`/`Inboxes`/`Sources`: `Inbox*`, `Source*`, `Collection*`, `Add`, `Init`, `SourceFiles`); `Collections` call sites (`Config.fs:435-436,531-533`, `Collection*`) move too; `normalizeBundles`/`normalizeInboxMap` null-defence (`Config.fs:610-652`) applies to the new fields.
- Update the six `Version = 1` fallbacks to `GlobalConfig.empty`: `Add.fs:212`, `CollectionCreate.fs:24`, `InboxAdd.fs:35`, `SourceAdd.fs:125`, `CollectionAddFile.fs:26`, `InboxDefault.fs:51`.
- Help text `Args.fs:350` (`settings.inboxWatchIntervalSeconds` -> `inbox.watchIntervalSeconds`).

### 4. Tests (xUnit v3)
- Convert fixtures to `{ GlobalConfig.empty with ... }` / `LocalConfig.empty` / `EffectiveConfig.empty`: ~80 literals across 13 files (`ConfigTests`, `InboxTests`, `SyncTests` heaviest); `IndexBuilderTests.fs:~62`, `SiteGeneratorGraphTests.fs:~70`; assertions in `InitTests.fs:92-96`, `InboxTests.fs:300-318`.
- New migration tests beside `ConfigTests.fs:351-400`: v2 flat JSON -> v3 sections (each key mapped, partial/absent sections -> `None`), v1 JSON -> v3 chain, version-1-stamped file with v2 shape, version 3 round-trip via real `Serialization` (no such test exists today), version 4 rejected, local-over-global merge per section key.

### 5. Docs and housekeeping
- `docs/reference/config-file.md` (largest rewrite: shapes 28-48, version note 51, settings table 178-188, examples 202-235, merge 245-248, scaffold 257); fix its "version 1" mentions (31,43,51,202,223). Also document the entity moves: `inboxes`/`defaultInboxes` -> `inbox.items`, `sources`/`defaultSources` -> `source.items`, `collections` -> `collection.items`; the "section names singular, entities under `items`" rule; that global and local files now use identical keys; and merge semantics per section (scalars local-over-global per key; inbox/source items by name, local wins; collection items concatenated). Every example and the `eru init` scaffold must use the new keys.
- Other docs: `how-to/publish-the-site-to-github-pages.md:22`, `reference/site-generation.md:53`, `how-to/set-up-the-mcp-server.md:78`, `reference/mcp-server.md:18`, `how-to/use-send-and-watch-locally.md:47`, `reference/cli.md:321,490,885`, `skills/eru/references/{commands,mcp}.md`.
- `CHANGELOG.md` `[Unreleased]`: **Breaking** entry with the old -> new key table (settings keys plus `inboxes`/`defaultInboxes` -> `inbox.items`, `sources`/`defaultSources` -> `source.items`, `collections` -> `collection.items`), auto-migration note, and the new "sections valid in both files" loosening.
- Grep docs/skills for the old entity keys after editing; today only `docs/reference/config-file.md` mentions them, but the other doc files above may show example configs or `eru ... add` output that references them.
- Tick off `todo.md:37`.

## Verification
- `dotnet test --solution eru.slnx` (all green); targeted: `-- --filter-method "*Config*"`.
- Manual: take a real v2 `.eru/config.json` and `~/.config/eru` global file (copies), run `eru sync` / `eru site generate` and check behaviour is unchanged and the file is untouched; run `eru inbox default <name>` and confirm the file is rewritten in v3 with an `inbox.default` key; run `eru init` in a temp dir and confirm `"version": 3` and sections.
- Grep for leftover flat names: `siteIgnorePatterns|siteHideEmptyBundles|okfIgnorePatterns|inboxWatchIntervalSeconds|mcpRefreshIntervalMinutes` should only remain in V2 migration types, changelog, and old plans. Same for `defaultInboxes|defaultSources|DefaultInboxes|DefaultSources`.
