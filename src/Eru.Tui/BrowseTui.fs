#nowarn "0044"
module Eru.Tui.BrowseTui

open System.IO
open Terminal.Gui.App
open Eru
open Eru.Tui.Browse.BrowseState

let run (deps: Deps) : int =
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

    // Show local tab when there are tracked files, otherwise start on files
    let initialTab =
        if lockEntries.IsEmpty then FilesTab else LocalTab

    Application.Init(Unchecked.defaultof<string>)
    try
        let window = new Browse.BrowseWindow.BrowseWindow(deps, initialTab, sources, lockEntries)
        Application.Run(window, Unchecked.defaultof<_>)
        0
    finally
        Application.Shutdown()
