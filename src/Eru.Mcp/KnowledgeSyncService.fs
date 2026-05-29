namespace Eru.Mcp

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
    let cacheRoot = Paths.collectionCachePath ()

    let buildEff () =
        let globalCfgOpt = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfgOpt  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        let baseEff =
            Config.merge globalCfgOpt localCfgOpt
            |> Result.defaultWith (fun _ -> currentEff)
        for src in baseEff.Sources do
            match src.Url with
            | None -> ()
            | Some url ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch ".eru/manifest.json" with
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest src.Name raw |> ignore
                | _ -> ()
        Config.withManifests deps.ReadCachedManifest baseEff

    member _.CurrentEff = Volatile.Read(&currentEff)

    member _.Sync() =
        let mutable errors     = []
        let mutable filesCached = 0
        let freshEff = buildEff ()
        freshEff.Collections
        |> List.iter (fun f ->
            match freshEff.Sources |> List.tryFind (fun s -> s.Name = f.Source) with
            | None ->
                errors <- errors @ [$"unknown source '{f.Source}'"]
            | Some src ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match src.Url with
                | None ->
                    errors <- errors @ [$"source '{f.Source}' has no URL configured"]
                | Some url ->
                    match deps.FetchRemoteContent url branch f.RemotePath with
                    | Error e  -> errors <- errors @ [$"fetch failed for {f.Source}/{f.RemotePath}: {e}"]
                    | Ok []    -> errors <- errors @ [$"no files returned for {f.Source}/{f.RemotePath}"]
                    | Ok files ->
                        files |> List.iter (fun (resolvedPath, content) ->
                            let dest = System.IO.Path.Combine(cacheRoot, f.Source, resolvedPath.Replace('/', System.IO.Path.DirectorySeparatorChar))
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName dest) |> ignore
                            System.IO.File.WriteAllText(dest, content)
                            filesCached <- filesCached + 1))
        Volatile.Write(&currentEff, freshEff)
        { SourcesRefreshed = freshEff.Sources.Length; FilesCached = filesCached; Errors = errors }

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
