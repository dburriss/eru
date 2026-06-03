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

    let topBar         = new View()
    let tabFiles       = new Label()
    let tabSources     = new Label()
    let tabLocal       = new Label()
    let tabCollections = new Label()
    let tabConfig      = new Label()

    let filesPane       = new FilesPane.FilesPane(deps, sources, lockEntries)
    let sourcesPane     = new SourcesPane.SourcesPane(sources)
    let lockPane        = new LockPane.LockPane(lockEntries)
    let collectionsPane = new CollectionsPane.CollectionsPane()
    let configPane      = new ConfigPane.ConfigPane()

    let hintBar = new Label()

    let filesHint       = " [a] add  [x] remove  [r] refresh  [/] search  [tab] sources  [q] quit"
    let sourcesHint     = " [A] add source  [X] remove source  [r] refresh  [/] search  [tab] local  [q] quit"
    let localHint       = " [x] remove  [d] disconnect  [r] refresh  [/] search  [tab] collections  [q] quit"
    let collectionsHint = " [tab] config  [q] quit"
    let configHint      = " [tab] files  [q] quit"

    let tabHint tab =
        match tab with
        | FilesTab       -> filesHint
        | SourcesTab     -> sourcesHint
        | LocalTab       -> localHint
        | CollectionsTab -> collectionsHint
        | ConfigTab      -> configHint

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
                    filesPane.RefreshLockBadges entries
                | Error _ -> ()
            | Error _ -> ()
        | _ -> ()

    let showTab (tab: ActiveTab) =
        currentTab <- tab
        filesPane.Visible       <- false
        sourcesPane.Visible     <- false
        lockPane.Visible        <- false
        collectionsPane.Visible <- false
        configPane.Visible      <- false
        tabFiles.Text       <- "  Files  "
        tabSources.Text     <- "  Sources  "
        tabLocal.Text       <- "  Local  "
        tabCollections.Text <- "  Collections  "
        tabConfig.Text      <- "  Config  "
        hintBar.Text <- tabHint tab
        match tab with
        | FilesTab ->
            tabFiles.Text <- "[ Files ]"
            filesPane.Visible <- true
            filesPane.FocusContent()
        | SourcesTab ->
            tabSources.Text <- "[ Sources ]"
            sourcesPane.Visible <- true
            sourcesPane.FocusContent()
        | LocalTab ->
            tabLocal.Text <- "[ Local ]"
            lockPane.Visible <- true
            lockPane.FocusContent()
        | CollectionsTab ->
            tabCollections.Text <- "[ Collections ]"
            collectionsPane.Visible <- true
            collectionsPane.FocusContent()
        | ConfigTab ->
            tabConfig.Text <- "[ Config ]"
            configPane.Visible <- true
            configPane.FocusContent()

    let restoreHint () =
        hintBar.Text <- tabHint currentTab

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
                    sourcesPane.Reload newSources
                    filesPane.Reload(newSources, currentLockEntries)
                    showInfo msg
                | Error e -> showError e

    let handleRefreshSource () =
        hintBar.Text <- " Refreshing sources..."
        let _ = Threading.Tasks.Task.Run(fun () ->
            try
                Sync.populateIndex deps |> ignore
                Application.Invoke(fun () ->
                    restoreHint ()
                    filesPane.RefreshAll())
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

    let handleRemoveGlob (sourceName: string) (globPath: string) =
        let matching =
            currentLockEntries
            |> List.filter (fun e ->
                e.SourceName = sourceName &&
                Patterns.matchesGlob globPath e.RemotePath)
        if matching.IsEmpty then
            showError $"No tracked files match '{globPath}'."
        else
            let noun  = if matching.Length = 1 then "file" else "files"
            let paths = matching |> List.map (fun e -> $"  {e.LocalPath}") |> String.concat "\n"
            let r = styledDialog "Remove"
                        $"Delete {matching.Length} {noun} from disk and lock file?\n{paths}"
                        BrowseTheme.Danger [| "Yes"; "No" |]
            if r = 0 then
                let errors =
                    matching
                    |> List.choose (fun e ->
                        let cmd: Remove.Command = { Target = e.LocalPath; DryRun = false }
                        match Remove.execute deps cmd with
                        | Error err -> Some err
                        | Ok _      -> None)
                if errors.IsEmpty then
                    reloadLockEntries ()
                else
                    reloadLockEntries ()
                    showError (errors |> String.concat "\n")

    let handleRemoveSource (name: string) =
        let r = styledDialog "Remove Source"
                    $"Remove source '{name}' from config?"
                    BrowseTheme.Danger [| "Yes"; "No" |]
        if r = 0 then
            let cmd: SourceRemove.Command = { Name = name; IsGlobal = false; DryRun = false }
            match SourceRemove.execute deps cmd with
            | Error e -> showError e
            | Ok msg ->
                match SourceList.execute deps with
                | Ok newSources ->
                    sourcesPane.Reload newSources
                    filesPane.Reload(newSources, currentLockEntries)
                    showInfo msg
                | Error e -> showError e

    let handleAction (action: BrowseAction) =
        match action with
        | AddFile(sn, rp)          -> handleAddFile sn rp
        | AddSource                -> handleAddSource ()
        | RefreshSource            -> handleRefreshSource ()
        | Disconnect path          -> handleDisconnect path
        | RemoveEntry path         -> handleRemoveEntry path
        | RemoveGlob(sn, globPath) -> handleRemoveGlob sn globPath
        | RemoveSource name        -> handleRemoveSource name

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
        BrowseTheme.apply BrowseTheme.Bar topBar

        tabFiles.Text <- "[ Files ]"
        tabFiles.X <- Pos.Absolute 1
        tabFiles.Y <- Pos.Absolute 0
        tabFiles.Width <- Dim.Absolute 10
        BrowseTheme.apply BrowseTheme.Accent tabFiles
        tabFiles.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab FilesTab)

        tabSources.Text <- "  Sources  "
        tabSources.X <- Pos.Right tabFiles + Pos.Absolute 1
        tabSources.Y <- Pos.Absolute 0
        tabSources.Width <- Dim.Absolute 12
        BrowseTheme.apply BrowseTheme.Accent tabSources
        tabSources.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab SourcesTab)

        tabLocal.Text <- "  Local  "
        tabLocal.X <- Pos.Right tabSources + Pos.Absolute 1
        tabLocal.Y <- Pos.Absolute 0
        tabLocal.Width <- Dim.Absolute 10
        BrowseTheme.apply BrowseTheme.Accent tabLocal
        tabLocal.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab LocalTab)

        tabCollections.Text <- "  Collections  "
        tabCollections.X <- Pos.Right tabLocal + Pos.Absolute 1
        tabCollections.Y <- Pos.Absolute 0
        tabCollections.Width <- Dim.Absolute 16
        BrowseTheme.apply BrowseTheme.Accent tabCollections
        tabCollections.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab CollectionsTab)

        tabConfig.Text <- "  Config  "
        tabConfig.X <- Pos.Right tabCollections + Pos.Absolute 1
        tabConfig.Y <- Pos.Absolute 0
        tabConfig.Width <- Dim.Absolute 11
        BrowseTheme.apply BrowseTheme.Accent tabConfig
        tabConfig.MouseEvent.Add(fun mouse ->
            if mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) then showTab ConfigTab)

        let panePos (p: View) =
            p.X <- Pos.Absolute 0
            p.Y <- Pos.Absolute 1
            p.Width  <- Dim.Fill()
            p.Height <- Dim.Fill(Dim.Absolute 1)

        panePos filesPane
        panePos sourcesPane
        panePos lockPane
        panePos collectionsPane
        panePos configPane

        hintBar.X <- Pos.Absolute 0
        hintBar.Y <- Pos.AnchorEnd()
        hintBar.Width  <- Dim.Fill()
        hintBar.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Bar hintBar

        topBar.Add(tabFiles, tabSources, tabLocal, tabCollections, tabConfig) |> ignore
        this.Add(topBar) |> ignore
        this.Add(filesPane, sourcesPane, lockPane, collectionsPane, configPane, hintBar) |> ignore

        filesPane.ActionRequested.Add(handleAction)
        sourcesPane.ActionRequested.Add(handleAction)
        lockPane.ActionRequested.Add(handleAction)

        showTab initialTab
        Application.AddTimeout(
            TimeSpan.Zero,
            fun () ->
                (match currentTab with
                 | FilesTab       -> filesPane.FocusContent()
                 | SourcesTab     -> sourcesPane.FocusContent()
                 | LocalTab       -> lockPane.FocusContent()
                 | CollectionsTab -> collectionsPane.FocusContent()
                 | ConfigTab      -> configPane.FocusContent())
                false)
        |> ignore

    override _.OnKeyDown(key: Key) =
        if key.KeyCode = KeyCode.Tab then
            key.Handled <- true
            showTab (match currentTab with
                     | FilesTab       -> SourcesTab
                     | SourcesTab     -> LocalTab
                     | LocalTab       -> CollectionsTab
                     | CollectionsTab -> ConfigTab
                     | ConfigTab      -> FilesTab)
        elif key.KeyCode = KeyCode.Q && not key.IsShift && not key.IsCtrl && not key.IsAlt then
            key.Handled <- true
            this.RequestStop()
        elif key.KeyCode = KeyCode.Esc then
            key.Handled <- true
            this.RequestStop()
        base.OnKeyDown(key)
