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

type KnowledgeSyncService(deps: Deps, startupEff: EffectiveConfig, logger: ILogger<KnowledgeSyncService>) =
    let mutable currentEff = startupEff
    let mutable syncRunning = 0   // 0 = idle, 1 = running; Interlocked-guarded

    let buildEff () =
        let globalCfgOpt = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfgOpt  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        let baseEff =
            Config.merge globalCfgOpt localCfgOpt
            |> Result.defaultWith (fun _ -> currentEff)
        Config.withManifests deps.ReadCachedManifest baseEff

    member _.CurrentEff = Volatile.Read(&currentEff)

    member _.Sync() =
        let errors = Sync.populateIndex deps
        if errors <> [] then
            logger.LogWarning("Index population completed with errors: {Errors}", String.concat "; " errors)
        let freshEff = buildEff ()
        Volatile.Write(&currentEff, freshEff)
        { SourcesRefreshed = freshEff.Sources.Length; FilesCached = 0; Errors = errors }

    member this.TriggerBackgroundSync() : bool =
        if Interlocked.CompareExchange(&syncRunning, 1, 0) = 0 then
            Task.Run(fun () ->
                try
                    try
                        let result = this.Sync()
                        if result.Errors <> [] then
                            logger.LogWarning("Sync completed with errors: {Errors}", String.concat "; " result.Errors)
                    with ex ->
                        logger.LogError(ex, "Sync threw an unhandled exception")
                finally
                    Interlocked.Exchange(&syncRunning, 0) |> ignore) |> ignore
            true
        else
            false
