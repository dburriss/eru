# Per-source configuration

## Context
Only `url`, `branch` and `bundles` are per-source today. File filtering, search ignore, OKF and site ignore settings are global (`EffectiveConfig`). Goal: let each entry in `source.items` override those settings. Decisions: overrides **replace** the global value (per key), and live **inline** in the source entry in both global and local config.

## Config shape
```json
{ "source": { "items": [{
  "name": "docs", "url": "...",
  "files":  { "blockPatterns": [], "allowPatterns": [], "allowBinaries": true },
  "search": { "ignorePatterns": ["CHANGELOG.md"] },
  "okf":    { "ignorePatterns": [], "fixTypes": false, "defaultType": "guide" },
  "site":   { "ignorePatterns": [] }
}]}}
```
All blocks and keys optional. Absent key means fall back to the existing global resolution (env > local > global > default).

## Changes
1. **Domain types** (`src/Eru.Domain/Config.fs:39`): add optional `Files: FilesSection option`, `Search`, `Okf`, `Site` to `SourceConfig`, reusing the existing section records. For `site`, only `ignorePatterns` is honoured (validate/reject other keys). For `search`, only `ignorePatterns` (mode stays global, since `Searcher` builds one index per mode).
2. **Resolution** (`Config.resolveLocalSources`, line 646): when a local entry borrows from the global entry of the same name, merge per key with local winning, then global.
3. **Effective per-source view**: add `Config.effectiveFor (eff) (src) : SourceEffective` returning the resolved block/allow/allowBinaries, search ignore, OKF and site ignore for one source, falling back to `eff` globals. This is the single place the replace rule lives.
4. **Consumers**, switching from the global `eff.*` to `effectiveFor`:
   - `Sync.fs`: `executeLocked` (lines 506-519, block/allow), `populateIndex` OKF discovery (line 232).
   - `Add.fs` `pullOne`/`pullMany` (253-308) and `Read.fs:67-76`: look up the source by name and use its effective block settings.
   - `CandidateBuilder.build` (line 35 loop) and `RankedSearch.applyIgnore` (196, 223): apply search ignore per candidate by its source instead of on the combined set.
   - `SourceBundleAdd.fs:141` (okf defaults).
   - `SiteGenerator.fs`: per-source site ignore.
5. **Validation**: extend `checkGlobalSourceUrls`-style checks to validate per-source blocks (bad globs, invalid `okf.defaultType`, unsupported keys).
6. **CLI get/set** (in `ConfigCmd.fs`, reusing `knownSettings`, `settableSections` and `resolve`, lines 101-190):
   - `eru config get <key> --source <name>` returns the effective value for that source and its origin (`FromSource`, `FromEnv`, `FromLocal`, `FromGlobal`, `Default`). `eru config view --source <name>` shows all per-source keys.
   - `eru config set <key> <value> --source <name> [--global]` writes into the matching `source.items[]` entry in local config (or global with `--global`). The source must already exist. A local entry that borrows its url from the global one is allowed, so set creates a local stub entry with no `url`. `eru config unset <key> --source <name>` is the inverse, so a source can fall back to the global value.
   - Only the four supported keys are settable per source (files.*, search.ignorePatterns, okf.*, site.ignorePatterns). Others are rejected with a clear message.
   - Front-end in `src/Eru.Cli`, the Argu `--source` option on the existing config subcommands, so no new top-level command.
7. **Env vars** (`src/Eru.Domain/EnvConfig.fs`): add `ERU__SOURCE__<NAME>__<SECTION>__<KEY>`.
   - `EnvConfig.read` currently excludes `source` from `vocabulary`. Add a per-source vocabulary containing the four supported sections and keys, reusing `listPaths`, `stringPaths` and `boolPaths` typing.
   - Source names are case-sensitive everywhere else (`s.Name = name`, `checkDuplicateNames`), so `--source`, JSON lookups and `config get/set` keep exact matching. Only the env layer is loose, because env var names are upper-case by convention.
   - `<NAME>` is matched case-insensitively against the merged source names, with `-` and `.` mapped to `_`. Unknown names produce a warning and are ignored. If two or more source names normalize to the same string (e.g. `Docs` and `docs`), an env var targeting them is ambiguous and is an error from `validateEnv`.
   - The env layer carries these as per-source override blocks that `effectiveFor` applies at the top of the precedence chain.
   - It cannot create sources or set `url`, `branch` or `bundles`.
8. **Serialization**: check that `Serialization.deserialize` handles the nested optional records and round-trips them through `SourceAdd` and `SourceRemove` without dropping fields.
9. **Docs/changelog**: update `docs/reference/config-file.md` (source entry schema, precedence step), `docs/reference/environment-variables.md` (the `ERU__SOURCE__<NAME>__...` form and its name rules), `docs/reference/cli.md`, `CHANGELOG.md`, and `skills/eru/references/commands.md` for the new `--source` option.

## Tests (`tests/Eru.Tests`)
- `ConfigTests.fs`: per-source replace, fallback to global when absent, local-over-global borrow merge, validation errors, v3 round trip.
- `SyncTests.fs` / `AddTests.fs` / `ReadTests.fs`: block and allow applied per source (one source allows binaries, another doesn't).
- `RankedSearchTests.fs` / `SearchTests.fs`: search ignore applies only to the owning source.
- `SourceTests.fs`: `source add/remove` preserves the override blocks.
- `ConfigCmdTests.fs`: get/set/unset with `--source`, origin reporting, rejecting unsupported keys and unknown sources, local stub for a global source.
- `EnvConfigTests.fs`: per-source env parsing, name normalization, unknown-name warning, collision error, precedence over file values.

## Verification
`dotnet build` and `dotnet test`. Then, with a scratch `.eru/config.json` with two sources and different `files.allowBinaries` and `search.ignorePatterns`, run `eru sync`, `eru search` and `eru config get` to confirm each source behaves independently.

## Notes
- AGENTS.md: no code until you say "implement". No `Co-Authored-By` or attribution trailers on commits or PRs (this overrides the default attribution reminder).
