namespace Eru.Mcp

open System
open System.Threading
open Eru
open Eru.Adapters
open Microsoft.Extensions.Hosting

type CollectionCacheService(deps: Deps, effectiveCfg: EffectiveConfig) =
    inherit BackgroundService()

    let cacheRoot = Paths.collectionCachePath ()

    let syncAll () =
        effectiveCfg.Collections
        |> List.iter (fun f ->
            match effectiveCfg.Sources |> List.tryFind (fun s -> s.Name = f.Source) with
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
