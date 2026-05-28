# Changelog

## [Unreleased]
### Added
- `eru source remove <name>` — remove a named source from local config (or global with `--global`); supports `--dryrun`
- `eru collection remove <collection> -f <source:path>` — remove a file reference from a collection; if it was the last file the collection entry is also removed; supports `--global` and `--dryrun`

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
