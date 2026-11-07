---
status: done
---
# Plan: Stop stale `eru mcp` servers from clobbering shared cache state

## Incident (2026-11-07)

Five `eru mcp` processes (started Nov 2, 3, 5, 5, 6) outlived a new eru install. Each one's
`CollectionCacheService` timer (`McpRefreshIntervalMinutes`) kept calling `Sync.populateIndex` with the
**old** code. That code's Step 1c reseeds `Entries` from manifests and drops OKF-discovered entries, and
Step 1d bumps `ConsecutiveShaCheckFailures`. The shared `~/.cache/eru/sources/<name>/index.json` went back
to empty after `eru sync` had written 710 entries.

## Findings

### 1. Why servers pile up and outlive clients

- **Spawn:** the MCP client (Claude Code, etc.) starts one `eru mcp` per agent session or per configured
  server entry. eru has no control over this, and nothing reaps the process except the client closing its stdin.
- **Exit paths today** (`src/Eru.Mcp/McpServer.fs:67`): `builder.Build().RunAsync()` with
  `WithStdioServerTransport()`. The only shutdown triggers are stdin EOF (via the SDK transport) and SIGINT/SIGTERM.
  - **No parent-process watch.** If the client is killed and stdin stays open (a wrapper such as `mise run mcp`, or a
    forked grandchild, holds the pipe's write end), the server never learns.
  - **No idle handling.** The two `BackgroundService`s (`IndexService`, `CollectionCacheService`) loop forever on
    `PeriodicTimer`, so a server nobody talks to still syncs every interval.
  - **No version check.** A server keeps running the binary it was loaded from; a dotnet tool upgrade replaces files
    on disk, not the process in memory.
- **Convention:** exiting on stdin EOF and leaving lifecycle to the client is normal for stdio MCP servers. Idle
  timeouts and parent polling are not part of MCP. eru still defends itself because an orphan writes shared state.
- **Unverified:** whether the SDK's stdio transport stops the host on EOF in the pinned `ModelContextProtocol`
  version, and whether the five survivors were orphaned (ppid=1) or held open by a pipe. See Verification step 1.

### 2. Shared state and writers

| State | Writers | Protection today |
|---|---|---|
| `sources/<name>/index.json` | `Sync.populateIndex` (many `WriteSourceIndex` calls per run), `SourceBundleAdd`, `eru sync`, `eru source files --refresh`, `eru browse`, `eru site serve`, every MCP server | None. `SourceIndexAdapter.writeIndex` is a bare `File.WriteAllText`. |
| `sources/<name>/files/` content cache, manifests | same | Content-addressed names, so mostly idempotent. Manifests are overwritten whole. |
| `ferret/` index | each server's `IndexService.warm` | Ferret's own concerns (not audited here) |
| `mcp.log` | all servers, via Serilog `File` sink | Serilog opens the file unshared by default, so a second process falls back to a suffixed file |

- **No lock exists.** Despite its name, `Sync.executeLocked` (`Sync.fs:486`) is just the post-populate half of
  `execute`. No `FileShare`/`Mutex`/`lock` appears anywhere in `src/`. The `syncRunning` flag in
  `KnowledgeSyncService` is per process, so it only stops overlap inside one server.
- **No atomic write.** `writeIndex` truncates and rewrites in place, so a concurrent reader can see a half-written
  file. `readIndex` maps malformed JSON to "no index" (`Ok None`), so the next sync starts from empty: a second clobber
  path even with identical binaries.
- **Read-modify-write races:** `readIndexOrEmpty` then `WriteSourceIndex` happens several times per sync with network
  calls in between, so two writers can lose each other's updates.
- **No writer identity:** `SourceIndex` carries only a schema `Version`, so a newer index can't tell an older binary to
  back off.

### 3. What would have prevented the incident

The five servers already ran pre-fix code, so no code change can stop them retroactively. For the next upgrade:

- **Version stamp + refuse older writer** prevents the overwrite itself. This is the smallest change that fixes the
  damage mechanism.
- **Supersede** (a server exits when a newer eru is running) would have removed the stale servers as soon as the first
  new one started.
- Atomic writes alone would not have helped (the stale writes were complete, valid files). A lock alone would not
  have helped either (the stale server was serialised, not racing).

## Decisions

- Stdio MCP lifecycle is client-owned by convention; eru defends itself anyway.
- **No separate machine-wide daemon.** Lifecycle, per-OS install and the per-repo config merge (each server carries
  its own repo's config) make it worse than the problem. Syncing stays in-process, made single-writer by a
  cross-process lock + throttle. An explicit `eru sync --watch` could take the same lock later as an opt-in.
- MCP "tasks" are request-scoped, not a machine-wide worker, so they are not used.
- **Idle never exits the process.** It pauses the background timers and the server keeps answering, because a client
  calling a dead stdio server fails or must reconnect (behaviour unverified per client). Default off.
- **Stale servers are cleaned up by supersede:** a server exits when a newer eru version is running.
- Version comparison uses the full `System.Version` (alpha builds bump the patch).
- Single background sync is lock + throttle only. Followers do not reload when the leader writes `index.json`.

## Steps (in order; each shippable)

### Step 1: Version stamp, refuse older writer, atomic write

- `src/Eru.Domain/Config.fs` (`SourceIndex`, ~line 403): add `WrittenBy : string option`. Update literal
  constructions: `Sync.fs:81`, `SourceBundleAdd.fs:42`, test helpers.
- Move the version lookup out of `src/Eru.Cli/VersionCli.fs` (`parseVersion`, assembly informational version) into a
  shared spot in `Eru.Adapters` so the adapter can call it; keep `VersionCli` calling it.
- `src/Eru.Adapters/SourceIndexAdapter.fs` `writeIndex`:
  - stamp `WrittenBy = Some <this version>`;
  - read the existing stamp; if it is a **newer** `System.Version` (strip `+commit`; unparsable = not newer) return
    `Error "index written by newer eru <v>; this process (<m>) is stale"`, leaving the file untouched;
  - write `index.json.<pid>.tmp` in the same directory, then `File.Move(tmp, path, overwrite = true)`; delete the
    temp on failure.
- `src/Eru.Domain/Sync.fs` `populateIndex`: it currently does `WriteSourceIndex ... |> ignore` everywhere. Surface a
  refusal as one error per source ("this eru is stale, restart it") and skip further writes for that source in the run.
- `src/Eru.Search/KnowledgeSyncService.fs` and `src/Eru.Mcp/CollectionCacheService.fs`: on the stale-writer error,
  log once and stop the sync timer loop.
- Caveat to document: the schema bump does **not** protect against servers that already run old code.

### Step 2: Cross-process sync lock + throttle (single background sync)

- New adapter helper (e.g. `src/Eru.Adapters/SyncLock.fs`): exclusive lock on `<cache>/eru/sync.lock` via
  `FileStream(..., FileShare.None)`; path from `Paths` with the same XDG/LocalAppData branching as `mcpLogPath`. The OS
  releases it if the holder dies. Expose `tryAcquire : unit -> IDisposable option`.
- `<cache>/eru/last-sync` timestamp, written by the holder after a successful run.
- `KnowledgeSyncService.TriggerBackgroundSync`: after the in-process guard, `tryAcquire`; if held by another process,
  skip; if the timestamp is younger than the refresh interval, skip. Put a hard timeout around a sync so a hung git
  call can't hold the lock forever.
- The `eru sync` CLI path takes the same lock: wait briefly, else print "another sync is running" (no silent skip).
- Followers refresh on their own next warm tick (no mtime reload; out of scope).

### Step 2b: Supersede: a stale server exits when a newer eru is running

- Registry directory `<cache>/eru/mcp/` (path from `Paths`, same branching as `mcpLogPath`). On startup each server
  writes `<pid>.json` with `{ pid, version, startedAt }` and deletes it on clean shutdown (`ApplicationStopping`).
- On each timer tick (a small hosted service, or the existing `CollectionCacheService` tick) list the registry, drop
  entries whose PID is no longer alive (`Process.GetProcessById` throws) and delete those files. If any live entry has a
  higher `System.Version` than this process, call `IHostApplicationLifetime.StopApplication()` and log why to `mcp.log`.
- Equal versions never trigger an exit (they are usually separate live sessions).
- Reuses the shared version helper from Step 1.
- Limits to document: servers already running old code have no registry check, so this helps from the next upgrade
  onward; a stale server can sync once before its next tick, which the Step 1 stamp refusal covers; clients may need to
  reconnect after a superseded server exits.

### Step 3: Idle pause (default off)

- New setting `mcp.idleTimeoutMinutes` (`Config.fs`, `ConfigCmd.fs`, `ERU__MCP__IDLETIMEOUTMINUTES`; `0` = never pause).
- Activity timestamp updated from the tool and resource handlers (`McpTools.fs`, `McpResources.fs`).
- `CollectionCacheService` and `IndexService` skip their timer ticks while idle; the next request resumes them and
  triggers a sync. Background ticks never count as activity. The process stays up and keeps answering.

### Deferred

- Parent-process / stdin-EOF watch: only if the repro shows orphans (ppid=1).
- `eru mcp --status` / `eru doctor` to list and stop servers.

## Tests (`tests/Eru.Tests`)

- Adapter: write stamps the version; write over a newer stamp returns `Error` and the file is byte-identical; write
  over older/unstamped succeeds; a failed move leaves the old file intact; no `*.tmp` left on success.
- `populateIndex` with a refusing `WriteSourceIndex`: exactly one stale-writer error per source, no further writes.
- Regression shape: a stale writer cannot replace a newer, populated index (`710` -> `0`).
- Lock: a second `tryAcquire` in the same process fails while held and succeeds after dispose; the throttle skips when
  `last-sync` is fresh; a sync that exceeds the timeout releases the lock.
- Supersede: a registry with a higher-version live entry makes the check return "exit"; equal or lower versions do not;
  dead-PID entries are pruned and ignored; the server's own entry is written on start and removed on stop.
- Idle pause: fake clock; ticks skipped after the timeout; a request resumes them; `0` disables.
- Domain tests use `/` paths, never `Path.Combine` (AGENTS.md).

## Docs and changelog

- `CHANGELOG.md` `## [Unreleased]`: **Fixed** (a stale server no longer overwrites a newer index; index writes are
  atomic; one background sync across servers; a server exits when a newer eru is running). **Added**
  `mcp.idleTimeoutMinutes`.
- `docs/reference/mcp-server.md`: restart-after-upgrade note (running servers are not restarted by an upgrade, and old
  ones need `pkill -f 'eru mcp'`), single-sync behaviour, supersede, idle pause.
- `docs/reference/config-file.md`, `environment-variables.md`: the new setting.
- `docs/how-to/set-up-the-mcp-server.md`: troubleshooting for "`N entries indexed` drops back to 0".
- No `Co-Authored-By` or attribution trailers in commits (AGENTS.md); Conventional Commits subjects.

## Verification

1. Repro first (cheap, read-only): `tail -f /dev/null | eru mcp`, kill the parent, then `ps -o pid,ppid,lstart` and
   `lsof -p` on the survivor; also record the PPIDs of any leaked servers. This decides whether the parent watch is needed.
2. `dotnet build` and `dotnet test --solution eru.slnx`.
3. Stale-writer check: install a build, start `eru mcp`, run `eru sync`, then run a second binary with a lower
   version against the same cache and confirm `index.json` is untouched and a warning appears in `mcp.log`.
4. Start three `eru mcp` processes with a short `mcp.refreshIntervalMinutes`; confirm one sync per interval (log
   lines) and that `eru sync` reports "another sync is running" while one is in flight.
5. Supersede: start a server from an older build, then one from a newer build; confirm the older one logs and exits on
   its next tick, and that two same-version servers both stay up.
6. With `mcp.idleTimeoutMinutes` small, confirm ticks stop, a `search_knowledge` call still answers and resumes sync.

## Immediate remediation (no code)

```bash
ps -eo pid,ppid,lstart,command | grep '[e]ru mcp'   # check start dates and PPIDs
pkill -f 'eru mcp'                                  # SIGTERM; matches every eru mcp, including your live session's
eru sync                                            # rebuild the index; clients respawn servers on next use
```
