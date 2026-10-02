namespace Eru

open System
open System.IO

module LinkGraph =

    type NodeId =
        | InternalNode of EntryId
        | ExternalNode of Url: string

    let nodeKey (n: NodeId) : string =
        match n with
        | InternalNode id  -> EntryId.toString id
        | ExternalNode url -> url

    type Edge = { From: NodeId; To: NodeId; Description: string option }

    type Command = { SourceFilter: string option }

    type BuildResult = { Nodes: NodeId list; Edges: Edge list }

    let private isExternal (target: string) =
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

    // Resolves "." and ".." segments against a combined path; returns None if the
    // path would escape above the root (more ".." segments than directories).
    let private normalizePath (path: string) : string option =
        let stack = ResizeArray<string>()
        let mutable escaped = false
        for part in path.Split('/') do
            if not escaped then
                match part with
                | "" | "." -> ()
                | ".." ->
                    if stack.Count > 0 then stack.RemoveAt(stack.Count - 1)
                    else escaped <- true
                | p -> stack.Add p
        if escaped || stack.Count = 0 then None
        else Some (String.Join("/", stack))

    let resolveLink (source: string) (currentPath: string) (target: string) : NodeId option =
        let target = target.Trim()
        if target = "" then None
        elif isExternal target then Some (ExternalNode target)
        elif target.StartsWith("#") || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) then None
        else
            let anchorIdx = target.IndexOf('#')
            let targetPath = if anchorIdx >= 0 then target.Substring(0, anchorIdx) else target
            if targetPath = "" then None
            else
                let dir = PathUtil.dirName currentPath
                let combined =
                    if targetPath.StartsWith("/") then targetPath.TrimStart('/')
                    elif dir = "" then targetPath
                    else $"{dir}/{targetPath}"
                normalizePath combined
                |> Option.map (fun normalized -> InternalNode { Source = source; RemotePath = normalized })

    type TitleIndex = { KnownPaths: Set<string>; ByKey: Map<string, string> }

    let private slug (s: string) = s.Trim().ToLowerInvariant()

    let private emptyTitleIndex = { KnownPaths = Set.empty; ByKey = Map.empty }

    let private buildTitleIndex (entries: (string * IndexEntry) list) : TitleIndex =
        let knownPaths = entries |> List.map fst |> Set.ofList
        let byStem = entries |> List.map (fun (path, _) -> slug (Path.GetFileNameWithoutExtension path), path)
        let byTitle = entries |> List.choose (fun (path, e) -> e.Title |> Option.map (fun t -> slug t, path))
        { KnownPaths = knownPaths; ByKey = Map.ofList (byStem @ byTitle) }

    let resolveWikilink (source: string) (currentPath: string) (titleIndex: TitleIndex) (target: string) : NodeId option =
        match resolveLink source currentPath target with
        | Some (InternalNode id) as pathResolved when titleIndex.KnownPaths.Contains id.RemotePath ->
            pathResolved
        | pathResolved ->
            match titleIndex.ByKey.TryFind (slug target) with
            | Some remotePath -> Some (InternalNode { Source = source; RemotePath = remotePath })
            | None -> pathResolved

    let execute (deps: Deps) (cmd: Command) : Result<BuildResult, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        let sources =
            eff.Sources
            |> List.filter (fun s -> cmd.SourceFilter |> Option.forall (fun f -> f = s.Name))

        let entries =
            sources
            |> List.collect (fun src ->
                match deps.ReadSourceIndex src.Name with
                | Ok (Some idx) -> idx.Entries |> Map.toList |> List.map (fun (remotePath, entry) -> src.Name, remotePath, entry)
                | _ -> [])

        let titleIndexBySource =
            entries
            |> List.groupBy (fun (s, _, _) -> s)
            |> List.map (fun (s, es) -> s, buildTitleIndex (es |> List.map (fun (_, p, e) -> p, e)))
            |> Map.ofList

        let nodes = ResizeArray<NodeId>()
        let seen  = System.Collections.Generic.HashSet<NodeId>()
        let addNode (n: NodeId) = if seen.Add n then nodes.Add n

        let edges = ResizeArray<Edge>()

        for (sourceName, remotePath, entry) in entries do
            let selfNode = InternalNode { Source = sourceName; RemotePath = remotePath }
            addNode selfNode
            match entry.CacheRelPath with
            | None -> ()
            | Some cacheRelPath ->
                match deps.ReadCachedSourceContent sourceName cacheRelPath with
                | Ok (Some content) ->
                    deps.ExtractLinks content
                    |> List.choose (fun link ->
                        let resolved =
                            match link.Kind with
                            | MarkdownLink -> resolveLink sourceName remotePath link.Target
                            | Wikilink ->
                                let titleIndex = titleIndexBySource |> Map.tryFind sourceName |> Option.defaultValue emptyTitleIndex
                                resolveWikilink sourceName remotePath titleIndex link.Target
                        resolved |> Option.map (fun target -> target, link.Description))
                    |> List.iter (fun (target, description) ->
                        addNode target
                        edges.Add { From = selfNode; To = target; Description = description })
                | _ -> ()

        Ok { Nodes = nodes |> List.ofSeq; Edges = edges |> List.ofSeq }

    let edgesFor (result: BuildResult) (node: NodeId) : {| Outgoing: NodeId list; Incoming: NodeId list |} =
        {| Outgoing = result.Edges |> List.filter (fun e -> e.From = node) |> List.map (fun e -> e.To)
           Incoming = result.Edges |> List.filter (fun e -> e.To   = node) |> List.map (fun e -> e.From) |}
