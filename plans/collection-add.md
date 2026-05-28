---
status: done
---

# Plan: `eru collection create` and `eru collection add`

## Context

Collections (`CollectionConfig`) are curated groups of file references used by the MCP server and `eru add --collection`. Previously they could only be defined by manually editing JSON config files. These commands let users create collections and add file references to them from the CLI.

## Commands

```
eru collection create <name> [-t tag] [-d description] [-g]
eru collection add    <collection> --file source:path [-t tag] [-d description] [-g]
```

- `--global` / `-g`: write to global config (`~/.config/eru/config.json`); default is local `.eru/config.json`
- `--file` / `-f`: file reference as `source:remotePath` (e.g. `gh-source:docs/guide.md`)
- Tags repeat via `-t`; supported on both collection level (`create`) and file level (`add`)
- `--description` / `-d` supported on both
- `create` errors if the collection name already exists
- `add` errors if the collection is not found, or the `source:path` pair is already listed

## Domain changes

**`Config.fs`**: Added `Collections: CollectionConfig list` to `LocalConfig` (was global-only before). Updated `Config.merge` to validate and merge local collections into `EffectiveConfig.Collections` alongside global ones.

**`Collection.fs`** (new): Two domain functions — `Collection.create` and `Collection.addFile` — following the same `(deps: Deps) (cmd: XxxCommand) : int` pattern as `Source.add`.

**`Init.fs`**: Scaffold template updated to include `"collections": []` so newly initialised local configs include the field.

**`ConfigAdapter.fs`**: Added a null-guard after deserialising `LocalConfig` to coerce missing `collections` (from pre-existing configs) to `[]`.

## CLI wiring

Follows the `source` subcommand pattern: `CollectionArgs` DU with `Create` and `Add` sub-cases, active patterns in `CommandMapper.fs`, and match arms in `Program.fs`.

## Files changed

- `src/Eru.Domain/Config.fs`
- `src/Eru.Domain/Collection.fs` (new)
- `src/Eru.Domain/Eru.Domain.fsproj`
- `src/Eru.Domain/Init.fs`
- `src/Eru.Adapters/ConfigAdapter.fs`
- `src/Eru.Cli/Args.fs`
- `src/Eru.Cli/CommandMapper/CommandMapper.fs`
- `src/Eru.Cli/Program.fs`
- `tests/Eru.Tests/ConfigTests.fs`
- `tests/Eru.Tests/SourceTests.fs`
- `tests/Eru.Tests/AddTests.fs`
- `tests/Eru.Tests/SearchTests.fs`
- `tests/Eru.Tests/SyncTests.fs`
