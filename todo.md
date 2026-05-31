# TODO

- [ ] `eru rm` to remove file and lock entry for a specific artifact; supports `--global` and `--dryrun`
- [ ] `eru disconnect` to remove lock entries for a source without deleting the source config; supports `--global` and `--dryrun`
- [ ] support --text and --json and --table output modes on all commands with default as table. Use https://spectreconsole.net/console/
- [ ] `eru add --target` to support specifying the filename too (should reflect in the lock file as well)
- [ ] fix: `eru source files` should show description and tags from manifest if available, not just the config description; also show the source name in the listing for easier reference. Should pull them from files too if available, not just the manifest file.
- [ ] Add a spinner for `eru sync` https://spectreconsole.net/console/tutorials/status-spinners-tutorial