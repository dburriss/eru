# Plan: Environment-variable overrides for config settings

## Context

eru reads settings only from `~/.config/eru/config.json` and `.eru/config.json`. Clients that launch `eru mcp` (Claude Desktop, VS Code, `.mcp.json`) can pass an `env` block but not flags, and the CLI and `eru browse` have the same need to override a setting without editing a file. Today only `ERU_SEARCH_MODE` is overridable.

Goal: every setting can be overridden by an env var, in the CLI, the TUI and the MCP server, with the env names mirroring the config file shape.

Decisions already made with the user:
- Precedence: CLI flag > **env** > local config > global config > built-in default.
- No exemptions: `files.*` is overridable like everything else.
- Naming mirrors the JSON shape using .NET's `__` nesting separator, case-insensitive: `ERU__SEARCH__MODE`, `ERU__MCP__REFRESHINTERVALMINUTES`, `ERU__FILES__ALLOWBINARIES`, `ERU__SITE__CARDS__COLUMNS`. Lists are indexed: `ERU__FILES__BLOCKPATTERNS__0`, `__1`. Nested objects follow the same rule: `ERU__OKF__FIXTYPES__0__PATTERN`.
- `ERU_SEARCH_MODE` stays as an alias.
- Custom reader, not `Microsoft.Extensions.Configuration`: no new package reference, and `Eru.Domain` stays pure. Production passes `Environment.GetEnvironmentVariables()` at the edge; tests pass a literal map.
- Scope is the settings sections only (`site, okf, search, mcp, sync, files, inbox`). `source.*`, `collection.*` and `inbox.items` stay file-only.

## Approach

Treat env as a third config layer with the same shape as the file, not as per-key special cases.

1. **Env to a config record.** A pure function takes an env map (`string -> string`) and builds a `JsonObject` shaped like the config file: split names on `__`, lower-case the first segment match against known sections, fold numeric segments into arrays, coerce scalars by the value's JSON type (bool, int, string). It then goes through the existing parse/deserialize path into a `LocalConfig`, so type checks and validation errors reuse the existing code (e.g. `search.mode must be one of lex, sem, hybrid`). Unknown `ERU__*` names, and any under `source`, `collection` or `inbox.items`, produce a warning on stderr and are ignored.
2. **Overlay in resolution.** Add the env record as a layer above local in `Config.merge`'s per-key resolution (`first` helper, `Config.fs:544`) and in `SiteSettings.merge` for the site keys. The standalone `resolveOkf*` / `resolveSearch*` helpers (`Config.fs:547-585`) take the same layer, because `okf.fixTypes`, `okf.defaultType` and the structured `site.*` values have no `EffectiveConfig` field and are read from raw configs by `OkfFixCli.fs:63`, `OkfValidateCli.fs:55` and `SourceBundleAdd.fs:140`.
3. **Keep `merge` source-compatible.** Add `Config.mergeWithEnv env global local`; `Config.merge` stays as `mergeWithEnv None`, so the 54 existing test call sites don't change. Production call sites move to the env-aware variant.
4. **Wire the env record into `Deps`.** Add a `ReadEnvConfig` function next to `ReadGlobalConfig` / `ReadLocalConfig` (`AdapterDeps.fs:43-44`) and pass it at the production `merge` call sites. These are a repeated pattern, so migrate them mechanically:
   - Domain: `Sync.fs:141,420,574`, `Search.fs:37`, `Read.fs:93`, `Add.fs:230`, `InboxSend.fs:127`, `InboxProcess.fs:316,389,426`, `LinkGraph.fs:91`, `Remove.fs:23`, `Disconnect.fs:23`.
   - CLI: `SiteGenerateCli.fs:24`, `SiteServeCli.fs:20`, `SearchCli.fs:124`, `InboxWatchCli.fs:88`.
   - TUI: `BrowseTui.fs:18`, `Browse/BrowseWindow.fs:90`, `Browse/FilesPane.fs:301`.
   - MCP: `McpServer.fs:24`, `Eru.Search/KnowledgeSyncService.fs:23` (`buildEff`, which re-merges on every refresh, so env must apply here too or the first refresh drops it).
