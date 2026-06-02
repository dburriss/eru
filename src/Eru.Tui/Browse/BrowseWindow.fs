#nowarn "0044"
module Eru.Tui.Browse.BrowseWindow

open System
open Terminal.Gui.App
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Terminal.Gui.Input
open Eru
open BrowseState

type BrowseWindow(deps: Deps, initialTab: ActiveTab, sources: SourceList.SourceRow list, lockEntries: LockEntry list) as this =
    inherit Window()

    let mutable currentTab = initialTab
    let mutable currentLockEntries = lockEntries

    let tabSrc      = new Label()
    let tabLock     = new Label()
    let filterLbl   = new Label()
    let filterField = new TextField()

    let sourcesPane = new SourcesPane.SourcesPane(deps, sources, lockEntries)
    let lockPane    = new LockPane.LockPane(lockEntries)

    let hintBar = new Label()

    let sourcesHint = " a:Add  A:Add source  r:Refresh  Tab:Switch  /:Filter  q:Quit"
    let lockHint    = " d:Disconnect  Del:Remove  A:Add source  Tab:Switch  /:Filter  q:Quit"

    let showError (msg: string) =
        MessageBox.Query(Application.Instance, "Error", msg, [| "OK" |]) |> ignore

    let showInfo (msg: string) =
        MessageBox.Query(Application.Instance, "Info", msg, [| "OK" |]) |> ignore

    let reloadLockEntries () =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Ok gc, Ok lc ->
            match Config.merge gc lc with
            | Ok eff ->
                match deps.ReadLockEntries eff.StateFile with
                | Ok entries ->
                    currentLockEntries <- entries
                    lockPane.Reload entries
                    sourcesPane.RefreshLockBadges entries
                | Error _ -> ()
            | Error _ -> ()
        | _ -> ()

    let showTab (tab: ActiveTab) =
        currentTab <- tab
        match tab with
        | SourcesTab ->
            tabSrc.Text  <- "[Sources]"
            tabLock.Text <- " Lock    "
            sourcesPane.Visible <- true
            lockPane.Visible    <- false
            hintBar.Text <- sourcesHint
        | LockTab ->
            tabSrc.Text  <- " Sources "
            tabLock.Text <- "[Lock]   "
            sourcesPane.Visible <- false
            lockPane.Visible    <- true
            hintBar.Text <- lockHint

    let applyFilter (text: string) =
        sourcesPane.ApplyFilter text
        lockPane.ApplyFilter text

    let handleAddFile (sourceName: string) (remotePath: string) =
        let cmd: Add.Command = {
            RemotePath     = Some $"{sourceName}:{remotePath}"
            Tags           = []
            SourceName     = Some sourceName
            CollectionName = None
            Target         = None
            DryRun         = false
            IsGlobal       = false
        }
        match Add.execute deps cmd with
        | Error e -> showError e
        | Ok entries ->
            let added = entries |> List.choose (function Add.Pulled e -> Some e | _ -> None)
            reloadLockEntries ()
            let paths = added |> List.map (fun e -> e.LocalPath) |> String.concat "\n"
            if paths <> "" then showInfo $"Added:\n{paths}"

    let handleAddSource () =
        match AddSourceDialog.show () with
        | None -> ()
        | Some input ->
            let cmd: SourceAdd.Command = {
                Url      = input.Url
                Name     = input.Name
                Branch   = input.Branch
                BasePath = input.BasePath
                IsGlobal = false
                DryRun   = false
            }
            match SourceAdd.execute deps cmd with
            | Error e -> showError e
            | Ok msg ->
                match SourceList.execute deps with
                | Ok newSources ->
                    sourcesPane.Reload(newSources, currentLockEntries)
                    showInfo msg
                | Error e -> showError e

    let handleRefreshSource (name: string) =
        let _ = Threading.Tasks.Task.Run(fun () ->
            try
                Sync.populateIndex deps |> ignore
                Application.Invoke(fun () ->
                    sourcesPane.RefreshSource name)
            with ex ->
                Application.Invoke(fun () ->
                    showError ex.Message))
        ()

    let handleDisconnect (localPath: string) =
        let r = MessageBox.Query(Application.Instance, "Disconnect",
                    $"Disconnect '{localPath}'?\n(Keeps the local file.)", [| "Yes"; "No" |])
        if r = 0 then
            let cmd: Disconnect.Command = { Target = localPath; DryRun = false }
            match Disconnect.execute deps cmd with
            | Error e -> showError e
            | Ok _    -> reloadLockEntries ()

    let handleRemoveEntry (localPath: string) =
        let r = MessageBox.Query(Application.Instance, "Remove",
                    $"Delete '{localPath}' from disk and lock file?", [| "Yes"; "No" |])
        if r = 0 then
            let cmd: Remove.Command = { Target = localPath; DryRun = false }
            match Remove.execute deps cmd with
            | Error e -> showError e
            | Ok _    -> reloadLockEntries ()

    let handleAction (action: BrowseAction) =
        match action with
        | AddFile(sn, rp)    -> handleAddFile sn rp
        | AddSource          -> handleAddSource ()
        | RefreshSource name -> handleRefreshSource name
        | Disconnect path    -> handleDisconnect path
        | RemoveEntry path   -> handleRemoveEntry path

    do
        this.Title <- "eru browse"
        this.X <- Pos.Absolute 0
        this.Y <- Pos.Absolute 0
        this.Width  <- Dim.Fill()
        this.Height <- Dim.Fill()

        tabSrc.Text  <- "[Sources]"
        tabSrc.X  <- Pos.Absolute 1
        tabSrc.Y  <- Pos.Absolute 0
        tabSrc.Width <- Dim.Absolute 9

        tabLock.Text <- " Lock    "
        tabLock.X <- Pos.Right tabSrc + Pos.Absolute 1
        tabLock.Y <- Pos.Absolute 0
        tabLock.Width <- Dim.Absolute 9

        filterLbl.Text <- " /Filter:"
        filterLbl.X <- Pos.Right tabLock + Pos.Absolute 2
        filterLbl.Y <- Pos.Absolute 0
        filterLbl.Width <- Dim.Absolute 9

        filterField.X <- Pos.Right filterLbl
        filterField.Y <- Pos.Absolute 0
        filterField.Width <- Dim.Fill(Dim.Absolute 1)

        filterField.ValueChanged.Add(fun e ->
            let text = if isNull (box e.NewValue) then "" else e.NewValue
            applyFilter text)

        sourcesPane.X <- Pos.Absolute 0
        sourcesPane.Y <- Pos.Absolute 1
        sourcesPane.Width  <- Dim.Fill()
        sourcesPane.Height <- Dim.Fill(Dim.Absolute 1)

        lockPane.X <- Pos.Absolute 0
        lockPane.Y <- Pos.Absolute 1
        lockPane.Width  <- Dim.Fill()
        lockPane.Height <- Dim.Fill(Dim.Absolute 1)

        hintBar.X <- Pos.Absolute 0
        hintBar.Y <- Pos.AnchorEnd()
        hintBar.Width  <- Dim.Fill()
        hintBar.Height <- Dim.Absolute 1

        this.Add(tabSrc, tabLock, filterLbl, filterField)
        this.Add(sourcesPane, lockPane, hintBar)

        sourcesPane.ActionRequested.Add(handleAction)
        lockPane.ActionRequested.Add(handleAction)

        showTab initialTab

    override _.OnKeyDown(key: Key) =
        if key.KeyCode = KeyCode.Tab then
            key.Handled <- true
            showTab (match currentTab with SourcesTab -> LockTab | LockTab -> SourcesTab)
        elif key.KeyCode = KeyCode.Q && not key.IsShift && not key.IsCtrl && not key.IsAlt then
            key.Handled <- true
            this.RequestStop()
        elif key.KeyCode = KeyCode.Esc && not filterField.HasFocus then
            key.Handled <- true
            this.RequestStop()
        elif key.KeyCode = KeyCode.Esc && filterField.HasFocus then
            key.Handled <- true
            filterField.Text <- ""
            applyFilter ""
            match currentTab with
            | SourcesTab -> sourcesPane.SetFocus() |> ignore
            | LockTab    -> lockPane.SetFocus() |> ignore
        elif not filterField.HasFocus && not key.IsShift && not key.IsCtrl && not key.IsAlt
             && key.AsRune.Value = int '/' then
            key.Handled <- true
            filterField.SetFocus() |> ignore
        base.OnKeyDown(key)
