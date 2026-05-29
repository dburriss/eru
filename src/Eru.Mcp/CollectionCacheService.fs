namespace Eru.Mcp

open System
open System.Threading
open Eru
open Eru.Adapters
open Microsoft.Extensions.Hosting

type CollectionCacheService(deps: Deps, effectiveCfg: EffectiveConfig) =
    inherit BackgroundService()

    let cacheRoot = Paths.collectionCachePath ()

    let buildEff () =
        let globalCfgOpt = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfgOpt  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        let baseEff =
            Config.merge globalCfgOpt localCfgOpt
            |> Result.defaultWith (fun _ -> effectiveCfg)
        for src in baseEff.Sources do
            match src.Url with
            | None -> ()
            | Some url ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch ".eru/manifest.json" with
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest src.Name raw |> ignore
                | _ -> ()
        Config.withManifests deps.ReadCachedManifest baseEff

    let syncAll () =
        let eff = buildEff ()
        eff.Collections
        |> List.iter (fun f ->
            match eff.Sources |> List.tryFind (fun s -> s.Name = f.Source) with
            | None -> eprintfn "eru: collection cache: unknown source '%s'" f.Source
            | Some src ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match src.Url with
                | None -> eprintfn "eru: collection cache: source '%s' has no URL configured" f.Source
                | Some url ->
                    match deps.FetchRemoteContent url branch f.RemotePath with
                    | Error e   -> eprintfn "eru: collection cache: fetch failed for %s/%s: %s" f.Source f.RemotePath e
                    | Ok []     -> eprintfn "eru: collection cache: no files returned for %s/%s" f.Source f.RemotePath
                    | Ok files  ->
                        files |> List.iter (fun (resolvedPath, content) ->
                            let dest = IO.Path.Combine(cacheRoot, f.Source, resolvedPath.Replace('/', IO.Path.DirectorySeparatorChar))
                            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName dest) |> ignore
                            IO.File.WriteAllText(dest, content)))

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            syncAll ()
            use timer = new PeriodicTimer(TimeSpan.FromMinutes(float effectiveCfg.McpRefreshIntervalMinutes))
            while! timer.WaitForNextTickAsync(ct) do
                syncAll ()
        }
