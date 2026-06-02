module Eru.Tui.Browse.SourcesPane

open System.Collections.Generic
open Terminal.Gui.App
open Terminal.Gui.ViewBase
open Terminal.Gui.Views
open Terminal.Gui.Drivers
open Eru
open BrowseState

type SourcesPane(deps: Deps, initialSources: SourceList.SourceRow list, initialLockEntries: LockEntry list) as this =
    inherit FrameView()

    let actionEvent = Event<BrowseAction>()
    let fileCache = Dictionary<string, SourceFiles.SourceFileRow list>()
    let mutable currentFilter = ""
    let mutable lockEntries: LockEntry list = initialLockEntries
    let mutable allSources: SourceList.SourceRow list = initialSources

    let makeFileNode (sourceName: string) (row: SourceFiles.SourceFileRow) =
        let entry =
            lockEntries |> List.tryFind (fun e ->
                e.SourceName = sourceName && e.RemotePath = row.Path)
        FileNode(row, sourceName, entry)

    let loadSourceFiles (sourceName: string) : SourceTreeNode seq =
        if not (fileCache.ContainsKey(sourceName)) then
            match SourceFiles.execute deps (Some sourceName) with
            | Ok results ->
                let rows =
                    results
                    |> List.tryFind (fun (n, _) -> n = sourceName)
                    |> Option.map snd
                    |> Option.defaultValue []
                fileCache.[sourceName] <- rows
            | Error _ -> fileCache.[sourceName] <- []
        fileCache.[sourceName]
        |> Seq.map (makeFileNode sourceName >> (fun n -> n :> SourceTreeNode))

    let treeView =
        let builder =
            DelegateTreeBuilder<SourceTreeNode>(
                (fun node ->
                    match node with
                    | :? SourceNode as sn -> loadSourceFiles sn.Source.Name
                    | _ -> Seq.empty),
                (fun node -> node :? SourceNode))
        new TreeView<SourceTreeNode>(builder)

    let detailView = new FrameView()
    let detailLabel = new Label()

    let formatSourceDetail (src: SourceList.SourceRow) =
        let url  = src.Url    |> Option.defaultValue "(none)"
        let br   = src.Branch |> Option.defaultValue "HEAD"
        let bp   = src.BasePath |> Option.defaultValue ""
        let tags = src.Tags |> String.concat ", "
        $"Source:  {src.Name}\nURL:     {url}\nBranch:  {br}\nBasePath:{bp}\nScope:   {src.Scope}\nTags:    {tags}"

    let formatFileDetail (fn: FileNode) =
        let tags    = fn.Row.Tags |> String.concat ", "
        let desc    = fn.Row.Description |> Option.defaultValue ""
        let status  = if fn.IsInstalled then "✓ installed" else "not installed"
        let localPt =
            match fn.LockEntry with
            | Some e -> e.LocalPath
            | None -> ""
        $"File:   {fn.Row.Path}\nSource: {fn.SourceName}\nHash:   {fn.Row.Hash}\nTags:   {tags}\nDesc:   {desc}\nStatus: {status}\nLocal:  {localPt}"

    let populateSources (filter: string) =
        treeView.ClearObjects()
        let filtered =
            if filter = "" then allSources
            else allSources |> List.filter (fun s ->
                s.Name.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
        for src in filtered do
            treeView.AddObject(SourceNode src)

    do
        this.Title <- "Sources"
        treeView.X <- Pos.Absolute 0
        treeView.Y <- Pos.Absolute 0
        treeView.Width <- Dim.Percent(35, DimPercentMode.ContentSize)
        treeView.Height <- Dim.Fill()
        treeView.AspectGetter <- fun node ->
            match node with
            | :? FileNode as fn ->
                if fn.IsInstalled then $"{fn.Row.Path} [✓]" else fn.Row.Path
            | _ -> node.Label
        populateSources ""
        this.Add(treeView) |> ignore

        detailView.Title <- "Details"
        detailView.X <- Pos.Right treeView
        detailView.Y <- Pos.Absolute 0
        detailView.Width <- Dim.Fill()
        detailView.Height <- Dim.Fill()

        detailLabel.X <- Pos.Absolute 0
        detailLabel.Y <- Pos.Absolute 0
        detailLabel.Width <- Dim.Fill()
        detailLabel.Height <- Dim.Fill()
        detailLabel.Text <- "Select a source or file to see details."
        detailView.Add(detailLabel) |> ignore
        this.Add(detailView) |> ignore

        treeView.SelectionChanged.Add(fun e ->
            match e.NewValue with
            | :? FileNode as fn -> detailLabel.Text <- formatFileDetail fn
            | :? SourceNode as sn ->
                detailLabel.Text <- formatSourceDetail sn.Source
            | _ -> ())

    member _.ActionRequested = actionEvent.Publish

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

    override _.OnKeyDown(key: Terminal.Gui.Input.Key) =
        if key.KeyCode = KeyCode.A && not key.IsShift then
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
            | None -> ()
        base.OnKeyDown(key)
