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

    let cacheRoot = Paths.collectionCachePath ()

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

    let readFrontmatter absPath =
        try Frontmatter.parse (File.ReadAllText absPath)
        with _ -> Frontmatter.empty

    [<McpServerTool(Name = "search_knowledge", UseStructuredContent = true, OutputSchemaType = typeof<SearchHit[]>)>]
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

        // 1. Cached collection files
        if Directory.Exists(cacheRoot) then
            for file in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories) do
                let relPath    = Path.GetRelativePath(cacheRoot, file).Replace(Path.DirectorySeparatorChar, '/')
                let parts      = relPath.Split('/', 2)
                let sourceName = if parts.Length > 0 then parts.[0] else ""
                let remotePath = if parts.Length > 1 then parts.[1] else relPath
                let meta       = eff.Collections |> List.tryFind (fun c -> c.Source = sourceName && c.RemotePath = remotePath)
                let configTags = meta |> Option.map (fun m -> m.Tags) |> Option.defaultValue []
                let configDesc = meta |> Option.bind (fun m -> m.Description)
                let fm         = readFrontmatter file
                let fileTags   = (configTags @ fm.Tags) |> List.distinct
                let desc       = configDesc |> Option.orElse fm.Description
                if hasTags requiredTags fileTags && isPathAllowed remotePath then
                    candidates.Add({
                        AbsPath     = file
                        RelPath     = relPath
                        Source      = Cache
                        SourceName  = Some sourceName
                        Tags        = fileTags
                        Description = desc
                    })

        // 2. Lock file entries
        let lockPath = Paths.lockFilePath (deps.GetCwd()) (Some eff.StateFile)
        match deps.ReadLockEntries lockPath with
        | Ok entries ->
            for entry in entries do
                if isPathAllowed entry.RemotePath && File.Exists(entry.LocalPath) then
                    let fm = readFrontmatter entry.LocalPath
                    if hasTags requiredTags fm.Tags then
                        candidates.Add({
                            AbsPath     = entry.LocalPath
                            RelPath     = entry.LocalPath
                            Source      = Lock
                            SourceName  = Some entry.SourceName
                            Tags        = fm.Tags
                            Description = fm.Description
                        })
        | Error _ -> ()

        // 3. Local knowledge directories
        let cwd = deps.GetCwd()
        for knowledgeDir in [ "knowledge"; "KNOWLEDGE" ] do
            let dirPath = Path.Combine(cwd, knowledgeDir)
            if Directory.Exists(dirPath) then
                for file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories) do
                    let relPath = Path.GetRelativePath(cwd, file)
                    if isPathAllowed relPath then
                        let fm = readFrontmatter file
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

        let results =
            hits |> List.map (fun (f, excerpts) ->
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
            hits |> List.map (fun (f, excerpts) ->
                {   Path        = f.RelPath
                    Source      = match f.Source with Cache -> "cache" | Lock -> "lock" | Local -> "local"
                    SourceName  = f.SourceName
                    Tags        = f.Tags
                    Description = f.Description
                    Excerpts    = excerpts })
            |> List.toArray

        CallToolResult(
            Content           = [| TextContentBlock(Text = textOutput) |],
            StructuredContent = System.Nullable(JsonSerializer.SerializeToElement(structuredHits))
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
        let lockPath = Paths.lockFilePath cwd (Some eff.StateFile)
        let lockMatch =
            match deps.ReadLockEntries lockPath with
            | Ok entries -> entries |> List.tryFind (fun e -> e.LocalPath = path)
            | Error _    -> None
        match lockMatch with
        | Some entry when File.Exists(entry.LocalPath) -> File.ReadAllText(entry.LocalPath)
        | _ ->

        // 3. Cache hit at collectionCacheRoot/<path>
        let cachePath = Path.Combine(cacheRoot, path.Replace('/', Path.DirectorySeparatorChar))
        if File.Exists(cachePath) then File.ReadAllText(cachePath)
        else

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
                    match deps.FetchRemoteContent url branch remotePath with
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
