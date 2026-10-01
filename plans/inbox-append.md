# Plan: `--append <text|@file>` for `eru inbox process` / `eru inbox watch`

## Context
The built-in ingestor instructions end with a "commit" step, which is wrong for flavours that must not commit (gh-aw uses `create_pull_request`). Instead of config, callers pass flavour-specific closing text per invocation. The text is inserted after the resolved instructions and before the item. Nothing is stored in config. No env var for now.

## Changes

1. **`src/Eru.Cli/Args.fs`**: add repeatable `Append of text: string` (no `[<Unique>]`) to `InboxProcessArgs` (~l.302) and `InboxWatchArgs` (~l.319), plus `| Append _ -> "..."` in both `Usage` members. Follow the `Agent_Args` / `Tag` repeatable precedent.
2. **`src/Eru.Domain/InboxProcess.fs`**
   - Add `Append : string list` to `Options` (l.8-15).
   - Add a pure resolver `resolveAppend deps raw : Result<string,string>`: if `raw` starts with `@`, read the path via `deps.ReadLocalFile` (relative paths resolved against `deps.GetCwd()`); `Ok None` gives `Error "append file '...' not found."`; if it starts with `@@`, return the text with one leading `@` dropped (literal escape, e.g. `@@mention` → `@mention`); otherwise return the text unchanged. Add a test and a docs note for the `@@` escape.
   - In `executeCore` (l.287), resolve all appends once, before the `opts.DryRun` branch (l.322), so a bad `@file` fails fast even on `--dryrun`. Watch re-reads on each batch.
   - Thread the resolved `string list` through `processAll` → `processOne` → `buildPrompt` (l.203-220).
   - `buildPrompt`: `instructions.TrimEnd() + (appends |> List.map (fun a -> "\n\n" + a.Trim()) |> String.concat "") + "\n\n---\n\nCurate the following raw inbox item:\n\n" + itemContent`. Pieces are joined in flag order.
3. **CLI handlers**: set `Append = r.GetResults ...Append` in the active patterns of `src/Eru.Cli/InboxProcessCli.fs` (l.11-35) and `src/Eru.Cli/InboxWatchCli.fs` (l.21-40). These are the only constructors of `Options` in `src`.
4. **`agents/ingestor.agent.md`**: remove step 7 (Commit, l.134-150) so the built-in default is commit-neutral. Also fix the other commit mentions (l.8 "one commit per raw item", l.36 "commit before moving to…", l.38 "commits made"). Keep the sentence "You curate raw captured material in `inbox/raw/`", because a test asserts it (InboxProcessTests.fs l.359).
5. **Docs**
   - `docs/reference/cli.md`: usage lines and flag-table rows for process (l.377-421) and watch (l.423-459), plus a short paragraph on ordering, repeatability and `@file`.
   - `skills/eru/references/commands.md`: usage lines (l.258, l.283), a process table row, and a watch sentence.
   - `CHANGELOG.md`: a bullet at the end of `## Added` under Unreleased. Also note under Changed that the built-in instructions no longer commit.
   - `docs/how-to/generate-docs-from-inbox-with-gh-aw.md` is a candidate for a short "plain Action" example (`--append "Commit one change set per item."`). I'll check its content before editing.
6. **Tests** (`tests/Eru.Tests/InboxProcessTests.fs`): add `Append = []` to `emptyOpts` (l.91). New tests, following the l.334 pattern:
   - literal text appears in the prompt after the instructions and before the item (ordering via `IndexOf`)
   - repeated appends keep their order
   - `@/path` is read via `ReadLocalFile` (add the path to the contents map)
   - a missing `@file` returns `Error` and `RunAgent` is never called
   - no appends leaves the prompt unchanged
   - the built-in default no longer mentions committing

## Verification
- `dotnet build` and `dotnet test` (whole suite, since `Options` and `emptyOpts` change).
- Manual: `eru inbox process --all --dryrun --append "X" --append @file.md`, then a real run against a scratch inbox with a stub agent to eyeball the prompt order. Also check a missing `@file` error.
