#nowarn "0044"
module Eru.Tui.Browse.LockPane

open System.IO
open System.Text
open Terminal.Gui.App
open Terminal.Gui.Drawing
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Eru
open BrowseState

type private TrackedFileNode(entry: LockEntry) =
    member _.Entry = entry
    override _.ToString() = entry.LocalPath

type LockPane(initialEntries: LockEntry list) as this =
    inherit View()

    let actionEvent = Event<BrowseAction>()
    let mutable allEntries: LockEntry list = initialEntries
    let mutable filteredEntries: LockEntry list = initialEntries
    let mutable currentFilter = ""
    let mutable filteredNodes: TrackedFileNode list = []

    let navLabel     = new Label()
    let searchRow    = new View()
    let searchPrompt = new Label()
    let searchField  = new TextField()
    let treeView     =
        let builder = DelegateTreeBuilder<TrackedFileNode>(
            (fun _ -> Seq.empty),
            (fun _ -> false))
        new TreeView<TrackedFileNode>(builder)
    let detailView   = new View()
    let detailTitle  = new Label()
    let detailTags   = new Label()
    let detailMeta   = new Label()
    let previewRule  = new Label()
    let previewLabel = new Label()
    let previewText  = new TextView()

    let shortHash (hash: string) =
        if System.String.IsNullOrWhiteSpace hash then ""
        elif hash.Length <= 8 then hash
        else hash.Substring(0, 8)

    let tagsText (tags: string list) =
        tags |> String.concat " · "

    let readLocalPreview (entry: LockEntry) =
        try
            if not (File.Exists entry.LocalPath) then
                "(local file not found)"
            else
                let bytes = File.ReadAllBytes entry.LocalPath
                if bytes |> Array.exists ((=) 0uy) then
                    "(binary content not shown)"
                else
                    let maxBytes = 24000
                    let take = min bytes.Length maxBytes
                    let text = Encoding.UTF8.GetString(bytes, 0, take)
                    if bytes.Length > maxBytes then
                        $"{text}\n\n(preview truncated)"
                    else text
        with ex ->
            $"(error reading local file: {ex.Message})"

    let showEmpty () =
        detailTitle.Text <- "No tracked files"
        detailTags.Text <- ""
        detailMeta.Text <- "The lock file has no entries matching the current filter."
        previewLabel.Text <- "Preview"
        previewText.Text <- ""

    let showEntry (entry: LockEntry) =
        detailTitle.Text <- entry.LocalPath
        detailTags.Text <- tagsText entry.Tags
        let desc =
            match entry.Description with
            | Some d when d <> "" -> $"\nDescription {d}"
            | _ -> ""
        detailMeta.Text <-
            $"Status tracked\nHash {shortHash entry.ContentHash}\nSource {entry.SourceName}\nSource path {entry.RemotePath}\nLock file .eru/eru.lock{desc}"
        previewLabel.Text <- "Preview"
        previewText.Text <- readLocalPreview entry

    let populate () =
        treeView.ClearObjects()
        filteredNodes <- filteredEntries |> List.map TrackedFileNode
        for node in filteredNodes do
            treeView.AddObject(node)
        match filteredNodes with
        | first :: _ ->
            treeView.GoTo(first)
            showEntry first.Entry
        | [] -> showEmpty ()

    let applyFilter (filter: string) =
        currentFilter <- filter
        filteredEntries <-
            if filter = "" then allEntries
            else
                allEntries |> List.filter (fun e ->
                    e.LocalPath.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                    e.SourceName.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                    e.RemotePath.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
        populate ()

    let selectedEntry () =
        let node = treeView.SelectedObject
        if isNull (box node) then None
        else Some node.Entry

    do
        this.CanFocus <- true
        BrowseTheme.apply BrowseTheme.Main this

        navLabel.Text <- "Tracked Files"
        navLabel.X <- Pos.Absolute 1
        navLabel.Y <- Pos.Absolute 1
        navLabel.Width <- Dim.Percent(35, DimPercentMode.ContentSize)
        navLabel.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Muted navLabel
        this.Add(navLabel) |> ignore

        treeView.X <- Pos.Absolute 1
        treeView.Y <- Pos.Absolute 3
        treeView.Width <- Dim.Percent(35, DimPercentMode.ContentSize)
        treeView.Height <- Dim.Fill(Dim.Absolute 2)
        treeView.AllowLetterBasedNavigation <- false
        treeView.Style.ShowBranchLines <- false
        treeView.Style.HighlightModelTextOnly <- true
        BrowseTheme.apply BrowseTheme.Main treeView
        treeView.AspectGetter <- fun node ->
            let prefix =
                if System.Object.ReferenceEquals(node, treeView.SelectedObject) then "▌ "
                else "  "
            $"{prefix}{node.Entry.LocalPath}"
        this.Add(treeView) |> ignore

        searchRow.X <- Pos.Absolute 1
        searchRow.Y <- Pos.Bottom treeView
        searchRow.Width <- Dim.Percent(35, DimPercentMode.ContentSize)
        searchRow.Height <- Dim.Absolute 1
        searchRow.CanFocus <- true
        BrowseTheme.apply BrowseTheme.Bar searchRow

        searchPrompt.Text <- "/"
        searchPrompt.X <- Pos.Absolute 0
        searchPrompt.Y <- Pos.Absolute 0
        searchPrompt.Width <- Dim.Absolute 2
        BrowseTheme.apply BrowseTheme.Muted searchPrompt
        searchRow.Add(searchPrompt) |> ignore

        searchField.X <- Pos.Right searchPrompt
        searchField.Y <- Pos.Absolute 0
        searchField.Width <- Dim.Fill()
        searchField.Height <- Dim.Absolute 1
        searchField.CanFocus <- false
        BrowseTheme.apply BrowseTheme.Bar searchField
        searchRow.Add(searchField) |> ignore
        this.Add(searchRow) |> ignore

        detailView.X      <- Pos.Right treeView + Pos.Absolute 3
        detailView.Y      <- Pos.Absolute 1
        detailView.Width  <- Dim.Fill()
        detailView.Height <- Dim.Absolute 10
        BrowseTheme.apply BrowseTheme.Main detailView

        detailTitle.X      <- Pos.Absolute 0
        detailTitle.Y      <- Pos.Absolute 0
        detailTitle.Width  <- Dim.Fill()
        detailTitle.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Accent detailTitle
        detailView.Add(detailTitle) |> ignore

        detailTags.X      <- Pos.Absolute 0
        detailTags.Y      <- Pos.Absolute 2
        detailTags.Width  <- Dim.Fill()
        detailTags.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Tracked detailTags
        detailView.Add(detailTags) |> ignore

        detailMeta.X      <- Pos.Absolute 0
        detailMeta.Y      <- Pos.Absolute 4
        detailMeta.Width  <- Dim.Fill()
        detailMeta.Height <- Dim.Fill()
        BrowseTheme.apply BrowseTheme.Muted detailMeta
        detailView.Add(detailMeta) |> ignore
        this.Add(detailView) |> ignore

        previewRule.Text <- "────────────────────────────────────────────────────────────────────────────────────────────────────"
        previewRule.X <- Pos.Right treeView + Pos.Absolute 3
        previewRule.Y <- Pos.Bottom detailView
        previewRule.Width <- Dim.Fill()
        previewRule.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Dim previewRule
        this.Add(previewRule) |> ignore

        previewLabel.Text <- "Preview"
        previewLabel.X <- Pos.Right treeView + Pos.Absolute 3
        previewLabel.Y <- Pos.Bottom previewRule + Pos.Absolute 1
        previewLabel.Width <- Dim.Fill()
        previewLabel.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Muted previewLabel
        this.Add(previewLabel) |> ignore

        previewText.X <- Pos.Right treeView + Pos.Absolute 3
        previewText.Y <- Pos.Bottom previewLabel + Pos.Absolute 1
        previewText.Width <- Dim.Fill()
        previewText.Height <- Dim.Fill()
        previewText.ReadOnly <- true
        previewText.WordWrap <- true
        previewText.CanFocus <- false
        BrowseTheme.apply BrowseTheme.Main previewText
        this.Add(previewText) |> ignore

        treeView.SelectionChanged.Add(fun e ->
            if isNull (box e.NewValue) then showEmpty ()
            else showEntry e.NewValue.Entry)

        searchField.ValueChanged.Add(fun e ->
            let text = if isNull (box e.NewValue) then "" else e.NewValue
            applyFilter text)

        searchField.KeyDown.Add(fun key ->
            if key.KeyCode = KeyCode.Esc then
                key.Handled <- true
                searchField.Text <- ""
                applyFilter ""
                searchField.CanFocus <- false
                treeView.SetFocus() |> ignore
            elif key.KeyCode = KeyCode.Enter then
                key.Handled <- true
                searchField.CanFocus <- false
                treeView.SetFocus() |> ignore)

        treeView.KeyDown.Add(fun key ->
            if not key.Handled then
                if not key.IsShift && not key.IsCtrl && not key.IsAlt && key.AsRune.Value = int '/' then
                    key.Handled <- true
                    searchField.CanFocus <- true
                    searchField.SetFocus() |> ignore
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
                    | None -> ())

        populate ()

    member _.ActionRequested = actionEvent.Publish

    member _.FocusContent() = treeView.SetFocus() |> ignore

    member _.Reload(entries: LockEntry list) =
        allEntries <- entries
        applyFilter currentFilter
