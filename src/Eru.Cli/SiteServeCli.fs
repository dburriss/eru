module Eru.Cli.SiteServeCli

open Argu
open Eru
open Eru.Adapters
open Eru.Serve

let (|SiteServeCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Site args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SiteArgs.Serve serveArgs -> Some serveArgs
                | _ -> None)
        | _ -> None)

let run (deps: Deps) (args: ParseResults<SiteServeArgs>) : int =
    let cfgResult =
        let globalCfg = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfg  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        Config.merge globalCfg localCfg
        |> Result.map (fun eff -> Config.withManifests deps.ReadCachedManifest eff)
    match cfgResult with
    | Error e -> eprintfn "Error reading config: %s" e; 1
    | Ok cfg ->
        let opts = {
            SiteServeServer.ServeOptions.defaults with
                OutputDir    = args.TryGetResult(SiteServeArgs.Output)        |> Option.defaultValue "./cache-site/"
                Port         = args.TryGetResult(SiteServeArgs.Port)          |> Option.defaultValue 5173
                OpenBrowser  = not (args.Contains SiteServeArgs.No_Open)
                SyncInterval = args.TryGetResult(SiteServeArgs.Sync_Interval) |> Option.defaultValue 15
        }
        SiteServeServer.run deps cfg opts |> Async.AwaitTask |> Async.RunSynchronously
