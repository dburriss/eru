#nowarn "0044"
#nowarn "3391"
module Eru.Tui.Browse.BrowseWindow

open System
open Terminal.Gui.App
open Terminal.Gui.Drawing
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

    let _ = BrowseTheme.register ()

    let topBar      = new View()
    let tabSrc      = new Label()
    let tabLock     = new Label()
    let filterLbl   = new Label()
    let filterField = new TextField()

    let sourcesPane = new SourcesPane.SourcesPane(deps, sources, lockEntries)
    let lockPane    = new LockPane.LockPane(lockEntries)

    let hintBar = new Label()

    let sourcesHint = " [a] add  [r] refresh  [A] source  [/] filter  [tab] local files  [q] quit"
    let lockHint    = " [d] disconnect  [del] remove  [A] source  [/] filter  [tab] sources  [q] quit"

    let styledDialog (title: string) (msg: string) (msgScheme: string) (buttons: string[]) =
        let mutable result = -1
        let lineCount = msg.Split('\n').Length
        let dlg = new Dialog()
        dlg.Title <- title
        dlg.Width <- Dim.Absolute 60
        dlg.Height <- Dim.Absolute (max 9 (lineCount + 7))
        BrowseTheme.apply BrowseTheme.Main dlg
        let tv = new TextView()
        tv.Text <- msg
        tv.ReadOnly <- true
        tv.CanFocus <- false
        tv.X <- Pos.Absolute 1
        tv.Y <- Pos.Absolute 1
        tv.Width <- Dim.Absolute 56
        tv.Height <- Dim.Absolute lineCount
        BrowseTheme.apply msgScheme tv
        dlg.Add(tv) |> ignore
        for i in 0 .. buttons.Length - 1 do
            let btn = new Button()
            btn.Text <- buttons.[i]
            if i = 0 then btn.IsDefault <- true
            let idx = i
            btn.Accepting.Add(fun _ -> result <- idx; dlg.RequestStop())
            BrowseTheme.apply BrowseTheme.Accent btn
            dlg.AddButton(btn)
        Application.Run(dlg, Unchecked.defaultof<_>)
        result

    let showError (msg: string) =
        styledDialog "Error" msg BrowseTheme.Danger [| "OK" |] |> ignore

    let showInfo (msg: string) =
        styledDialog "Info" msg BrowseTheme.Muted [| "OK" |] |> ignore

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
            tabSrc.Text  <- "[ Sources ]"
            tabLock.Text <- "  Local Files"
            sourcesPane.Visible <- true
            lockPane.Visible    <- false
            hintBar.Text <- sourcesHint
            sourcesPane.FocusContent()
        | LockTab ->
            tabSrc.Text  <- "  Sources  "
            tabLock.Text <- "[ Local Files ]"
            sourcesPane.Visible <- false
            lockPane.Visible    <- true
            hintBar.Text <- lockHint
            lockPane.FocusContent()

    let applyFilter (text: string) =
        sourcesPane.ApplyFilter text
        lockPane.ApplyFilter text

    let restoreHint () =
        hintBar.Text <- match currentTab with SourcesTab -> sourcesHint | LockTab -> lockHint

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
        hintBar.Text <- $" Adding {remotePath}..."
        let _ = Threading.Tasks.Task.Run(fun () ->
            try
                let result = Add.execute deps cmd
                Application.Invoke(fun () ->
                    restoreHint ()
                    match result with
                    | Error e -> showError e
                    | Ok entries ->
                        let added = entries |> List.choose (function Add.Pulled e -> Some e | _ -> None)
                        reloadLockEntries ()
                        if added.Length > 0 then
                            let noun = if added.Length = 1 then "file" else "files"
                            showInfo $"Added {added.Length} {noun}.")
            with ex ->
                Application.Invoke(fun () ->
                    restoreHint ()
                    showError ex.Message))
        ()

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
        hintBar.Text <- $" Refreshing {name}..."
        let _ = Threading.Tasks.Task.Run(fun () ->
            try
                Sync.populateIndex deps |> ignore
                Application.Invoke(fun () ->
                    restoreHint ()
                    sourcesPane.RefreshSource name)
            with ex ->
                Application.Invoke(fun () ->
                    restoreHint ()
                    showError ex.Message))
        ()

    let handleDisconnect (localPath: string) =
        let r = styledDialog "Disconnect"
                    $"Disconnect '{localPath}'?\n(Keeps the local file.)"
                    BrowseTheme.Muted [| "Yes"; "No" |]
        if r = 0 then
            let cmd: Disconnect.Command = { Target = localPath; DryRun = false }
            match Disconnect.execute deps cmd with
            | Error e -> showError e
            | Ok _    -> reloadLockEntries ()

    let handleRemoveEntry (localPath: string) =
        let r = styledDialog "Remove"
                    $"Delete '{localPath}' from disk and lock file?"
                    BrowseTheme.Danger [| "Yes"; "No" |]
        if r = 0 then
            let cmd: Remove.Command = { Target = localPath; DryRun = false }
            match Remove.execute deps cmd with
            | Error e -> showError e
            | Ok _    -> reloadLockEntries ()

    let focusFilter () =
        filterField.CanFocus <- true
        filterField.SetFocus() |> ignore

    let handleAction (action: BrowseAction) =
        match action with
        | AddFile(sn, rp)    -> handleAddFile sn rp
        | AddSource          -> handleAddSource ()
        | RefreshSource name -> handleRefreshSource name
        | Disconnect path    -> handleDisconnect path
        | RemoveEntry path   -> handleRemoveEntry path
        | FocusFilter        -> focusFilter ()

    do
        this.Title <- "eru browse"
        this.BorderStyle <- LineStyle.None
        BrowseTheme.apply BrowseTheme.Main this
        this.X <- Pos.Absolute 0
        this.Y <- Pos.Absolute 0
        this.Width  <- Dim.Fill()
        this.Height <- Dim.Fill()

        topBar.X <- Pos.Absolute 0
        topBar.Y <- Pos.Absolute 0
        topBar.Width <- Dim.Fill()
        topBar.Height <- Dim.Absolute 1
        topBar.CanFocus <- true
        BrowseTheme.apply BrowseTheme.Bar topBar

        tabSrc.Text  <- "[ Sources ]"
        tabSrc.X  <- Pos.Absolute 1
        tabSrc.Y  <- Pos.Absolute 0
        tabSrc.Width <- Dim.Absolute 12
        BrowseTheme.apply BrowseTheme.Accent tabSrc
        tabSrc.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab SourcesTab)

        tabLock.Text <- "  Local Files"
        tabLock.X <- Pos.Right tabSrc + Pos.Absolute 2
        tabLock.Y <- Pos.Absolute 0
        tabLock.Width <- Dim.Absolute 17
        BrowseTheme.apply BrowseTheme.Accent tabLock
        tabLock.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab LockTab)

        filterLbl.Text <- "filter"
        filterLbl.X <- Pos.Right tabLock + Pos.Absolute 4
        filterLbl.Y <- Pos.Absolute 0
        filterLbl.Width <- Dim.Absolute 7
        BrowseTheme.apply BrowseTheme.Muted filterLbl

        filterField.X <- Pos.Right filterLbl + Pos.Absolute 1
        filterField.Y <- Pos.Absolute 0
        filterField.Width <- Dim.Fill(Dim.Absolute 1)
        filterField.CanFocus <- false
        BrowseTheme.apply BrowseTheme.Bar filterField
        filterField.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then focusFilter())

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
        BrowseTheme.apply BrowseTheme.Bar hintBar

        topBar.Add(tabSrc, tabLock, filterLbl, filterField) |> ignore
        this.Add(topBar) |> ignore
        this.Add(sourcesPane, lockPane, hintBar) |> ignore

        sourcesPane.ActionRequested.Add(handleAction)
        lockPane.ActionRequested.Add(handleAction)

        showTab initialTab
        Application.AddTimeout(
            TimeSpan.Zero,
            fun () ->
                (match currentTab with
                 | SourcesTab -> sourcesPane.FocusContent()
                 | LockTab    -> lockPane.FocusContent())
                false)
        |> ignore

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
        elif key.KeyCode = KeyCode.Enter && filterField.HasFocus then
            key.Handled <- true
            filterField.CanFocus <- false
            match currentTab with
            | SourcesTab -> sourcesPane.FocusContent()
            | LockTab    -> lockPane.FocusContent()
        elif key.KeyCode = KeyCode.Esc && filterField.HasFocus then
            key.Handled <- true
            filterField.Text <- ""
            filterField.CanFocus <- false
            applyFilter ""
            match currentTab with
            | SourcesTab -> sourcesPane.FocusContent()
            | LockTab    -> lockPane.FocusContent()
        base.OnKeyDown(key)
