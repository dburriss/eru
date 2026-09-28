---
status: done
---

# Plan: `eru inbox process` — curate a raw inbox item via an ACP agent

## Context

`eru inbox send` (`plans/inbox-send.md`) writes raw captures into
`<inbox.Path>/<rawPath>/<channel>/`. Today the only thing that turns those raw items
into structured OKF notes is a human-run periodic agent (`ingestor.md`, in the
knowledge repo) that reads `inbox/raw/<channel>/`, curates each item, and archives it
into `inbox/archive/<channel>/` — there is no `eru` command that does this, and no
processed/unprocessed state exists anywhere except "which directory the file is
currently sitting in."

This plan adds `eru inbox process`: given a configured inbox (and, by default, every
channel of it that has an agent configured), it picks the oldest raw item across that
scope (or a named one, or every pending item with `--all`), hands it to that item's
channel-configured agent over the [Agent Client
Protocol](https://agentclientprotocol.com) to curate, and on success archives the raw
item the same way `ingestor.md` does today. This is the unit both a one-shot CLI
invocation and the future `eru inbox watch` (`plans/inbox-watch.md`) build on — `watch`
is deliberately *not* part of this plan; it is a thin loop around `process`, planned
separately so this plan can stay focused on getting processing correct and testable
first.

`InboxChannelConfig.Agent : string option` already exists (`Domain.fs`, added in the
`inbox-send` plan as "reserved: which agent processes this channel") but has never been
used for anything, and its shape (a bare string) isn't enough to launch a real ACP
subprocess. This plan replaces it with a small structured record — see Config change
below.

### Decisions from discussion

- **ACP library**: [`Acp.Net`](https://www.nuget.org/packages/Acp.Net/0.0.1-rc1) (an
  early `0.0.1-rc1` release — expect to validate its API surface carefully during
  implementation, and see the fallback note below).
- **Agent is per-channel and configurable**, not hardcoded to one product. The
  reference shape is the user's own `itr` project
  (`github.com/dburriss/itr`, `itr/itr.json`):
  ```json
  { "agent": { "protocol": "acp", "command": "opencode", "args": ["acp"] } }
  ```
  `itr`'s `src/adapters/AcpAdapter.fs` is **not** built on the `Acp.Net` package — it
  hand-rolls JSON-RPC 2.0 over stdio directly with `System.Text.Json`
  (`initialize` → `session/new` → `session/prompt`, reading `session/update`
  `agent_message_chunk` notifications until a response with an `id` + `result`/`error`
  arrives). It's cited here only as a **proven protocol-shape reference** — useful as a
  fallback implementation if `Acp.Net` 0.0.1-rc1 turns out too thin or unstable to build
  on directly, not as evidence the package itself works a particular way.
- **Multi-channel is in scope for v1** (not deferred): `-c` restricts to one channel;
  omitting it means "every channel of the resolved inbox that has an agent configured."

## Config change (`src/Eru.Domain/Domain.fs`)

```fsharp
type AgentConfig = {
    Protocol : string        // "acp" — only protocol supported in v1
    Command  : string        // e.g. "opencode"
    Args     : string list   // e.g. ["acp"]
}

type InboxChannelConfig = {
    Description : string option
    Agent       : AgentConfig option   // was: string option
}
```

This is a breaking JSON-shape change to the `channel.agent` field, but since nothing in
the codebase reads or writes it meaningfully yet (confirmed unused beyond pass-through),
there's no real migration to write — just update `Init.fs`/scaffold docs and the CLI
flags below. `inbox channel add`'s existing `--agent <label>` flag
(`InboxChannelAddArgs`/`InboxChannelAddCli.fs`, from `plans/inbox-send.md`) changes
shape to three flags:

```
eru inbox channel add <inbox> <channel> [--agent-protocol acp] [--agent-command <cmd>]
                                         [--agent-args <arg> ...] [-d <description>] ...
```

`--agent-protocol` defaults to `"acp"` (the only supported value in v1 — reject others
with a clear error) if any of the three `--agent-*` flags are given; all three are
optional as a group (a channel can have no agent configured at all, meaning `process`
skips it in multi-channel scope and errors if explicitly selected via `-c`).

## Selection model ("unprocessed" = "still in `raw/`")

No new status field or lock file is introduced — this repo's only existing convention
(per `ingestor.md`) is that a raw item's *location* is its state. `process` keeps that:

- **Channel scope**: `-c <channel>` restricts to just that channel (error immediately
  if it has no `Agent` configured). Omitted → every channel configured on the resolved
  inbox that has an `Agent` (channels without one are silently skipped in this default
  scope, since they were never opted in to automated processing).
- For each channel in scope, lists the (non-recursive) contents of
  `<inbox.Path>/<rawPath>/<channel>/`. A raw item is one visible file, **excluding**
  `*.meta.json` sidecars (those are metadata for a `File`-kind capture, paired by
  `<file>.meta.json`, not independent items — see `InboxSend.fs:169-176`).
- All items across every channel in scope are pooled into one list, each tagged with
  its originating channel, then sorted ascending by filename — safe because every
  capture filename starts with a sortable compact UTC timestamp
  (`yyyy-MM-ddTHHmmss-...`, `InboxSend.fs:90-95`), so lexicographic order is
  chronological order regardless of channel. "Oldest" = first in this pooled list. This
  keeps multi-channel scope a pure extension of the single-channel case — every
  downstream step (selection, `--all` folding, archiving) operates on "an item plus
  which channel/agent it came from" and doesn't care how many channels contributed to
  the pool.
- Archiving on success: move the item (and its `.meta.json` sidecar, if present) from
  `.../raw/<channel>/<file>` to `.../archive/<channel>/<file>` — i.e. the last `raw`
  path segment of the resolved raw directory is replaced with `archive`, mirroring the
  existing `knowledge/inbox/archive/<channel>/...` layout `ingestor.md` already
  produces by hand. If a configured `RawPath` doesn't end in a `raw` segment, error
  clearly rather than guessing — call this out as a known v1 limitation.

## CLI

```
eru inbox process [<name>] [-i <inbox>] [-c <channel>] [--all] [--dryrun] [-o <format>]
```

- `<name>` (`MainCommand`, optional) — process this specific item instead of the
  oldest: matched against the pooled listing by exact filename or by filename stem
  (extension-insensitive), so `eru inbox process 2026-09-27T101500-ripgrep-tips` and
  `...ripgrep-tips.md` both work. If the same name exists in more than one channel in
  scope, error asking for `-c` to disambiguate (timestamp-prefixed names make this rare
  but not impossible). Errors if no match anywhere in scope.
- `-i`/`--inbox` — same resolution rule as `inbox send`: explicit name, else the single
  configured inbox, else `DefaultInbox`, else a listing error.
- `-c`/`--channel` — see Selection model above: explicit restricts scope to one
  channel; omitted means every agent-enabled channel of the resolved inbox.
- `--all` — process every pending item in the pooled, scoped list, oldest first,
  stopping at the first failure and reporting how many succeeded before it (across
  channels, in the same pooled chronological order — not channel-by-channel).
- `--dryrun` — resolve and print which item(s) would be selected (with their channel)
  and which agent would run for each, without spawning anything or moving files.
- `-o`/`--output` — `text`/`json`/`table`, matching every other command.

Errors with a clear message when the resolved scope has no channels with an agent
configured (`eru inbox channel add <inbox> <channel> --agent-command <cmd>` is how
you'd fix it) and when there's nothing pending anywhere in scope.

## Domain (`src/Eru.Domain/InboxProcess.fs`)

```fsharp
type Options = {
    InboxName : string option
    Channel   : string option   // -c; None = every agent-enabled channel
    ItemName  : string option   // <name>, if given
    All       : bool
    DryRun    : bool
}

type ProcessedItem = {
    Channel     : string
    ItemPath    : string   // original raw path
    ArchivePath : string   // where it landed on success
    Agent       : AgentConfig
}

val execute : deps: Deps -> eff: EffectiveConfig -> cwd: string -> opts: Options
              -> Result<ProcessedItem list, string>
```

Pure control flow (mirrors `InboxSend.execute`'s resolve-inbox cascade,
`InboxSend.fs`'s `resolveInbox`): resolve the inbox, resolve the channel scope (one
explicit channel, or every channel with an `Agent`), list + pool + sort raw items
across that scope via `Deps.ListLocalFiles`, select target(s) per `Options`, build each
item's agent prompt (frontmatter-stripped body for a `.md` item — needs a small new
`Frontmatter` helper, see below — or a reference to the raw path for a `File` item),
call `Deps.RunAgent` with that item's channel's `AgentConfig`, and on `Ok` call
`Deps.MoveLocalFile` for the item (+ sidecar if present). `--all` folds over the sorted
pooled list, short-circuiting on the first `Error`.

**Small addition to `src/Eru.Domain/Frontmatter.fs`**: no existing function returns the
body text after the frontmatter block (only the private `extractBlock` locates it,
`Frontmatter.fs:13`) — add `body (content: string) : string` that strips the block if
present and returns the rest verbatim, for building the agent prompt from a `.md` raw
item without re-embedding the YAML.

## `Deps` additions (`src/Eru.Domain/Deps.fs`)

```fsharp
ListLocalFiles : string -> Result<string list, string>   // non-recursive; full paths, files only
MoveLocalFile  : string -> string -> Result<unit, string> // src -> dst; creates dst's parent dir
RunAgent       : AgentConfig -> workingDir: string -> prompt: string -> Result<string, string>
```

`RunAgent` mirrors the proven, minimal shape of `itr`'s `IAgentHarness.Prompt` (`prompt
-> debug -> Result<string, string>`) rather than inventing ACP-specific stop-reason
plumbing: `Ok response` means the agent produced a final response (success →
archive); `Error msg` covers both transport failures (couldn't launch/talk to the
subprocess) and an agent-reported failure, and leaves the raw item in place with `msg`
surfaced to the user. No structured "stop reason" type is introduced in the domain
layer — keeping `Deps`'s contract to a plain `Result<string, string>` is enough for
`process` to decide success/failure, and avoids committing to ACP protocol details
(turn stop-reason vocabulary etc.) this early against a `0.0.1-rc1` package.

## Adapter (`src/Eru.Adapters/AcpAgentAdapter.fs`, new)

The one genuinely new piece of infrastructure: nothing in this repo today spawns a
long-lived subprocess and talks bidirectional stdio JSON-RPC to it (`GitAdapter.fs`
only does one-shot `SimpleExec.Command.Run/ReadAsync`; the only other `Process.Start`
calls, `SiteServeServer.fs:137-138` / `SiteGenerator.fs:1114-1115`, just open a URL in
the OS browser). This adapter is the seam:

1. Launch `AgentConfig.Command AgentConfig.Args` via `System.Diagnostics.Process` with
   `RedirectStandardInput = true`, `RedirectStandardOutput = true`,
   `UseShellExecute = false`, `WorkingDirectory = workingDir`.
2. Drive the ACP session via `Acp.Net`'s client API (`initialize` → new session →
   prompt turn) if its `0.0.1-rc1` surface covers this; if it proves too thin, fall
   back to hand-rolled JSON-RPC over the same stdio streams, in the shape `itr`'s
   `AcpAdapter.fs`/`AcpMessages` module already demonstrates working
   (`initialize` request id 0 → `session/new` → `session/prompt`, accumulating
   `session/update` `agent_message_chunk` text until a response carrying `result`/
   `error` arrives) — call this out explicitly as the fallback plan so a thin package
   doesn't block the whole feature.
3. Ensure the subprocess is terminated/disposed even on failure (`try`/`finally`), with
   a timeout (`itr`'s adapter uses 120s per read) so a hung agent doesn't hang `eru`.

Kept entirely inside `Eru.Adapters`, matching the domain/effects split in
`CLAUDE.md`/`AGENTS.md` (`Sync.fs`'s pure-vs-`AdapterDeps.fs`-effectful precedent) —
`Eru.Domain` never references `Acp.Net`, JSON-RPC, or `System.Diagnostics.Process`
directly, only the `RunAgent : AgentConfig -> string -> string -> Result<string, string>`
shape.

`src/Eru.Adapters/Eru.Adapters.fsproj` gains `<PackageReference Include="Acp.Net"
Version="0.0.1-rc1" />` and the new `<Compile Include>`. `AdapterDeps.create` wires
`RunAgent` to it, alongside `ListLocalFiles` (`Directory.GetFiles`) and `MoveLocalFile`
(`Directory.CreateDirectory` on the destination's parent + `File.Move`).

## CLI wiring

Same one-file-per-command pattern as every other command
(`src/Eru.Cli/InboxProcessCli.fs`, `InboxProcessArgs` in `Args.fs`'s `InboxArgs` group,
one dispatch line in `Program.fs`) — no new pattern to design here, follow
`InboxSendCli.fs`/`InboxSendArgs` exactly. `InboxChannelAddCli.fs`/
`InboxChannelAddArgs` need updating for the new `--agent-protocol`/`--agent-command`/
`--agent-args` flags replacing the old single `--agent <label>`.

## Tests (`tests/Eru.Tests/InboxProcessTests.fs`)

Using the existing fake-`Deps`/`CapturedState` pattern (`InboxSendTests.fs`):
- Oldest-first selection pooled across multiple agent-enabled channels; a channel
  without an `Agent` configured is excluded from the default (no `-c`) scope.
- `-c` restricts scope to one channel, even if others have items/agents configured.
- `<name>` selects a specific item (by full name and by stem) across the pooled scope;
  ambiguous name across two channels in scope → error asking for `-c`; no match → error.
- `--all` processes every pending item in pooled chronological order across channels;
  stops and reports partial progress on a simulated agent failure partway through.
- Success moves item (+ sidecar, when present) from `raw/<channel>/` to
  `archive/<channel>/`, verified via captured `MoveLocalFile` calls, using that item's
  own channel (not the first/only one) when scope spans multiple channels.
- `RunAgent` returning `Error` leaves the item in place and surfaces the message.
- No channel in scope has an agent configured → clear error, nothing invoked.
- Nothing pending anywhere in scope → clear "nothing to process" result, not an error.
- `--dryrun` never calls `RunAgent` or `MoveLocalFile`.
- `Frontmatter.body` unit tests: strips a present block, returns content unchanged when
  there is no block.
- `Config.fs` merge/migration tests for `AgentConfig` replacing the old string shape.

## Docs

- `docs/reference/cli.md` — `eru inbox process` section; update `inbox channel add`'s
  documented flags for the new `--agent-protocol`/`--agent-command`/`--agent-args`.
- `docs/reference/config-file.md` — document `AgentConfig` (`protocol`/`command`/
  `args`) replacing the old free-text `agent` string.
- `skills/eru/references/commands.md` — add alongside the rest of `inbox`.

## Verification

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~InboxProcess"

# manual smoke test (requires opencode, or another ACP-capable agent, installed)
dotnet run --project src/Eru -- inbox channel add knowledge eru \
    --agent-protocol acp --agent-command opencode --agent-args acp -g
dotnet run --project src/Eru -- inbox send "ripgrep --hidden still respects .gitignore" -c eru
dotnet run --project src/Eru -- inbox process --dryrun
dotnet run --project src/Eru -- inbox process
# inspect knowledge/inbox/archive/eru/*.md for the archived item and confirm the
# agent's curation landed where ingestor.md's contract expects
```
