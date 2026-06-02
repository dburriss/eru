#nowarn "0044"
module Eru.Tui.Browse.SourcesPane

open System.Collections.Generic
open Terminal.Gui.Drawing
open Terminal.Gui.App
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Eru
open BrowseState

type SourcesPane(deps: Deps, initialSources: SourceList.SourceRow list, initialLockEntries: LockEntry list) as this =
    inherit View()

    let actionEvent = Event<BrowseAction>()
    let fileCache = Dictionary<string, SourceFiles.SourceFileRow list>()
    let mutable currentFilter = ""
    let mutable lockEntries: LockEntry list = initialLockEntries
    let mutable allSources: SourceList.SourceRow list = initialSources
    let mutable searchHits: Set<string * string> option = None

    let makeFileNode (sourceName: string) (row: SourceFiles.SourceFileRow) =
        let entry =
            lockEntries |> List.tryFind (fun e ->
                e.SourceName = sourceName && e.RemotePath = row.Path)
        FileNode(row, sourceName, entry)

    let loadSourceFiles (sourceName: string) : SourceTreeNode seq =
        if not (fileCache.ContainsKey(sourceName)) then
            match SourceFiles.execute deps (Some sourceName) with
            | Ok results ->
                fileCache.[sourceName] <-
                    results
                    |> List.tryFind (fun (n, _) -> n = sourceName)
                    |> Option.map snd
                    |> Option.defaultValue []
            | Error _ -> fileCache.[sourceName] <- []
        fileCache.[sourceName]
        |> Seq.map (makeFileNode sourceName >> (fun n -> n :> SourceTreeNode))

    let runSearch (filter: string) =
        if filter = "" then
            searchHits <- None
        else
            let query =
                if filter.StartsWith("#") then
                    { Search.Terms = []; Search.Tags = [ filter.Substring(1).Trim() ] }
                else
                    { Search.Terms = [ filter ]; Search.Tags = [] }
            match Search.execute deps query with
            | Ok results ->
                searchHits <- Some (results |> List.map (fun r -> r.SourceName, r.RemotePath) |> Set.ofList)
            | Error _ ->
                searchHits <- None

    let treeView =
        let builder =
            DelegateTreeBuilder<SourceTreeNode>(
                (fun node ->
                    match node with
                    | :? SourceNode as sn ->
                        let children = loadSourceFiles sn.Source.Name
                        match searchHits with
                        | None      -> children
                        | Some hits ->
                            children |> Seq.filter (fun n ->
                                match n with
                                | :? FileNode as fn -> hits.Contains((fn.SourceName, fn.Row.Path))
                                | _ -> true)
                    | _ -> Seq.empty),
                (fun node -> node :? SourceNode))
        new TreeView<SourceTreeNode>(builder)

    let navLabel       = new Label()
    let detailView     = new View()
    let detailTitle    = new Label()
    let detailTags     = new Label()
    let detailMeta     = new Label()
    let previewRule    = new Label()
    let previewLabel   = new Label()
    let previewText = new TextView()

    let shortHash (hash: string) =
        if System.String.IsNullOrWhiteSpace hash then ""
        elif hash.Length <= 8 then hash
        else hash.Substring(0, 8)

    let tagsText (tags: string list) =
        tags |> String.concat " · "

    let setDetail title tags meta =
        detailTitle.Text <- title
        detailTags.Text <- tags
        detailMeta.Text <- meta

    let showSourceDetail (src: SourceList.SourceRow) =
        let url  = src.Url    |> Option.defaultValue "(none)"
        let br   = src.Branch |> Option.defaultValue "HEAD"
        let bp   = src.BasePath |> Option.defaultValue ""
        let tags = tagsText src.Tags
        setDetail
            src.Name
            tags
            $"URL {url}\nBranch {br}\nBase path {bp}\nScope {src.Scope}"

    let showFileDetail (fn: FileNode) =
        let tags    = tagsText fn.Row.Tags
        let desc    = fn.Row.Description |> Option.defaultValue ""
        let status  = if fn.IsTracked then "tracked" else "not tracked"
        let localPt =
            match fn.LockEntry with
            | Some e -> e.LocalPath
            | None -> "(not in lock file)"
        let descLine =
            if desc = "" then ""
            else $"\nDescription {desc}"
        setDetail
            fn.Row.Path
            tags
            $"Status {status}\nHash {shortHash fn.Row.Hash}\nSource {fn.SourceName}\nSource path {fn.Row.Path}\nLocal file {localPt}{descLine}"

    let updatePreview (fn: FileNode) =
        let path = fn.Row.Path
        if path.Contains('*') || path.Contains('?') then
            previewLabel.Text <- "Matching Files"
            match deps.ReadSourceIndex fn.SourceName with
            | Ok (Some idx) ->
                let matches =
                    idx
                    |> Map.toSeq
                    |> Seq.map fst
                    |> Seq.filter (Patterns.matchesGlob path)
                    |> Seq.sort
                    |> Seq.toArray
                previewText.Text <-
                    if matches.Length = 0 then "(no matching files in index)"
                    else matches |> String.concat "\n"
            | Ok None  -> previewText.Text <- "(source index not found)"
            | Error e  -> previewText.Text <- $"(error: {e})"
        elif path.EndsWith(".md", System.StringComparison.OrdinalIgnoreCase) then
            previewLabel.Text <- "Preview"
            match deps.ReadSourceIndex fn.SourceName with
            | Ok (Some idx) ->
                match Map.tryFind path idx with
                | Some entry when entry.CacheRelPath.IsSome ->
                    match deps.ReadCachedSourceContent fn.SourceName entry.CacheRelPath.Value with
                    | Ok (Some content) -> previewText.Text <- content
                    | Ok None           -> previewText.Text <- "(cached content not found)"
                    | Error e           -> previewText.Text <- $"(error: {e})"
                | Some _ -> previewText.Text <- "(content not in local cache)"
                | None   -> previewText.Text <- "(file not in index)"
            | Ok None  -> previewText.Text <- "(source index not found)"
            | Error e  -> previewText.Text <- $"(error: {e})"
        else
            previewLabel.Text <- "Preview"
            previewText.Text  <- ""

    let populateSources (filter: string) =
        runSearch filter
        treeView.ClearObjects()
        let filtered =
            match searchHits with
            | None      -> allSources
            | Some hits ->
                let matchedSources = hits |> Set.map fst
                allSources |> List.filter (fun s -> matchedSources.Contains(s.Name))
        for src in filtered do
            let node = SourceNode src
            treeView.AddObject(node)
            if searchHits.IsSome then treeView.Expand(node)

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
        treeView.Height <- Dim.Fill(Dim.Absolute 1)
        treeView.AllowLetterBasedNavigation <- false
        treeView.Style.ShowBranchLines <- false
        treeView.Style.HighlightModelTextOnly <- true
        BrowseTheme.apply BrowseTheme.Main treeView
        treeView.AspectGetter <- fun node ->
            let prefix =
                if System.Object.ReferenceEquals(node, treeView.SelectedObject) then "▌ "
                else "  "
            match node with
            | :? FileNode as fn ->
                let check = if fn.IsTracked then "[✓]" else "[ ]"
                $"{prefix}{check} {fn.Row.Path}"
            | _ -> $"{prefix}{node.Label}"
        populateSources ""
        this.Add(treeView) |> ignore

        detailView.X      <- Pos.Right treeView + Pos.Absolute 3
        detailView.Y      <- Pos.Absolute 1
        detailView.Width  <- Dim.Fill()
        detailView.Height <- Dim.Absolute 10
        BrowseTheme.apply BrowseTheme.Main detailView

        detailTitle.X      <- Pos.Absolute 0
        detailTitle.Y      <- Pos.Absolute 0
        detailTitle.Width  <- Dim.Fill()
        detailTitle.Height <- Dim.Absolute 1
        detailTitle.Text   <- "Select a source or file"
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
        detailMeta.Text   <- "Metadata from the selected item appears here."
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
            match e.NewValue with
            | :? FileNode as fn ->
                showFileDetail fn
                updatePreview fn
            | :? SourceNode as sn ->
                showSourceDetail sn.Source
                previewLabel.Text <- "Preview"
                previewText.Text  <- ""
            | _ -> ())

        treeView.KeyDown.Add(fun key ->
            if not key.Handled then
                if not key.IsShift && not key.IsCtrl && not key.IsAlt && key.AsRune.Value = int '/' then
                    key.Handled <- true
                    actionEvent.Trigger(FocusFilter)
                elif key.KeyCode = KeyCode.A && not key.IsShift then
                    match treeView.SelectedObject with
                    | :? FileNode as fn ->
                        key.Handled <- true
                        actionEvent.Trigger(AddFile(fn.SourceName, fn.Row.Path))
                    | _ -> ()
                elif key.KeyCode = (KeyCode.ShiftMask ||| KeyCode.A) then
                    key.Handled <- true
                    actionEvent.Trigger(AddSource)
                elif key.KeyCode = KeyCode.R && not key.IsShift then
                    let srcName =
                        match treeView.SelectedObject with
                        | :? FileNode as fn -> Some fn.SourceName
                        | :? SourceNode as sn -> Some sn.Source.Name
                        | _ -> None
                    match srcName with
                    | Some name ->
                        key.Handled <- true
                        actionEvent.Trigger(RefreshSource name)
                    | None -> ())

    member _.ActionRequested = actionEvent.Publish

    member _.FocusContent() = treeView.SetFocus() |> ignore

    member _.ApplyFilter(filter: string) =
        currentFilter <- filter
        populateSources filter

    member _.RefreshLockBadges(entries: LockEntry list) =
        lockEntries <- entries
        fileCache.Clear()
        populateSources currentFilter

    member _.Reload(sources: SourceList.SourceRow list, entries: LockEntry list) =
        allSources <- sources
        lockEntries <- entries
        fileCache.Clear()
        populateSources currentFilter

    member _.RefreshSource(sourceName: string) =
        if fileCache.ContainsKey(sourceName) then
            fileCache.Remove(sourceName) |> ignore
        treeView.RebuildTree()
