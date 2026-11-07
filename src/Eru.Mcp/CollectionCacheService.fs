namespace Eru.Mcp

open System.Threading
open Microsoft.Extensions.Hosting
open Eru.Search

type CollectionCacheService(sync: KnowledgeSyncService) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            sync.TriggerBackgroundSync(false) |> ignore
            use timer = new PeriodicTimer(System.TimeSpan.FromMinutes(float sync.CurrentEff.McpRefreshIntervalMinutes))
            let mutable running = true
            while running do
                let! tick = timer.WaitForNextTickAsync(ct)
                // A stale server (a newer eru wrote the index) stops syncing for good.
                if not tick || sync.IsStale then running <- false
                elif not sync.IsIdle then sync.TriggerBackgroundSync(false) |> ignore   // idle: paused until a request
        }
