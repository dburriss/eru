# Plan: enable/disable toggle for knowledge sources

## Context
Sources have no on/off switch today (the only bool on a source is `AllowBinaries`, a files setting). Users want to switch a source off without removing it, so its config, cache and lock entries survive. Decisions: a disabled source is fully inert; it is toggled by a config field, CLI commands and an env override; collections referencing a disabled source skip it with a warning.

## Changes

1. **Type** — `src/Eru.Domain/Config.fs`
   - Add `Enabled: bool option` to `SourceConfig` (lines ~180-189). `None` means enabled.
   - Update `SourceConfig.empty` (~269) and `hasOverrides` (~266).
   - Fix every record literal: `SourceAdd.fs:99-105`, `Add.fs` (~208, ~215), `Config.fs:~948` (V1 migration), test fixtures.
   - Check that the reflection-based serializer (`Eru.Adapters/ConfigAdapter.fs`, `Serialization.fs`) round-trips the option and omits `None`.

2. **Merge and effective config** — `Config.fs`
   - `mergeOverrides` (675-697): carry `Enabled`, so a local entry can disable a global source.
   - `mergeWithEnv` (~753): filter out sources where `Enabled = Some false` when building `EffectiveConfig.Sources`. This makes sync, search, site, MCP and the link graph honor it with no per-consumer changes.
   - Add a helper such as `isEnabled`.

3. **Env override** — `src/Eru.Domain/EnvConfig.fs`
   - Add `enabled` to `sourceVocabulary` (~45), the key mapping (~129) and the warning list (~191-196).
   - Parse the value as a bool, and warn on an invalid one.
   - Wire it through `applyEnvSourceOverrides` (Config.fs ~739).

4. **Collections** — `Config.checkCollectionSources` and `withManifests` (~875)
   - A reference to a disabled source is skipped and produces a warning, not an error.
   - Distinguish "disabled" from "unknown" so unknown names still error.

5. **CLI** — `eru source enable|disable <name>`
   - Add `src/Eru.Domain/SourceToggle.fs` and `src/Eru.Cli/SourceToggleCli.fs`, modelled on `SourceBundleAdd.fs:76-131`. Find the source in local or global config, set `Enabled`, and write via `deps.WriteLocalConfig` or `deps.WriteGlobalConfig`.
   - Add the cases in `Args.fs` (~190-195) and wire them in `Program`.
   - Setting `Enabled` to `None` when re-enabling keeps the config file clean.
   - `SourceList.fs:28-29`, `SourceView.fs:35-36` and `SourceFiles.fs:43-44` read raw config. Show a "disabled" marker in list and view. `SourceFiles` should say the source is disabled.
   - `ConfigCmd.fs:183-190` lists `eff.Sources`, so disabled sources won't appear. Decide in implementation whether to read raw config there too.

6. **Docs and changelog**
   - `docs/reference/config-file.md` (field table 54-72)
   - `docs/reference/cli.md` (136-248)
   - `docs/reference/environment-variables.md`
   - `skills/eru/references/commands.md`
   - `CHANGELOG.md`

## Tests (`tests/Eru.Tests/`)
- `ConfigTests.fs`: a local override disables a global source, and disabled sources are filtered from the effective config.
- `EnvConfigTests.fs`: parsing of `ENABLED`, valid and invalid.
- `SourceTests.fs` (or a new `SourceToggleTests.fs`): enable/disable commands in local and global config, an unknown name, and the list marker.
- `CollectionTests.fs`: a disabled source is skipped with a warning.
- `SyncTests.fs` / `SearchTests.fs`: a disabled source is not synced or searched.

## Verification
- `dotnet build` and `dotnet test`.
- Manually, in a scratch dir: `eru source add ...`, `eru source disable X`, then `eru source list` (shows disabled), `eru sync` (skips X) and `eru search` (no X results). Then `eru source enable X` restores it, and `ERU__SOURCE__X__ENABLED=false` disables it without editing config.
