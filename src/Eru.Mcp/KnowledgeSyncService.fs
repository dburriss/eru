namespace Eru.Mcp

open Eru
open Eru.Adapters

type SyncResult = {
    SourcesRefreshed : int
    FilesCached      : int
    Errors           : string list
}

type KnowledgeSyncService(deps: Deps, startupEff: EffectiveConfig) =
    let mutable currentEff = startupEff
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

    member _.CurrentEff = currentEff

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
        currentEff <- freshEff
        { SourcesRefreshed = freshEff.Sources.Length; FilesCached = filesCached; Errors = errors }
