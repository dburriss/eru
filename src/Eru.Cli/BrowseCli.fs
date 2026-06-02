#nowarn "0044"
module Eru.Cli.BrowseCli

open System.IO
open Argu
open Terminal.Gui.App
open Eru
open Eru.Cli.Browse.BrowseState

type Cmd = unit

let (|BrowseCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Browse _ -> Some ()
        | _                -> None)

let run (deps: Deps) (_cmd: Cmd) : int =
    let eruDir    = Path.Combine(deps.GetCwd(), ".eru")
    let initialTab =
        if Directory.Exists eruDir then LockTab else SourcesTab

    let sources =
        match SourceList.execute deps with
        | Ok rows -> rows
        | Error _ -> []

    let lockEntries =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Ok gc, Ok lc ->
            match Config.merge gc lc with
            | Ok eff ->
                match deps.ReadLockEntries eff.StateFile with
                | Ok entries -> entries
                | Error _    -> []
            | Error _ -> []
        | _ -> []

    Application.Init(Unchecked.defaultof<string>)
    try
        let window = new Browse.BrowseWindow.BrowseWindow(deps, initialTab, sources, lockEntries)
        Application.Run(window, Unchecked.defaultof<_>)
        0
    finally
        Application.Shutdown()
