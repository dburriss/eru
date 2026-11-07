namespace Eru.Search

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Eru
open Eru.Adapters

type SyncResult = {
    SourcesRefreshed : int
    FilesCached      : int
    Errors           : string list
}

/// How background syncs coordinate across `eru mcp` processes (and `eru sync`). The file-based
/// implementation is the real one; tests substitute their own.
type SyncGate = {
    TryAcquire : unit -> System.IDisposable option
    LastSync   : unit -> System.DateTimeOffset option
    MarkSynced : unit -> unit
    Now        : unit -> System.DateTimeOffset
}

module SyncGate =
    let files : SyncGate = {
        TryAcquire = SyncLock.tryAcquire
        LastSync   = SyncLock.lastSync
        MarkSynced = SyncLock.markSynced
        Now        = fun () -> System.DateTimeOffset.UtcNow
    }

type KnowledgeSyncService(deps: Deps, startupEff: EffectiveConfig, gate: SyncGate, logger: ILogger<KnowledgeSyncService>) =
    let mutable currentEff = startupEff
    let mutable syncRunning = 0   // 0 = idle, 1 = running; Interlocked-guarded
    let mutable stale = 0         // 1 once a sync found a newer eru's index on disk; never reset
    let mutable lastActivityTicks = gate.Now().UtcTicks   // last MCP request (or startup); see NoteActivity

    let buildEff () =
        let globalCfgOpt = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfgOpt  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        let baseEff =
            DepsConfig.merge deps globalCfgOpt localCfgOpt
            |> Result.defaultWith (fun _ -> currentEff)
        Config.withManifests deps.ReadCachedManifest baseEff

    member _.CurrentEff = Volatile.Read(&currentEff)

    /// True when `mcp.idleTimeoutMinutes` is set and no MCP request has arrived for that long. Background
    /// timers skip their ticks while idle; the process keeps answering requests.
    member _.IsIdle =
        let minutes = Volatile.Read(&currentEff).McpIdleTimeoutMinutes
        minutes > 0
        && gate.Now().UtcTicks - Volatile.Read(&lastActivityTicks) > System.TimeSpan.FromMinutes(float minutes).Ticks

    /// True once a sync was refused because a newer eru wrote the index: this process runs old code and
    /// must stop syncing (restart it).
    member _.IsStale = Volatile.Read(&stale) = 1

    member _.Sync() =
        let errors = Sync.populateIndex deps
        if Sync.hasStaleWriter errors && Interlocked.Exchange(&stale, 1) = 0 then
            logger.LogWarning("This eru mcp server is stale (a newer eru wrote the index); background sync stopped. Restart it.")
        if errors <> [] then
            logger.LogWarning("Index population completed with errors: {Errors}", String.concat "; " errors)
        let freshEff = buildEff ()
        Volatile.Write(&currentEff, freshEff)
        { SourcesRefreshed = freshEff.Sources.Length; FilesCached = 0; Errors = errors }

    /// Starts a sync on a thread-pool thread and returns whether one was started. At most one runs at a
    /// time across every eru process: the in-process flag stops overlap inside this server, the gate's
    /// file lock stops it between servers. Unless `force`, a sync is also skipped when another process
    /// finished one less than half a refresh interval ago (timers of several servers then add up to
    /// roughly one sync per interval). `refresh_knowledge` passes `force`.
    member this.TriggerBackgroundSync(force: bool) : bool =
        if this.IsStale then false
        elif Interlocked.CompareExchange(&syncRunning, 1, 0) <> 0 then false
        else
            let release () = Interlocked.Exchange(&syncRunning, 0) |> ignore
            match gate.TryAcquire() with
            | None ->
                release ()
                logger.LogDebug("Another eru process is syncing; skipping this one")
                false
            | Some held ->
                let tooSoon =
                    not force
                    && (match gate.LastSync() with
                        | Some t -> gate.Now() - t < System.TimeSpan.FromMinutes(float this.CurrentEff.McpRefreshIntervalMinutes / 2.0)
                        | None -> false)
                if tooSoon then
                    held.Dispose()
                    release ()
                    false
                else
                    Task.Run(fun () ->
                        try
                            try
                                let result = this.Sync()
                                if not this.IsStale then gate.MarkSynced()
                                if result.Errors <> [] then
                                    logger.LogWarning("Sync completed with errors: {Errors}", String.concat "; " result.Errors)
                            with ex ->
                                logger.LogError(ex, "Sync threw an unhandled exception")
                        finally
                            held.Dispose()
                            release ()) |> ignore
                    true

    /// Records an MCP request. The first one after an idle spell resumes background syncing at once.
    member this.NoteActivity() : unit =
        let wasIdle = this.IsIdle
        Volatile.Write(&lastActivityTicks, gate.Now().UtcTicks)
        if wasIdle then this.TriggerBackgroundSync(false) |> ignore