5. **Write paths stay raw.** `eru config set`, `eru inbox default`, `eru source add` etc. already read and write raw `GlobalConfig` / `LocalConfig` / JSON, so env cannot leak into saved files. Keep it that way: `ReadGlobalConfig` / `ReadLocalConfig` stay raw.
6. **`config set` validation ignores env.** `ConfigCmd.fs:230,233` calls `merge` to validate a value before writing; it keeps using the env-less `Config.merge`, so a bad env var can't block it and env can't mask validation of the value being written.
7. **`ERU_SEARCH_MODE` alias.** The env reader maps `ERU_SEARCH_MODE` to `ERU__SEARCH__MODE` (the `ERU__` form wins if both are set). `RankedSearch.resolveMode` (`RankedSearch.fs:74-99`) is left as is, so the deprecated `ERU_SEARCH_BACKEND` ordering is unchanged.
8. **Provenance in `config get` / `config view`.** Add `FromEnv` to `Origin` (`ConfigCmd.fs:34`, `originName` -> `"env"`) so `eru config get search.mode` shows where an overridden value came from. The TUI Config pane (`ConfigPane.fs`) is only a "coming soon" stub, so nothing to do there yet.
9. **Fail fast on bad env.** Many call sites swallow merge errors and fall back to `EffectiveConfig.empty` (`Sync.fs:141`, `McpServer.fs:24`), which would silently drop every setting on one bad env value. So validate the env layer once at startup (CLI entry, `eru browse`, `eru mcp`) and report the offending variable name; for MCP this goes to stderr only, since stdout is the protocol channel.
10. **Docs.** `docs/reference/config-file.md`: new "Environment variables" section (naming rule, list indexing, precedence) and an updated Merge-and-precedence list; `docs/how-to/set-up-the-mcp-server.md`: an `env` block example; `CHANGELOG.md` entry; update the `ERU_SEARCH_MODE` mentions to say it is an alias.

## Critical files

- `src/Eru.Domain/Config.fs` (layer, `mergeWithEnv`, `resolve*`), `src/Eru.Domain/SiteConfig.fs` (site merge)
- New env-reader module in `src/Eru.Domain` (pure; takes an env map)
- `src/Eru.Adapters/AdapterDeps.fs` (new `ReadEnvConfig`), `Deps` definition
- `src/Eru.Domain/ConfigCmd.fs` (`FromEnv`, env-less validation)
- Call sites listed in step 4

## Verification

- Unit tests in `tests/Eru.Tests` matching `ConfigTests.fs` style (`unwrapOk`, `assertError`, `{ LocalConfig.empty with ... }`), all using an injected env map, no `SetEnvironmentVariable` (env is process-global and tests run in parallel):
  - env beats local beats global beats default for a scalar, a bool, an int and a list;
  - indexed lists and nested `okf.fixTypes` / `site.cards.columns` parse;
  - `files.blockPatterns` is overridable (no exemption);
  - bad value (`ERU__SEARCH__MODE=nope`, non-integer int key) names the variable;
  - unknown, `source.*` and `inbox.items` names warn and are ignored;
  - `ERU_SEARCH_MODE` alias, and `ERU__` wins over it;
  - `config set` never writes an env value into the file, and its validation ignores env.
- `dotnet build` and `dotnet test --solution eru.slnx`.
- Manual: `ERU__MCP__REFRESHINTERVALMINUTES=5 eru config get mcp.refreshIntervalMinutes` shows `5` with origin `env`; `ERU__SEARCH__MODE=hybrid eru search ...` runs hybrid; run `mise run mcp` with an `env` block, then trigger a refresh and confirm the setting survives it; run `eru browse` with `ERU__SYNC__STATEFILE` set and confirm the lock entries load from that file.
