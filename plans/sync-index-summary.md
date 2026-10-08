---
status: done
---

# Plan: Report index size in `eru sync`

## Context

`eru sync` printed only lock-file counts ("0 updated, 0 current…"), which read the same whether or not the source index was populated. That hid a bug where OKF sources ended up with an empty index from the second sync on (the manifest reseed in Step 1c wiped discovered entries while the HEAD SHA gate skipped rediscovery). Search then returned nothing with no signal why.

## Design

- `Sync.IndexSummary = { Source; Entries; Bundles }` with bundles rendered `<kind>:<path>` (`/` for root); `SyncResult` gains `Indexes`.
- `Sync.summarizeIndexes deps` merges global and local config and reads each source's stored index after `populateIndex`. It reads back rather than changing `populateIndex`'s signature, which `KnowledgeSyncService`, `SiteServeServer`, `BrowseWindow` and `SourceFilesCli` also call.
- `Sync.indexWarning` (pure) returns a message for a source with 0 entries: "no bundles registered" or "bundles registered, but okf concept files need a non-empty `type`".
- CLI (`SyncCli.fs`): text and table output print one `<source>: N entries indexed (<bundles>)` line per source after the summary, warnings go to stderr, nothing on `--dryrun`. JSON carries `indexes` always.
- Additive only: existing table/text output is unchanged apart from the new trailing lines; the exit code is unchanged (warnings do not fail the command).

## Changes

1. `src/Eru.Domain/Sync.fs`: types, `indexWarning`, `summarizeIndexes`, `execute` fills `Indexes`.
2. `src/Eru.Cli/SyncCli.fs`: `renderIndexes` for text and table.
3. `tests/Eru.Tests/SyncTests.fs`: index size stable across repeated syncs, `indexWarning` messages, zero-entry source with no bundles.
4. `docs/reference/cli.md`, `CHANGELOG.md`.

## Verification

- `dotnet test`.
- Manual: `eru sync` twice in a project with an OKF source shows the same non-zero count; `-o json` includes `indexes`.
