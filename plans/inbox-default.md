# Plan: `eru inbox default <name>`

## Context
With several inboxes configured, `eru inbox send`/`process` fail ("multiple inboxes configured") unless `-i` is passed or `settings.defaultInbox` is hand-edited in config JSON. Add a command that sets it, modelled on `eru inbox remove`.

## Design
- Usage: `eru inbox default <name> [-g] [--dryrun] [-o format]`
- Scope follows `InboxRemove`: local `.eru/config.json` `Settings.DefaultInbox` by default; `-g` writes global `Defaults.DefaultInbox`. `Config.merge` (Config.fs:494-496) already resolves local over global, so no consumer changes.
- Validate `<name>` exists in the merged inboxes (local + global) before writing (pattern: InboxList.fs:19, InboxChannelRemove.fs:17). A local default may point at a global inbox.
- When `Settings`/`Defaults` is `None`, build an all-`None` record (see `emptyGlobal`, Init.fs:30-41); otherwise `{ s with DefaultInbox = Some name }` so other settings are preserved.
- Global: create an empty global config if none exists (as InboxAdd.fs:35). Local: error "no .eru/config.json found. Run 'eru init' first."
- Dry run returns "Would set ..." without writing.

## Changes
1. `src/Eru.Domain/InboxDefault.fs` (new; `Command = { Name; IsGlobal; DryRun }`, `execute deps cmd : Result<string,string>`); add to `Eru.Domain.fsproj` after `InboxRemove.fs` (line 30).
2. `src/Eru.Cli/Args.fs`: `InboxDefaultArgs` (copy `InboxRemoveArgs`, lines 215-226); add `| [<SubCommand>] Default of ParseResults<InboxDefaultArgs>` and usage text to `InboxArgs` (~line 342). Check no union-case name clash.
3. `src/Eru.Cli/InboxDefaultCli.fs` (new, copy of InboxRemoveCli.fs); add to `Eru.Cli.fsproj` after line 59; `open` + `| InboxDefaultCmd cmd -> ...` in `Program.fs` (lines 20, 77).
4. Update error text in `InboxSend.fs:115` and `InboxProcess.fs:54` to mention `eru inbox default <name>`.
5. Tests in `tests/Eru.Tests/InboxTests.fs` under an `InboxDefault` banner, using `makeDeps`/`newState`: local write, global write, unknown inbox (`assertError`), dry run, `Settings = None`, existing settings preserved. Update any InboxSend/Process tests asserting the old message.
6. Docs: `docs/reference/cli.md` (new `### eru inbox default` after remove, ~line 303), `docs/reference/config-file.md:186`, `README.md:106`, `CHANGELOG.md` `[Unreleased]` → `### Added`.

## Verification
- `dotnet build` and `dotnet test`.
- Manual: with two inboxes, `eru inbox default knowledge`, then `eru inbox send "hi"` succeeds without `-i`; `eru inbox default nope` errors; `-g` and `--dryrun` behave as described.
