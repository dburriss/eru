# TODO

- [x] `eru rm` to remove file and lock entry for a specific artifact; supports `--output` and `--dryrun`
- [x] `eru disconnect` to remove lock entries for a source without deleting the source config; supports `--output` and `--dryrun`
- [x] support --text and --json and --table output modes on all commands with default as table. Use https://spectreconsole.net/console/
- [x] `eru add --target` to support specifying the filename too (should reflect in the lock file as well); if full path including filename is provided, use that; if just a directory, use that as the target directory and keep the original filename; if not provided, default to the current behavior of using the source path as the filename in the cache
- [x] Add a spinner for `eru sync` https://spectreconsole.net/console/tutorials/status-spinners-tutorial
- [x] Use spinner on `eru add`, `eru source files`, and anything else using sync under the hood
- [x] harmonize cache, manifest, collections, and lock file so searchable and single sync between mcp and cli; plan is unified-cache-index.md
- [x] plans/sync-batch-fetch.md to fetch per source instead of per file
- [x] `eru cache clear` command to clear all cache indexes and files; supports `--dryrun` and `--output` flags
- [x] eru source files Files heading should be named Items since it includes globs and directories, not just files
- [ ] `eru source files` should have hash as first column
- [ ] Move eru.lock to .eru/ to avoid cluttering repos with a new top-level file; update all relevant paths in code and docs
- [ ] TUI for browsing sources and lock file entries; maybe start with a `eru browse` command that lists all sources and their files in a navigable console UI using Spectre.Console's tree and table components, with details on selection. https://spectreconsole.net/console and https://gui-cs.github.io/Terminal.Gui/docs/index.html