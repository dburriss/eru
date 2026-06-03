namespace Eru.Mcp

open System
open System.ComponentModel
open System.IO
open System.Text.Json
open Eru
open Eru.Adapters
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

[<McpServerToolType>]
type KnowledgeTools(deps: Deps, syncService: KnowledgeSyncService) =

    let backend : SearchFn =
        match Environment.GetEnvironmentVariable "ERU_SEARCH_BACKEND" with
        | "indexed" -> IndexedSearch.search
        | "ck"      -> CkSearch.search
        | _         -> SimpleScan.search

    let parseTerms (query: string) =
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.map (fun t -> t.ToLowerInvariant())

    let parseTags (tags: string) =
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.map (fun t -> t.Trim().ToLowerInvariant())

    let hasTags (required: string list) (itemTags: string list) =
        required = [] ||
        required |> List.forall (fun t ->
            itemTags |> List.exists (fun it -> it.ToLowerInvariant() = t))

    let resolveAbsPath (sourceName: string) (entry: IndexEntry) (localEntry: LockEntry option) : string option =
        // Prefer local file if available
        let localPath = entry.LocalPath |> Option.orElse (localEntry |> Option.map (fun e -> e.LocalPath))
        match localPath with
        | Some p when File.Exists p -> Some p
        | _ ->
            match entry.CacheRelPath with
            | Some rel ->
                let abs = Path.Combine(Paths.sourceCacheDir sourceName, rel)
                if File.Exists abs then Some abs else None
            | None -> None

    [<McpServerTool(Name = "search_knowledge", UseStructuredContent = true, OutputSchemaType = typeof<SearchResult>)>]
    [<Description("Full-text search across cached collection files, locally pulled artifacts (.eru/eru.lock), and local knowledge/ directories. Returns matching file paths, metadata, and content excerpts.")>]
    member _.Search(
        [<Description("Search terms (space-separated, OR semantics). Matched against file content and path. Leave empty to list all known artifacts.")>] query: string,
        [<Description("Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering.")>] tags: string) : CallToolResult =

        let eff          = syncService.CurrentEff
        let termList     = parseTerms query
        let requiredTags = parseTags tags

        let isPathAllowed path =
            not (Patterns.isPathBlocked eff.BlockPatterns eff.AllowPatterns path)

        let candidates = System.Collections.Generic.List<CandidateFile>()
        let metadataOnlyCandidates = System.Collections.Generic.List<CandidateFile>()

        // Build lock entry map for LocalPath resolution
        let lockEntryMap =
            match deps.ReadLockEntries eff.StateFile with
            | Ok entries -> entries |> List.map (fun e -> (e.SourceName, e.RemotePath), e) |> Map.ofList
            | Error _    -> Map.empty

        // 1. Index-based candidates from all sources
        for src in eff.Sources do
            match SourceIndexAdapter.readIndex src.Name with
            | Ok (Some idx) ->
                for KeyValue(remotePath, entry) in idx do
                    if isPathAllowed remotePath then
                        let lockEntry = lockEntryMap |> Map.tryFind (src.Name, remotePath)
                        // Collection-side tags (not stored in index)
                        let colTags =
                            eff.Collections
                            |> List.tryFind (fun c -> c.Source = src.Name && c.RemotePath = remotePath)
                            |> Option.map (fun c -> c.Tags)
                            |> Option.defaultValue []
                        let allTags = (entry.Tags @ colTags) |> List.distinct
                        if hasTags requiredTags allTags then
                            match resolveAbsPath src.Name entry lockEntry with
                            | Some absPath ->
                                let relPath =
                                    lockEntry
                                    |> Option.map (fun e -> e.LocalPath)
                                    |> Option.defaultValue ($"{src.Name}/{remotePath}")
                                candidates.Add({
                                    AbsPath     = absPath
                                    RelPath     = relPath
                                    Source      = Cache
                                    SourceName  = Some src.Name
                                    Tags        = allTags
                                    Description = entry.Description
                                })
                            | None ->
                                // Metadata-only: no cached content yet
                                metadataOnlyCandidates.Add({
                                    AbsPath     = ""
                                    RelPath     = $"{src.Name}/{remotePath}"
                                    Source      = Cache
                                    SourceName  = Some src.Name
                                    Tags        = allTags
                                    Description = entry.Description
                                })
            | _ -> ()

        // 2. Lock file entries not covered by any index
        let indexedPaths =
            candidates |> Seq.map (fun c -> c.RelPath) |> Set.ofSeq
        for KeyValue((sn, rp), lockEntry) in lockEntryMap do
            let relPath = lockEntry.LocalPath
            if not (Set.contains relPath indexedPaths) && isPathAllowed rp && File.Exists lockEntry.LocalPath then
                candidates.Add({
                    AbsPath     = lockEntry.LocalPath
                    RelPath     = lockEntry.LocalPath
                    Source      = Lock
                    SourceName  = Some sn
                    Tags        = lockEntry.Tags
                    Description = lockEntry.Description
                })

        // 3. Local knowledge directories
        let cwd = deps.GetCwd()
        let knowledgeDirs =
            [ "knowledge"; "KNOWLEDGE" ]
            |> List.map (fun d -> Path.Combine(cwd, d))
            |> List.filter Directory.Exists
            |> List.distinctBy (fun p -> DirectoryInfo(p).FullName)
        for dirPath in knowledgeDirs do
            for file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories) do
                let relPath = Path.GetRelativePath(cwd, file)
                if isPathAllowed relPath then
                    let fm = Frontmatter.parse (File.ReadAllText file)
                    if hasTags requiredTags fm.Tags then
                        candidates.Add({
                            AbsPath     = file
                            RelPath     = relPath
                            Source      = Local
                            SourceName  = None
                            Tags        = fm.Tags
                            Description = fm.Description
                        })

        let hits = backend termList (candidates |> Seq.toList)

        // Metadata-only hits: term match against path and description (no content)
        let metaHits =
            metadataOnlyCandidates
            |> Seq.filter (fun c ->
                termList = [] ||
                termList |> List.exists (fun t ->
                    c.RelPath.ToLowerInvariant().Contains(t) ||
                    (c.Description |> Option.exists (fun d -> d.ToLowerInvariant().Contains(t)))))
            |> Seq.map (fun c -> (c, []))
            |> Seq.toList

        let allHits = hits @ metaHits

        let results =
            allHits |> List.map (fun (f, excerpts) ->
                let label =
                    match f.Source with
                    | Lock ->
                        let sourcePart = f.SourceName |> Option.map (fun s -> $" (from {s})") |> Option.defaultValue ""
                        let descPart   = f.Description |> Option.map (fun d -> " — " + d) |> Option.defaultValue ""
                        $"[lock] {f.RelPath}{sourcePart}{descPart}"
                    | Local ->
                        let tagsStr = if f.Tags = [] then "" else " [tags: " + String.concat "," f.Tags + "]"
                        let descStr = f.Description |> Option.map (fun d -> " — " + d) |> Option.defaultValue ""
                        $"[local] {f.RelPath}{tagsStr}{descStr}"
                    | Cache ->
                        let tagsStr = if f.Tags = [] then "" else " [tags: " + String.concat "," f.Tags + "]"
                        let descStr = f.Description |> Option.map (fun d -> " — " + d) |> Option.defaultValue ""
                        $"[collection] {f.RelPath}{tagsStr}{descStr}"
                if excerpts.IsEmpty then label
                else
                    let excerptBlock = excerpts |> String.concat "\n  > "
                    $"{label}\n  > {excerptBlock}")

        let textOutput =
            if results.IsEmpty then "No matching artifacts found."
            else String.concat "\n\n" results

        let structuredHits =
            allHits |> List.map (fun (f, excerpts) ->
                {   Path        = f.RelPath
                    Source      = match f.Source with Cache -> "cache" | Lock -> "lock" | Local -> "local"
                    SourceName  = f.SourceName
                    Tags        = f.Tags
                    Description = f.Description
                    Excerpts    = excerpts })
            |> List.toArray

        CallToolResult(
            Content           = [| TextContentBlock(Text = textOutput) |],
            StructuredContent = System.Nullable(JsonSerializer.SerializeToElement({ Hits = structuredHits }))
        )

    [<McpServerTool(Name = "read_artifact")>]
    [<Description("Read the full content of a knowledge artifact by local path, lock-file path, cached collection path, or 'sourceName:remotePath' reference.")>]
    member _.Read(
        [<Description("Artifact path: a local file path (relative or absolute), 'sourceName:remotePath', or a path from search_knowledge results.")>] path: string) : string =

        let eff  = syncService.CurrentEff
        let cwd  = deps.GetCwd()

        // 1. Local file (relative to CWD or absolute)
        let localPath = if Path.IsPathRooted(path) then path else Path.Combine(cwd, path)
        if File.Exists(localPath) then File.ReadAllText(localPath)
        else

        // 2. Lock file LocalPath match
        let lockMatch =
            match deps.ReadLockEntries eff.StateFile with
            | Ok entries -> entries |> List.tryFind (fun e -> e.LocalPath = path)
            | Error _    -> None
        match lockMatch with
        | Some entry when File.Exists(entry.LocalPath) -> File.ReadAllText(entry.LocalPath)
        | _ ->

        // 3. Source index cache lookup (new cache structure)
        let indexCacheHit =
            eff.Sources |> List.tryPick (fun src ->
                match SourceIndexAdapter.readIndex src.Name with
                | Ok (Some idx) ->
                    // Try "sourceName/remotePath" format
                    let prefix = $"{src.Name}/"
                    let remotePath =
                        if path.StartsWith prefix then Some (path.[prefix.Length..])
                        else
                            // Try direct remotePath match
                            if Map.containsKey path idx then Some path else None
                    remotePath |> Option.bind (fun rp ->
                        match Map.tryFind rp idx with
                        | Some entry when entry.CacheRelPath.IsSome ->
                            let absPath = Path.Combine(Paths.sourceCacheDir src.Name, entry.CacheRelPath.Value)
                            if File.Exists absPath then Some (File.ReadAllText absPath)
                            else None
                        | _ -> None)
                | _ -> None)

        match indexCacheHit with
        | Some content -> content
        | None ->

        // 4. sourceName:remotePath — live fetch
        let colonIdx = path.IndexOf(':')
        if colonIdx > 0 then
            let sourceName = path.[..colonIdx - 1]
            let remotePath = path.[colonIdx + 1..]
            match eff.Sources |> List.tryFind (fun s -> s.Name = sourceName) with
            | None     -> $"Error: unknown source '{sourceName}'"
            | Some src ->
                match src.Url with
                | None     -> $"Error: source '{sourceName}' has no URL configured"
                | Some url ->
                    let branch = src.Branch |> Option.defaultValue "HEAD"
                    match deps.FetchRemoteContent url branch [remotePath] with
                    | Ok ((_, content) :: _) ->
                        if Patterns.isBlocked eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries remotePath content then
                            $"Error: '{remotePath}' is blocked by the current block patterns"
                        else content
                    | Ok []   -> $"Error: no content returned for {path}"
                    | Error e -> $"Error fetching {path}: {e}"
        else
            $"Error: artifact not found: {path}"

    [<McpServerTool(Name = "refresh_knowledge")>]
    [<Description("Trigger a background refresh of the knowledge cache. Returns immediately; sync runs in the background and errors are written to the eru log file.")>]
    member _.Refresh() : string =
        if syncService.TriggerBackgroundSync() then
            "Knowledge refresh started in the background."
        else
            "A knowledge refresh is already in progress."
