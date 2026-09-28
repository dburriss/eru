---
status: proposed
---

# Plan: `eru inbox watch` — auto-process an inbox as items arrive

## Context

`eru inbox process` (`plans/inbox-process.md`) processes an inbox's pending raw items
on demand across one or every agent-enabled channel — either the oldest, a named one,
or `--all`. `watch` is the long-running counterpart: instead of running `process` by
hand after every `inbox send`, it keeps an inbox under observation and runs the same
"process all pending" logic automatically whenever new items show up, until
interrupted.

This is deliberately a **separate, much smaller plan** built entirely on top of
`inbox process` — it adds no new selection, archiving, or agent-invocation logic of its
own; it only decides *when* to call `InboxProcess.execute { opts with All = true }`
again. That keeps `inbox process` reviewable and testable in isolation, and keeps this
plan's scope to "process-loop lifecycle" only.

The closest existing precedent for a long-running background loop in this repo is
`eru site serve` (`src/Eru.Serve/SiteServeServer.fs:112-132`): a `Task.Run` wrapping a
`System.Threading.PeriodicTimer`, ticking on an interval, catching
`OperationCanceledException` to exit, torn down via a `CancellationTokenSource` when
the host stops. `watch` follows the same interval-polling shape, plus a
`FileSystemWatcher` for immediate reaction — per discussion, both together rather than
either alone, since `FileSystemWatcher` can miss events on some filesystems/OSes and
polling alone adds latency.

## Behavior

```
eru inbox watch [-i <inbox>] [-c <channel>] [--interval <seconds>] [--dryrun]
```

- `-i`/`--inbox`, `-c`/`--channel` — same resolution and scope rules as `inbox
  process`: `-c` restricts to one channel; omitted watches every channel of the
  resolved inbox that has an `Agent` configured, matching `process`'s multi-channel
  default. A single `FileSystemWatcher` rooted at `<inbox.Path>/<rawPath>/` with
  `IncludeSubdirectories = true` covers every channel's raw directory at once, so
  multi-channel scope adds no extra watcher/timer plumbing — one loop, one poll timer,
  regardless of how many channels are in scope.
- `--interval` — polling fallback period in seconds, default `30` (mirrors
  `SiteServeServer`'s `SyncInterval` precedent, just in seconds instead of minutes since
  inbox items are expected to arrive much more often than a knowledge sync).
- `--dryrun` — same meaning as `process --dryrun`, applied on every trigger: logs what
  *would* be processed without spawning an agent or moving files. Useful for watching
  the trigger cadence itself without burning agent calls.
- On start: resolves inbox/channel once (fails fast on the same errors `process`
  would — inbox/channel not configured, path missing, no agent configured), then prints
  what it's watching and blocks.
- On trigger (either the `FileSystemWatcher` firing on the raw directory, or the
  `PeriodicTimer` ticking): runs `InboxProcess.execute` with `All = true` against the
  resolved options, processing every currently-pending item sequentially, oldest first,
  before returning to watching. A `FileSystemWatcher` event is debounced with a short
  quiet period (~500ms) before triggering the batch, so a still-being-written file
  doesn't get picked up mid-write.
- A failure partway through a batch (same as `process --all`) is logged and does *not*
  stop the watch loop — the failing item stays in `raw/`, is retried on the next
  trigger, and watching continues. This is the one behavioral difference from a bare
  `process --all` call: `watch` never exits on a processing failure, since a
  transient agent/network error shouldn't kill a long-running service.
- `Ctrl+C`/SIGINT — graceful shutdown: cancel the `CancellationTokenSource`, let any
  in-flight batch finish its current item, dispose the `FileSystemWatcher`, exit 0.

## Implementation shape

No new domain module — `watch` is orchestration, not domain logic, so it lives beside
the CLI layer directly rather than growing `Eru.Domain`:

- `src/Eru.Cli/InboxWatchCli.fs` (new): the active-pattern + `run` boilerplate (same
  shape as every other CLI file) plus the loop itself:
  - `use watcher = new FileSystemWatcher(rawRootDir, IncludeSubdirectories = true)`
    (rooted at `<inbox.Path>/<rawPath>/`, one level above the per-channel
    subdirectories) with `NotifyFilter` for creation/rename, `EnableRaisingEvents =
    true`; its `Created`/`Renamed` handlers debounce (e.g. a `System.Threading.Timer`
    reset on each event) then signal a `SemaphoreSlim`/channel that a processing pass
    is due — a single watcher instance regardless of how many channels are in scope.
  - A loop awaiting either the debounce signal or `PeriodicTimer.WaitForNextTickAsync`
    (whichever fires first — `Task.WhenAny`), then calls
    `InboxProcess.execute deps eff cwd { opts with All = true }`, logging the result
    (items processed / failure) via the same render helpers `process` uses.
  - Both the timer and the watcher share one `CancellationToken` sourced from
    `Console.CancelKeyPress`, matching `SiteServeServer`'s shutdown pattern.
- No new project (unlike `site serve`'s `Eru.Serve`) — `FileSystemWatcher` and
  `PeriodicTimer` are BCL types needing no new package reference, and there's no HTTP
  server involved here.
- `Args.fs`: `InboxWatchArgs` (interval, dryrun, inbox/channel flags) added to the
  existing `InboxArgs` group; one dispatch line in `Program.fs`.

## Tests

Loop lifecycle (timers, `FileSystemWatcher`, `Ctrl+C`) is inherently hard to unit test
meaningfully and isn't where the risk is — the risk is in `InboxProcess.execute`, which
already has its own test coverage in `plans/inbox-process.md`. Keep test scope here
narrow:
- Options parsing/validation (`InboxWatchArgs` → resolved options), reusing whatever
  inbox/channel resolution tests already exist for `process`/`send`.
- If feasible, a small integration-style test that drives the debounce/trigger
  decision logic in isolation (e.g. extract "given N events within the debounce
  window, trigger once" as a small pure/testable function rather than inlining it
  directly in the `FileSystemWatcher` callback) — worth doing specifically so the
  debounce behavior isn't only verified by hand.

## Docs

- `docs/reference/cli.md` — `eru inbox watch` section, noting it's a long-running
  foreground command (how to background/daemonize it — e.g. via a process supervisor,
  `launchd`/`systemd` unit — is left to the user, not eru's concern).
- `skills/eru/references/commands.md` — add alongside `inbox process`.

## Verification

```bash
dotnet build

# terminal 1
dotnet run --project src/Eru -- inbox watch -c eru --interval 10

# terminal 2, while watch is running
dotnet run --project src/Eru -- inbox send "another note" -c eru
# confirm terminal 1 picks it up promptly (FileSystemWatcher path) without waiting
# the full 10s poll interval, and that Ctrl+C in terminal 1 exits cleanly
```
