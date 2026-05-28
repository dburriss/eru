namespace Eru.Mcp

open System
open System.ComponentModel
open System.IO
open Eru
open Eru.Adapters
open ModelContextProtocol.Server

[<McpServerToolType>]
type KnowledgeTools(deps: Deps, eff: EffectiveConfig) =

    let cacheRoot = Paths.collectionCachePath ()

    let parseTerms (query: string) =
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.map (fun t -> t.ToLowerInvariant())

    let parseTags (tags: string) =
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
        |> Array.toList
        |> List.map (fun t -> t.Trim().ToLowerInvariant())

    let matchesTerms (termList: string list) (text: string) =
        let lower = text.ToLowerInvariant()
        termList = [] || termList |> List.exists lower.Contains

    let firstMatchingLine (termList: string list) (content: string) =
        if termList = [] then
            content.Split('\n') |> Array.tryHead |> Option.defaultValue ""
        else
            content.Split('\n')
            |> Array.tryFind (fun line ->
                termList |> List.exists (fun t -> line.ToLowerInvariant().Contains(t)))
            |> Option.defaultValue ""

    let hasTags (required: string list) (itemTags: string list) =
        required = [] ||
        required |> List.forall (fun t ->
            itemTags |> List.exists (fun it -> it.ToLowerInvariant() = t))

    [<McpServerTool(Name = "search_knowledge")>]
    [<Description("Full-text search across cached collection files, locally pulled artifacts (.eru/eru.lock), and local knowledge/ directories. Returns matching file paths, metadata, and a content excerpt.")>]
    member _.Search(
        [<Description("Search terms (space-separated, OR semantics). Matched against file content, path, and description. Leave empty to list all known artifacts.")>] query: string,
        [<Description("Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering.")>] tags: string) : string =

        let termList     = parseTerms query
        let requiredTags = parseTags tags
        let results      = System.Collections.Generic.List<string>()

        let isPathAllowed path =
            not (Patterns.isPathBlocked eff.BlockPatterns eff.AllowPatterns path)

        // 1. Cached collection files
        if Directory.Exists(cacheRoot) then
            for file in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories) do
                let relPath    = Path.GetRelativePath(cacheRoot, file).Replace(Path.DirectorySeparatorChar, '/')
                let parts      = relPath.Split('/', 2)
                let sourceName = if parts.Length > 0 then parts.[0] else ""
                let remotePath = if parts.Length > 1 then parts.[1] else relPath
                let meta       = eff.Collections |> List.tryFind (fun c -> c.Source = sourceName && c.RemotePath = remotePath)
                let fileTags   = meta |> Option.map (fun m -> m.Tags) |> Option.defaultValue []
                let desc       = meta |> Option.bind (fun m -> m.Description) |> Option.defaultValue ""
                if hasTags requiredTags fileTags && isPathAllowed remotePath then
                    try
                        let content = File.ReadAllText(file)
                        if matchesTerms termList (content + " " + relPath + " " + desc) then
                            let excerpt  = firstMatchingLine termList content
                            let tagsStr  = if fileTags = [] then "" else " [tags: " + String.concat "," fileTags + "]"
                            let descStr  = if desc = "" then "" else " — " + desc
                            results.Add($"[collection] {relPath}{tagsStr}{descStr}\n  > {excerpt.Trim()}")
                    with _ -> ()

        // 2. Lock file entries
        let lockPath = Paths.lockFilePath (deps.GetCwd()) (Some eff.StateFile)
        match deps.ReadLockEntries lockPath with
        | Ok entries ->
            for entry in entries do
                if hasTags requiredTags [] && isPathAllowed entry.RemotePath && File.Exists(entry.LocalPath) then
                    try
                        let content = File.ReadAllText(entry.LocalPath)
                        if matchesTerms termList (content + " " + entry.LocalPath) then
                            let excerpt = firstMatchingLine termList content
                            results.Add($"[lock] {entry.LocalPath} (from {entry.SourceName}:{entry.RemotePath})\n  > {excerpt.Trim()}")
                    with _ -> ()
        | Error _ -> ()

        // 3. Local knowledge directories
        let cwd = deps.GetCwd()
        for knowledgeDir in [ "knowledge"; "KNOWLEDGE" ] do
            let dirPath = Path.Combine(cwd, knowledgeDir)
            if Directory.Exists(dirPath) then
                for file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories) do
                    let relPath = Path.GetRelativePath(cwd, file)
                    if hasTags requiredTags [] && isPathAllowed relPath then
                        try
                            let content  = File.ReadAllText(file)
                            if matchesTerms termList (content + " " + relPath) then
                                let excerpt = firstMatchingLine termList content
                                results.Add($"[local] {relPath}\n  > {excerpt.Trim()}")
                        with _ -> ()

        if results.Count = 0 then "No matching artifacts found."
        else String.concat "\n\n" results

    [<McpServerTool(Name = "read_artifact")>]
    [<Description("Read the full content of a knowledge artifact by local path, lock-file path, cached collection path, or 'sourceName:remotePath' reference.")>]
    member _.Read(
        [<Description("Artifact path: a local file path (relative or absolute), 'sourceName:remotePath', or a path from search_knowledge results.")>] path: string) : string =

        let cwd = deps.GetCwd()

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
