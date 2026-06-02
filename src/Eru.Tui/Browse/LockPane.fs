module Eru.Tui.Browse.LockPane

open System.Data
open Terminal.Gui.App
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Eru
open BrowseState

type LockPane(initialEntries: LockEntry list) as this =
    inherit FrameView()

    let actionEvent = Event<BrowseAction>()
    let mutable allEntries: LockEntry list = initialEntries
    let mutable filteredEntries: LockEntry list = initialEntries
    let mutable currentFilter = ""

    let tableView = new TableView()

    let buildTable (entries: LockEntry list) =
        let dt = new DataTable()
        dt.Columns.Add("Source")      |> ignore
        dt.Columns.Add("Local Path")  |> ignore
        dt.Columns.Add("Remote Path") |> ignore
        for e in entries do
            dt.Rows.Add(e.SourceName, e.LocalPath, e.RemotePath) |> ignore
        dt

    let applyFilter (filter: string) =
        currentFilter <- filter
        filteredEntries <-
            if filter = "" then allEntries
            else
                allEntries |> List.filter (fun e ->
                    e.LocalPath.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                    e.SourceName.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                    e.RemotePath.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
        tableView.Table <- DataTableSource(buildTable filteredEntries)

    let selectedEntry () =
        match tableView.Value with
        | null -> None
        | sel ->
            let row = sel.SelectedCell.Y
            if row >= 0 && row < filteredEntries.Length
            then Some filteredEntries.[row]
            else None

    do
        this.Title <- "Installed Files"
        tableView.X <- Pos.Absolute 0
        tableView.Y <- Pos.Absolute 0
        tableView.Width <- Dim.Fill()
        tableView.Height <- Dim.Fill()
        tableView.FullRowSelect <- true
        tableView.Table <- DataTableSource(buildTable filteredEntries)
        this.Add(tableView) |> ignore

    member _.ActionRequested = actionEvent.Publish

    member _.FocusContent() = tableView.SetFocus() |> ignore

    member _.ApplyFilter(filter: string) = applyFilter filter

    member _.Reload(entries: LockEntry list) =
        allEntries <- entries
        applyFilter currentFilter

    override _.OnKeyDown(key: Terminal.Gui.Input.Key) =
        if not key.IsShift && not key.IsCtrl && not key.IsAlt && key.AsRune.Value = int '/' then
            key.Handled <- true
            actionEvent.Trigger(FocusFilter)
        elif key.KeyCode = KeyCode.D && not key.IsShift then
            match selectedEntry () with
            | Some e ->
                key.Handled <- true
                actionEvent.Trigger(Disconnect e.LocalPath)
            | None -> ()
        elif key.KeyCode = (KeyCode.ShiftMask ||| KeyCode.A) then
            key.Handled <- true
            actionEvent.Trigger(AddSource)
        elif key.KeyCode = KeyCode.Delete then
            match selectedEntry () with
            | Some e ->
                key.Handled <- true
                actionEvent.Trigger(RemoveEntry e.LocalPath)
            | None -> ()
        base.OnKeyDown(key)
