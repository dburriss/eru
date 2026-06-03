#nowarn "0044"
module Eru.Tui.Browse.SourcesPane

open Terminal.Gui.App
open Terminal.Gui.Drawing
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Eru
open BrowseState

type SourcesPane(initialSources: SourceList.SourceRow list) as this =
    inherit View()

    let actionEvent = Event<BrowseAction>()
    let mutable allSources: SourceList.SourceRow list = initialSources
    let mutable currentFilter = ""

    let navLabel     = new Label()
    let searchRow    = new View()
    let searchPrompt = new Label()
    let searchField  = new TextField()
    let treeView     =
        let builder = DelegateTreeBuilder<SourceNode>(
            (fun _ -> Seq.empty),
            (fun _ -> false))
        new TreeView<SourceNode>(builder)
    let detailView   = new View()
    let detailTitle  = new Label()
    let detailTags   = new Label()
    let detailMeta   = new Label()
    let previewRule  = new Label()
    let previewLabel = new Label()
    let previewText  = new TextView()

    let tagsText (tags: string list) =
        tags |> String.concat " · "

    let showEmpty () =
        detailTitle.Text <- "No sources"
        detailTags.Text <- ""
        detailMeta.Text <- "No sources match the current filter."
        previewLabel.Text <- "Info"
        previewText.Text <- ""

    let showSource (src: SourceList.SourceRow) =
        let url  = src.Url    |> Option.defaultValue "(none)"
        let br   = src.Branch |> Option.defaultValue "HEAD"
        let bp   = src.BasePath |> Option.defaultValue ""
        detailTitle.Text <- src.Name
        detailTags.Text <- tagsText src.Tags
        detailMeta.Text <- $"URL {url}\nBranch {br}\nBase path {bp}\nScope {src.Scope}"
        previewLabel.Text <- "Info"
        previewText.Text <- ""

    let populate () =
        treeView.ClearObjects()
        let filtered =
            if currentFilter = "" then allSources
            else
                allSources |> List.filter (fun s ->
                    s.Name.Contains(currentFilter, System.StringComparison.OrdinalIgnoreCase))
        for src in filtered do
            treeView.AddObject(SourceNode src)
        match filtered with
        | first :: _ -> showSource first
        | [] -> showEmpty ()

    do
        this.CanFocus <- true
        BrowseTheme.apply BrowseTheme.Main this

        navLabel.Text <- "Sources"
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
            $"{prefix}{node.Source.Name}"
        populate ()
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
        detailTitle.Text   <- "Select a source"
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

        previewLabel.Text <- "Info"
        previewLabel.X <- Pos.Right treeView + Pos.Absolute 3
        previewLabel.Y <- Pos.Bottom previewRule + Pos.Absolute 1
        previewLabel.Width <- Dim.Fill()
        previewLabel.Height <- Dim.Absolute 1
        BrowseTheme.apply BrowseTheme.Muted previewLabel
        this.Add(previewLabel) |> ignore

        previewText.X        <- Pos.Right treeView + Pos.Absolute 3
        previewText.Y        <- Pos.Bottom previewLabel + Pos.Absolute 1
        previewText.Width    <- Dim.Fill()
        previewText.Height   <- Dim.Fill()
        previewText.ReadOnly <- true
        previewText.WordWrap <- true
        previewText.CanFocus <- false
        BrowseTheme.apply BrowseTheme.Main previewText
        this.Add(previewText) |> ignore

        treeView.SelectionChanged.Add(fun e ->
            if isNull (box e.NewValue) then showEmpty ()
            else showSource e.NewValue.Source)

        searchField.ValueChanged.Add(fun e ->
            let text = if isNull (box e.NewValue) then "" else e.NewValue
            currentFilter <- text
            populate ())

        searchField.KeyDown.Add(fun key ->
            if key.KeyCode = KeyCode.Esc then
                key.Handled <- true
                searchField.Text <- ""
                currentFilter <- ""
                populate ()
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
                elif key.KeyCode = (KeyCode.ShiftMask ||| KeyCode.A) then
                    key.Handled <- true
                    actionEvent.Trigger(AddSource)
                elif key.KeyCode = (KeyCode.ShiftMask ||| KeyCode.X) then
                    let node = treeView.SelectedObject
                    if not (isNull (box node)) then
                        key.Handled <- true
                        actionEvent.Trigger(RemoveSource node.Source.Name)
                elif key.KeyCode = KeyCode.R && not key.IsShift then
                    let node = treeView.SelectedObject
                    if not (isNull (box node)) then
                        key.Handled <- true
                        actionEvent.Trigger(RefreshSource))

    member _.ActionRequested = actionEvent.Publish

    member _.FocusContent() = treeView.SetFocus() |> ignore

    member _.Reload(sources: SourceList.SourceRow list) =
        allSources <- sources
        populate ()
