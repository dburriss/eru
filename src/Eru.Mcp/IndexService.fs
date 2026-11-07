namespace Eru.Mcp

open System
open System.Threading
open Eru
open Eru.Search
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

/// Builds the search index off the request path: at startup and on every refresh tick. When
/// a semantic mode is configured and the embedding model is missing, it downloads it here
/// (unless FERRET_OFFLINE=1) so a `search_knowledge` call never waits on a download.
type IndexService(deps: Deps, syncService: KnowledgeSyncService, searcher: RankedSearch.Searcher, logger: ILogger<IndexService>) =
    inherit BackgroundService()

    let warm (ct: CancellationToken) =
        task {
            try
                let eff = syncService.CurrentEff
                match RankedSearch.resolveMode None eff with
                | Error e -> logger.LogWarning("Search index not built: {Error}", e)
                | Ok mode ->
                    let offline = Environment.GetEnvironmentVariable "FERRET_OFFLINE" = "1"
                    do! RankedSearch.warmUp searcher deps eff (deps.GetCwd()) mode (not offline) ct
            with
            | :? OperationCanceledException -> ()
            | ex -> logger.LogError(ex, "Building the search index failed")
        }

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            do! warm ct
            use timer = new PeriodicTimer(TimeSpan.FromMinutes(float syncService.CurrentEff.McpRefreshIntervalMinutes))
            while! timer.WaitForNextTickAsync(ct) do
                // Paused while idle (mcp.idleTimeoutMinutes); a request or the next tick after it resumes.
                if not syncService.IsIdle then do! warm ct
        }
