# Plan: `inbox channel add` sets `defaultChannel` instead of duplicating an agent onto `default`

## Context
`eru inbox channel add <inbox> <channel> --agent-command ...` copies the agent onto a literal `default` channel
(`withDefaultAgentFallback`, `src/Eru.Domain/InboxChannelAdd.fs`) because `inbox send` falls back to `"default"`
when no `-c` and no `defaultChannel` are set (`InboxSend.fs:144`). The result is two identical agent blocks in config,
even when `defaultChannel` is already set (in which case the copy is never used).
Goal: no duplicated agent block, and `default` is just a normal channel name in `channel add` (no special casing).

## Behaviour
When the added channel has an agent and the inbox has no `defaultChannel`, set `defaultChannel = Some <new channel>`.
Otherwise unchanged. No `default` channel is ever auto-created or auto-wired. (`inbox send`/`process`/`list` still fall
back to the literal `"default"` when `defaultChannel` is unset; not touched.)

## Changes
1. `src/Eru.Domain/InboxChannelAdd.fs`
   - Delete `withDefaultAgentFallback` and the `"default"` special-casing; replace with a small step:
     `if agent.IsSome && inbox.DefaultChannel.IsNone then { inbox with DefaultChannel = Some cmd.ChannelName } else inbox`.
   - `defaultAutoPopulated` becomes `agent.IsSome && inbox.DefaultChannel.IsNone` (rename e.g. `setsDefault`).
   - Success message when set: "Set it as the inbox's default channel." (dry-run text uses the same message).
2. `tests/Eru.Tests/InboxTests.fs` (~lines 194-235)
   - Replace "auto-populates channel 'default'" with: sets `defaultChannel`, no `default` channel created.
   - Replace "does not overwrite an already-configured agent on 'default'" and "does not double-apply ... 'default' itself" with:
     existing `defaultChannel` preserved; adding a channel named `default` is treated like any other.
   - Keep "does not auto-populate when the new channel has no agent" (assert `DefaultChannel = None`).
3. Docs: `docs/reference/cli.md:364-367` rewrite the paragraph (first agent channel becomes the inbox's default channel
   unless one is set); `CHANGELOG.md` `[Unreleased]` → `### Changed`.

## Not changed
- Existing configs (e.g. the `default` block in software-engineering-norms/.eru/config.json is removed by hand).
- `inbox send` / `inbox process` / `InboxList` resolution.

## Verification
- `dotnet build` and `dotnet test`.
- Manual: `eru init`, `eru inbox add kb <path>`, `eru inbox channel add kb eru --agent-command x` → config has
  `defaultChannel: "eru"` and no `default` channel; `eru inbox send "hi"` lands in `eru`; `eru inbox process` picks it up.
