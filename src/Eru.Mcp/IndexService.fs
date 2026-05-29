namespace Eru.Mcp

open System
open System.IO
open System.Threading
open Eru
open Eru.Adapters
open Microsoft.Extensions.Hosting

type IndexService(deps: Deps, eff: EffectiveConfig) =
    inherit BackgroundService()

    let roots () =
        [ deps.GetCwd(); Paths.collectionCachePath() ]
        |> List.filter Directory.Exists

    let indexAll () =
        match Environment.GetEnvironmentVariable "ERU_SEARCH_BACKEND" with
        | "ck" ->
            roots () |> Array.ofList |> Array.Parallel.iter CkAdapter.indexDir
        | "indexed" ->
            roots ()
            |> List.collect (fun dir ->
                Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories) |> Seq.toList)
            |> Array.ofList
            |> Array.Parallel.iter (SearchIndexAdapter.getOrBuild >> ignore)
        | _ -> ()

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            indexAll ()
            use timer = new PeriodicTimer(TimeSpan.FromMinutes(float eff.McpRefreshIntervalMinutes))
            while! timer.WaitForNextTickAsync(ct) do
                indexAll ()
        }
