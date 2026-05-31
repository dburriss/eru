# TODO

- [x] `eru rm` to remove file and lock entry for a specific artifact; supports `--output` and `--dryrun`
- [x] `eru disconnect` to remove lock entries for a source without deleting the source config; supports `--output` and `--dryrun`
- [x] support --text and --json and --table output modes on all commands with default as table. Use https://spectreconsole.net/console/
- [x] `eru add --target` to support specifying the filename too (should reflect in the lock file as well); if full path including filename is provided, use that; if just a directory, use that as the target directory and keep the original filename; if not provided, default to the current behavior of using the source path as the filename in the cache
- [ ] fix: `eru source files` should show description and tags from manifest if available, not just the config description; also show the source name in the listing for easier reference. Should pull them from files too if available, not just the manifest file.
- [x] Add a spinner for `eru sync` https://spectreconsole.net/console/tutorials/status-spinners-tutorial
- [x] Use spinner on `eru add`, `eru source files`, and anything else using sync under the hood
- [ ] `eru files` command to list all cached files across sources with metadata from the manifest and lock file
- [ ] `eru search` command to --remote to search across all sources without needing to sync first
- [ ] eru.lock move to .eru/lock.json to avoid cluttering the home directory; update all commands to look for the lock file there