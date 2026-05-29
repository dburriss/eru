namespace Eru.Mcp

open System.Threading
open Microsoft.Extensions.Hosting

type CollectionCacheService(sync: KnowledgeSyncService) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            sync.TriggerBackgroundSync() |> ignore
            use timer = new PeriodicTimer(System.TimeSpan.FromMinutes(float sync.CurrentEff.McpRefreshIntervalMinutes))
            while! timer.WaitForNextTickAsync(ct) do
                sync.TriggerBackgroundSync() |> ignore
        }
