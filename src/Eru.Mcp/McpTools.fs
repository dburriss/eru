namespace Eru.Mcp

open System
open System.ComponentModel
open System.IO
open System.Runtime.InteropServices
open System.Text.Json
open System.Threading
open Eru
open Eru.Adapters
open Eru.Search
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

[<McpServerToolType>]
type KnowledgeTools(deps: Deps, syncService: KnowledgeSyncService, searcher: RankedSearch.Searcher) =

    let defaultLimit = RankedSearch.defaultLimit

    let label (c: CandidateFile) =
        let tagsStr = if c.Tags = [] then "" else " [tags: " + String.concat "," c.Tags + "]"
        let descStr = c.Description |> Option.map (fun d -> " — " + d) |> Option.defaultValue ""
        match c.Source with
        | Lock ->
            let sourcePart = c.SourceName |> Option.map (fun s -> $" (from {s})") |> Option.defaultValue ""
            $"[lock] {c.RelPath}{sourcePart}{descStr}"
        | Local      -> $"[local] {c.RelPath}{tagsStr}{descStr}"
        | Cache      -> $"[collection] {c.RelPath}{tagsStr}{descStr}"

    [<McpServerTool(Name = "search_knowledge", UseStructuredContent = true, OutputSchemaType = typeof<SearchResult>)>]
    [<Description("Ranked search across cached collection files, locally pulled artifacts (.eru/eru.lock), and local knowledge/ directories. Returns the best matching files first with file paths, metadata, a relevance score and up to 3 content excerpts each. Output is bounded by `limit`; `truncated` says whether more files matched.")>]
    member _.Search(
        [<Description("Search terms, space-separated. Files are ranked by relevance (rarer terms count more, matches in the path, headings and front matter count extra) and word forms are matched, so 'mock' finds 'mocked'. Leave empty to list known artifacts.")>] query: string,
        [<Description("Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering.")>] tags: string,
        [<Optional; DefaultParameterValue(15)>] [<Description("Maximum number of files to return (default 15).")>] limit: int,
        [<Optional; DefaultParameterValue(null: string)>] [<Description("Comma-separated source names to search (any of them), as listed by `eru source list`; use 'local' for local knowledge/ files. Leave empty to search every source. Unknown names return an error listing the valid ones.")>] sources: string,
        [<Optional; DefaultParameterValue(null: string)>] [<Description("Ranking mode: 'lex' (keywords, default), 'sem' (meaning) or 'hybrid' (both). Semantic modes fall back to 'lex' with a warning when the embedding model is not installed.")>] mode: string) : CallToolResult =
        syncService.NoteActivity()

        let eff = syncService.CurrentEff
        let cwd = deps.GetCwd()
        let requestedMode =
            if String.IsNullOrWhiteSpace mode then Ok None
            else
                match SearchMode.tryParse mode with
                | Some m -> Ok (Some m)
                | None   -> Error $"mode must be one of lex, sem, hybrid (got '{mode}')"

        let request (m: SearchMode option) : RankedSearch.Request =
            { Query = query
              Tags  = RankedSearch.parseTags tags
              Sources = RankedSearch.parseSources sources
              Mode  = m
              Limit = if limit < 1 then defaultLimit else limit }

        let outcome =
            requestedMode
            |> Result.bind (fun m -> RankedSearch.execute searcher deps eff cwd (request m) CancellationToken.None)

        match outcome with
        | Error e ->
            CallToolResult(IsError = System.Nullable true, Content = [| TextContentBlock(Text = $"Error: {e}") |])
        | Ok result ->
            let results =
                result.Hits |> List.map (fun h ->
                    if h.Excerpts.IsEmpty then label h.Candidate
                    else
                        let excerptBlock = h.Excerpts |> String.concat "\n  > "
                        $"{label h.Candidate}\n  > {excerptBlock}")

            let notes =
                [ if result.Truncated then
                      yield $"(results truncated at {max 1 (if limit < 1 then defaultLimit else limit)}; narrow the query or raise limit)"
                  for w in result.Warnings do yield $"(warning: {w})" ]

            let textOutput =
                if results.IsEmpty then "No matching artifacts found."
                else String.concat "\n\n" results
            let textOutput = if notes.IsEmpty then textOutput else textOutput + "\n\n" + String.concat "\n" notes

            let structured =
                { Hits =
                    result.Hits
                    |> List.map (fun h ->
                        { Path        = h.Candidate.RelPath
                          Source      = match h.Candidate.Source with Cache -> "cache" | Lock -> "lock" | Local -> "local"
                          SourceName  = h.Candidate.SourceName
                          Tags        = h.Candidate.Tags
                          Description = h.Candidate.Description
                          Excerpts    = h.Excerpts
                          Score       = h.Score })
                    |> List.toArray
                  Truncated = result.Truncated
                  Warnings  = result.Warnings |> List.toArray }

            CallToolResult(
                Content           = [| TextContentBlock(Text = textOutput) |],
                StructuredContent = System.Nullable(JsonSerializer.SerializeToElement(structured, Serialization.options))
            )

    [<McpServerTool(Name = "read_artifact")>]
    [<Description("Read the full content of a knowledge artifact by local path, lock-file path, cached collection path, 'sourceName:remotePath' reference, or path short hash.")>]
    member _.Read(
        [<Description("Artifact path: a local file path (relative or absolute), 'sourceName:remotePath', or a path from search_knowledge results.")>] path: string) : string =
        syncService.NoteActivity()

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

        // 3. Index lookup by "sourceName/remotePath" or a bare remotePath key
        let indexId =
            eff.Sources |> List.tryPick (fun src ->
                match deps.ReadSourceIndex src.Name with
                | Ok (Some idx) ->
                    let prefix = $"{src.Name}/"
                    let remotePath =
                        if path.StartsWith prefix then Some (path.[prefix.Length..])
                        elif Map.containsKey path idx.Entries then Some path
                        else None
                    remotePath
                    |> Option.filter (fun rp -> Map.containsKey rp idx.Entries)
                    |> Option.map (fun rp -> { Source = src.Name; RemotePath = rp })
                | _ -> None)

        // 4. "sourceName:remotePath" or a path short hash
        let target =
            match indexId with
            | Some id -> Ok id
            | None ->
                if (EntryId.tryParse path).IsNone && not (Patterns.isShortHash (path.ToLowerInvariant())) then
                    Error $"artifact not found: {path}"
                else Read.resolveTarget deps eff path

        match target |> Result.bind (Read.readContent deps eff) with
        | Ok content -> content
        | Error e    -> $"Error: {e}"

    [<McpServerTool(Name = "refresh_knowledge")>]
    [<Description("Trigger a background refresh of the knowledge cache. Returns immediately; sync runs in the background and errors are written to the eru log file.")>]
    member _.Refresh() : string =
        syncService.NoteActivity()
        if syncService.TriggerBackgroundSync(true) then
            "Knowledge refresh started in the background."
        else
            "A knowledge refresh is already in progress (in this or another eru process)."
