#nowarn "0044"
module Eru.Tui.Browse.FilesPane

open System.Collections.Generic
open Terminal.Gui.Drawing
open Terminal.Gui.App
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Eru
open BrowseState

type FilesPane(deps: Deps, initialSources: SourceList.SourceRow list, initialLockEntries: LockEntry list) as this =
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
        let globAllTracked =
            if row.Path.Contains('*') || row.Path.Contains('?') then
                match deps.ReadSourceIndex sourceName with
                | Ok (Some idx) ->
                    let matchingPaths =
                        idx
                        |> Map.toSeq
                        |> Seq.map fst
                        |> Seq.filter (fun k -> not (k.Contains('*') || k.Contains('?')))
                        |> Seq.filter (Patterns.matchesGlob row.Path)
                        |> Seq.toList
                    matchingPaths.Length > 0 &&
                    (matchingPaths |> List.forall (fun p ->
                        lockEntries |> List.exists (fun e ->
                            e.SourceName = sourceName && e.RemotePath = p)))
                | _ -> false
            else false
        FileNode(row, sourceName, entry, globAllTracked)

    let loadGlobChildren (gn: GlobNode) : SourceTreeNode seq =
        match deps.ReadSourceIndex gn.SourceName with
        | Ok (Some idx) ->
            idx
            |> Map.toSeq
            |> Seq.filter (fun (k, _) -> not (k.Contains('*') || k.Contains('?')))
            |> Seq.filter (fun (k, _) -> Patterns.matchesGlob gn.FileNode.Row.Path k)
            |> Seq.sortBy fst
            |> Seq.map (fun (path, entry) ->
                let row: SourceFiles.SourceFileRow = { Hash = Patterns.pathShortHash path; Path = path; Tags = entry.Tags; Description = entry.Description }
                makeFileNode gn.SourceName row :> SourceTreeNode)
        | _ -> Seq.empty

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
        let rows = fileCache.[sourceName]
        let globs = rows |> List.choose (fun r -> if r.Path.Contains('*') || r.Path.Contains('?') then Some r.Path else None)
        rows
        |> Seq.choose (fun row ->
            if row.Path.Contains('*') || row.Path.Contains('?') then
                Some (GlobNode(makeFileNode sourceName row) :> SourceTreeNode)
            elif globs |> List.exists (fun g -> Patterns.matchesGlob g row.Path) then
                None
            else
                Some (makeFileNode sourceName row :> SourceTreeNode))

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

    let filterBySearchHits (children: SourceTreeNode seq) =
        match searchHits with
        | None      -> children
        | Some hits ->
            children |> Seq.filter (fun n ->
                match n with
                | :? FileNode as fn -> hits.Contains((fn.SourceName, fn.Row.Path))
                | _ -> true)

    let treeView =
        let builder =
            DelegateTreeBuilder<SourceTreeNode>(
                (fun node ->
                    match node with
                    | :? SourceNode as sn -> loadSourceFiles sn.Source.Name |> filterBySearchHits
                    | :? GlobNode  as gn -> loadGlobChildren gn |> filterBySearchHits
                    | _ -> Seq.empty),
                (fun node -> node :? SourceNode || node :? GlobNode))
        new TreeView<SourceTreeNode>(builder)

    let saveSelectionKey () : (string * string option) option =
        match treeView.SelectedObject with
        | :? SourceNode as sn -> Some (sn.Source.Name, None)
        | :? FileNode   as fn -> Some (fn.SourceName, Some fn.Row.Path)
        | :? GlobNode   as gn -> Some (gn.SourceName, Some gn.FileNode.Row.Path)
        | _                   -> None

    let restoreSelection (key: (string * string option) option) =
        match key with
        | None -> ()
        | Some (sName, None) ->
            treeView.Objects
            |> Seq.tryFind (fun n -> match n with :? SourceNode as sn -> sn.Source.Name = sName | _ -> false)
            |> Option.iter treeView.GoTo
        | Some (sName, Some path) ->
            let srcNodeOpt =
                treeView.Objects
                |> Seq.tryPick (fun n ->
                    match n with
                    | :? SourceNode as sn when sn.Source.Name = sName -> Some sn
                    | _ -> None)
            match srcNodeOpt with
            | None -> ()
            | Some srcNode ->
                let child =
                    if treeView.IsExpanded srcNode then
                        treeView.GetChildren srcNode
                        |> Seq.tryFind (fun n ->
                            match n with
                            | :? FileNode as fn -> fn.Row.Path = path
                            | :? GlobNode as gn -> gn.FileNode.Row.Path = path
                            | _ -> false)
                    else None
                child
                |> Option.defaultValue (srcNode :> SourceTreeNode)
                |> treeView.GoTo

    let navLabel       = new Label()
    let searchRow      = new View()
    let searchPrompt   = new Label()
    let searchField    = new TextField()
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
                    |> Seq.filter (fun k -> not (k.Contains('*') || k.Contains('?')))
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

        navLabel.Text <- "Files"
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
            match node with
            | :? FileNode as fn ->
                let check = if fn.IsTracked then "[✓]" else "[ ]"
                $"{prefix}{check} {fn.Row.Path}"
            | :? GlobNode as gn ->
                let check = if gn.IsTracked then "[✓]" else "[ ]"
                $"{prefix}{check} {gn.FileNode.Row.Path}"
            | _ -> $"{prefix}{node.Label}"
        populateSources ""
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
            | :? GlobNode as gn ->
                showFileDetail gn.FileNode
                updatePreview gn.FileNode
            | :? SourceNode as sn ->
                showSourceDetail sn.Source
                previewLabel.Text <- "Preview"
                previewText.Text  <- ""
            | _ -> ())

        searchField.ValueChanged.Add(fun e ->
            let text = if isNull (box e.NewValue) then "" else e.NewValue
            currentFilter <- text
            populateSources text)

        searchField.KeyDown.Add(fun key ->
            if key.KeyCode = KeyCode.Esc then
                key.Handled <- true
                searchField.Text <- ""
                currentFilter <- ""
                populateSources ""
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
                elif key.KeyCode = KeyCode.A && not key.IsShift then
                    match treeView.SelectedObject with
                    | :? FileNode as fn ->
                        key.Handled <- true
                        actionEvent.Trigger(AddFile(fn.SourceName, fn.Row.Path))
                    | :? GlobNode as gn ->
                        key.Handled <- true
                        actionEvent.Trigger(AddFile(gn.SourceName, gn.FileNode.Row.Path))
                    | _ -> ()
                elif key.KeyCode = KeyCode.X && not key.IsShift then
                    match treeView.SelectedObject with
                    | :? FileNode as fn when fn.IsTracked ->
                        key.Handled <- true
                        let localPath =
                            match fn.LockEntry with
                            | Some e -> e.LocalPath
                            | None   -> fn.Row.Path
                        actionEvent.Trigger(RemoveEntry localPath)
                    | :? GlobNode as gn when gn.IsTracked ->
                        key.Handled <- true
                        actionEvent.Trigger(RemoveGlob(gn.SourceName, gn.FileNode.Row.Path))
                    | _ -> ()
                elif key.KeyCode = KeyCode.R && not key.IsShift then
                    match treeView.SelectedObject with
                    | :? FileNode | :? SourceNode | :? GlobNode ->
                        key.Handled <- true
                        actionEvent.Trigger(RefreshSource)
                    | _ -> ())

    member _.ActionRequested = actionEvent.Publish

    member _.FocusContent() = treeView.SetFocus() |> ignore

    member _.RefreshLockBadges(entries: LockEntry list) =
        let saved = saveSelectionKey ()
        lockEntries <- entries
        fileCache.Clear()
        treeView.RebuildTree()
        restoreSelection saved

    member _.Reload(sources: SourceList.SourceRow list, entries: LockEntry list) =
        allSources <- sources
        lockEntries <- entries
        fileCache.Clear()
        populateSources currentFilter

    member _.RefreshAll() =
        fileCache.Clear()
        treeView.RebuildTree()
