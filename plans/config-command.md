---
status: done
---
# Plan: `eru config` command (list / view / get / set / migrate)

## Context
Config is layered (global `~/.config/eru/config.json`, local `.eru/config.json`), versioned (current v3), and silently migrated in memory on read; files are only rewritten on the next explicit save. Users have no way to see which configs are active, what the merged result is (and where values come from), or to upgrade a file on disk deliberately. Add `eru config list | view | get | set | migrate`.

Follows the `print` command pattern (domain fn + Args + `*Cli.fs` + wiring).

Decisions (confirmed):
- `list` shows files + version; `view` shows effective values with origin; `migrate` has `--local`/`--global`, backup and `--dryrun`.
- `get`/`set` use dotted keys and support any value including lists.
- FSharp.Data.YamlValue is not used: config is JSON and AGENTS.md mandates `System.Text.Json` only.

## Subcommands
- `eru config list`: one row per config file (global, local): scope, path, exists/missing, detected version, "needs migration" flag (version < `Config.supportedVersion`). Table/json via `OutputFormat.parseFormat`.
- `eru config view`: merged effective config with provenance: each setting shown with value and origin (`local` | `global` | `default`). Sources/inboxes/collections show per-item origin (local, global, manifest). Default text/table; `--format json` for machine output.
- `eru config get <key>`: dotted key (e.g. `sync.commitOnPull`, `files.blockPatterns`); prints effective value + origin. Lists and scalars supported.
- `eru config set <key> <json-value> [--global] [--unset]`: writes to local by default, `--global` for global. Value parsed as JSON (falls back to raw string for bare words); lists replace the whole list. `--unset` removes the key. Implemented with `System.Text.Json.Nodes.JsonNode` on the raw file (walk/create path, set/remove), then validated by round-tripping through `readAndMigrate*Json` + `Config.merge` before writing; invalid results are rejected and nothing is written. Refuses if the file is an outdated version (tells user to run `config migrate`). Sources/inboxes/collections keep their dedicated commands.
- `eru config migrate [--local | --global] [--dryrun]`: default `--local`. Reads raw file, detects version, migrates to v3 using the existing migration chain, writes `<file>.bak` of the original, then writes v3. `--dryrun` prints resulting JSON, writes nothing. No-op message if already current; error if version > supported.

## Approach
1. **Domain** `src/Eru.Domain/ConfigCmd.fs` (new, after `Read.fs`):
   - `list deps` returns `{Scope; Path; Exists; Version option; NeedsMigration}` records.
   - `view deps` returns effective config + origin map. Provenance mirrors the `first local global` logic in `Config.merge` (same field set: sync.*, mcp.*, files.*, site.*, okf.*, inbox.*; sources/inboxes by name; collections concatenated).
   - `get`, `set`, `migrate deps scope dryRun` as described above.
2. **IO record** (as built): a separate `ConfigIO` record in `ConfigCmd.fs` instead of new `Deps` fields (avoids touching every test's `Deps` stub), built by `ConfigAdapter.configIO`. Originally: `Deps` needs raw-read access (path + raw JSON text) since `ReadLocalConfig` hides the version. Add to `src/Eru.Domain/Deps.fs`: `ConfigPath: scope -> string`, `ReadConfigRaw: scope -> Result<string option,string>`, `WriteConfigRaw`, and a backup/copy op. Implement in `src/Eru.Adapters/ConfigAdapter.fs`, wire in `AdapterDeps.fs`, reusing `Paths.localConfigPath/globalConfigPath`, `Config.readAndMigrate{Global,Local}Json` and `writeLocalConfig/writeGlobalConfig`.
3. **Version probe**: expose the private `probe` in `Config.fs` (version + `isV1Shape`) so `list` can report the on-disk version (missing version or `isV1Shape` counts as 1).
4. **CLI**: `src/Eru.Cli/Args.fs`: `ConfigArgs` nested sub-union with `[<CliPrefix(CliPrefix.None)>]` (like `Source`/`Cache`), plus per-subcommand arg unions (`migrate`: `--local`, `--global`, `--dryrun`; `set`: key, value, `--global`, `--unset`; `get`: key). Register `Config` in `EruArgs`. New `src/Eru.Cli/ConfigCli.fs` (Cmd record, active pattern, `run`), add to `Eru.Cli.fsproj` before `Program.fs`, wire `ConfigCmd` in `Program.fs` `main`.
5. **Tests** `tests/Eru.Tests/ConfigCmdTests.fs` (register in `Eru.Tests.fsproj`) using the `makeDeps` stub pattern from `ReadTests.fs`:
   - list: missing / v1 / v2 / v3 files.
   - view/get: provenance (local override, global-only, default).
   - set: scalar, list replace, `--unset`, invalid value rejected with no write, outdated file refused.
   - migrate: dry-run, backup written, v3 no-op, v4 rejected.
   - Round trip: migrated output re-read equals original merged result.
6. **Docs**: `CHANGELOG.md` `[Unreleased]` + `PackageReleaseNotes` in `Eru.Cli.fsproj`; `docs/reference/cli.md` (`## eru config`); `docs/reference/config-file.md` ("Upgrading from earlier versions"); `README.md`; `skills/eru/SKILL.md` + `skills/eru/references/commands.md`.

## Notes / constraints
- Domain paths use `/` and `PathJoin.Combine` (`DomainPathConventionTests`); build the backup name by string append, not `System.IO.Path`.
- No third-party JSON libs; use `System.Text.Json` through existing `Serialization`.
- Migration only writes the current shape; raw `site` `JsonElement`s (cards/layout/pages/document) must survive the round trip.

## Verification
- `dotnet test --solution eru.slnx -- --filter-method "*ConfigCmd*"`, then the full suite.
- Manual: in a temp dir with hand-written v1, v2 and v3 `.eru/config.json`, run `eru config list`, `eru config view` (global + local overriding a value), `eru config get`/`set`, `eru config migrate --dryrun`, then `eru config migrate`; confirm `.bak` exists and `eru config list` shows v3.
