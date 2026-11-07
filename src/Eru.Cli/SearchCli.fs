module Eru.Cli.SearchCli

open System
open System.IO
open System.Text.Json
open System.Threading
open Argu
open Spectre.Console
open Eru
open Eru.Adapters
open Eru.Search
open Eru.Cli.OutputFormat

type Cmd = { Query: LocalSearch.Query; Format: OutputFormat; Limit: int option; Mode: string option }

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args ->
            Some {
                Query = {
                    LocalSearch.Query.Terms = args.GetResult(SearchArgs.Terms, defaultValue = [])
                    LocalSearch.Query.Tags  = args.GetResults SearchArgs.Tag
                    LocalSearch.Query.Sources = args.GetResults SearchArgs.Source
                }
                Format = parseFormat (args.TryGetResult SearchArgs.Output)
                Limit  = args.TryGetResult SearchArgs.Limit
                Mode   = args.TryGetResult SearchArgs.Mode
            }
        | _ -> None)

// ── listing (no terms): artifacts eru knows about, matched on tags ────────────

let private renderText (results: LocalSearch.SearchResult list) =
    if results.IsEmpty then
        printfn "No results found."
    else
        for r in results do
            let hash      = Patterns.pathShortHash r.RemotePath
            let localPart = r.LocalPath |> Option.map (fun lp -> $"  [local: {lp}]") |> Option.defaultValue ""
            let tagPart =
                if r.Tags.IsEmpty then ""
                else
                    let tags = r.Tags |> String.concat ", "
                    $"  [tags: {tags}]"
            printfn "%s:%s  [hash: %s]%s%s" r.SourceName r.RemotePath hash tagPart localPart
            r.Description |> Option.iter (fun d -> printfn "  %s" d)

let private renderJson (results: LocalSearch.SearchResult list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(results, opts))

let private renderTable (results: LocalSearch.SearchResult list) =
    if results.IsEmpty then
        printfn "No results found."
    else
        let t = makeTable ["Hash"; "Source"; "Path"; "Tags"; "Local Path"; "Description"]
        for r in results do
            let hash  = Patterns.pathShortHash r.RemotePath
            let tags  = r.Tags |> String.concat ", "
            let local = r.LocalPath   |> Option.defaultValue ""
            let desc  = r.Description |> Option.defaultValue ""
            t.AddRowEscaped(hash, r.SourceName, r.RemotePath, tags, local, desc) |> ignore
        AnsiConsole.Write(t)

// ── ranked search (terms): files ranked by content ────────────────────────────

// What the hit is called: `source:remotePath` for source files, the repo-relative path otherwise.
let private displayPath (c: CandidateFile) =
    match c.SourceName, c.RemotePath with
    | Some sn, Some rp -> $"{sn}:{rp}"
    | _ -> c.RelPath

// The path of a copy on disk the user can open: pulled or repo files, not an unpulled cache entry.
let private localPath (c: CandidateFile) : string option =
    match c.Source, c.SourceName, c.RemotePath with
    | Cache, Some sn, Some rp when c.RelPath = $"{sn}/{rp}" -> None
    | _ -> if c.AbsPath = "" then None else Some c.RelPath

let private hashOf (c: CandidateFile) = Patterns.pathShortHash (c.RemotePath |> Option.defaultValue c.RelPath)

let private renderRankedText (hits: RankedSearch.RankedHit list) =
    if hits.IsEmpty then
        printfn "No results found."
    else
        for h in hits do
            let c = h.Candidate
            let score = if h.Score > 0.0 then $"{h.Score:F4}  " else ""
            let tags = String.concat ", " c.Tags
            let tagPart = if c.Tags.IsEmpty then "" else $"  [tags: {tags}]"
            let localPart = localPath c |> Option.map (fun lp -> $"  [local: {lp}]") |> Option.defaultValue ""
            printfn "%s%s  [hash: %s]%s%s" score (displayPath c) (hashOf c) tagPart localPart
            c.Description |> Option.iter (fun d -> printfn "  %s" d)
            for e in h.Excerpts do printfn "  > %s" e

let private renderRankedJson (hits: RankedSearch.RankedHit list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let rows =
        hits |> List.map (fun h ->
            let c = h.Candidate
            {| sourceName  = c.SourceName
               remotePath  = c.RemotePath
               path        = displayPath c
               hash        = hashOf c
               tags        = c.Tags
               description = c.Description
               localPath   = localPath c
               score       = h.Score
               excerpts    = h.Excerpts |})
    printfn "%s" (JsonSerializer.Serialize(rows, opts))

let private renderRankedTable (hits: RankedSearch.RankedHit list) =
    if hits.IsEmpty then
        printfn "No results found."
    else
        let t = makeTable ["Score"; "Hash"; "Path"; "Tags"; "Local Path"; "Description"]
        for h in hits do
            let c = h.Candidate
            let score = if h.Score > 0.0 then $"{h.Score:F4}" else ""
            t.AddRowEscaped(score, hashOf c, displayPath c, String.concat ", " c.Tags, localPath c |> Option.defaultValue "", c.Description |> Option.defaultValue "") |> ignore
        AnsiConsole.Write(t)

let private effectiveConfig (deps: Eru.Deps) : Result<EffectiveConfig, string> =
    match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
    | Error e, _ | _, Error e -> Error e
    | Ok g, Ok l -> DepsConfig.merge deps g l |> Result.map (Config.withManifests deps.ReadCachedManifest)

let private runRanked (deps: Eru.Deps) (cmd: Cmd) (terms: string list) : int =
    use cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun e -> e.Cancel <- true; cts.Cancel())

    let requestedMode =
        match cmd.Mode with
        | None -> Ok None
        | Some m ->
            match SearchMode.tryParse m with
            | Some mode -> Ok (Some mode)
            | None      -> Error $"--mode must be one of lex, sem, hybrid (got '{m}')"

    match effectiveConfig deps, requestedMode with
    | Error e, _ | _, Error e -> renderError e; 1
    | Ok eff, Ok mode ->
        use searcher = new RankedSearch.Searcher(Paths.ferretCacheDir (), Action<string>(eprintfn "%s"))
        let request : RankedSearch.Request =
            { Query = String.concat " " terms
              Tags  = cmd.Query.Tags |> List.map (fun t -> t.ToLowerInvariant())
              Sources = cmd.Query.Sources |> List.map (fun s -> s.ToLowerInvariant())
              Mode  = mode
              Limit = cmd.Limit |> Option.defaultValue RankedSearch.defaultLimit }

        match RankedSearch.execute searcher deps eff (deps.GetCwd()) request cts.Token with
        | Error e -> renderError e; 1
        | Ok result ->
            match cmd.Format with
            | Text  -> renderRankedText result.Hits
            | Json  -> renderRankedJson result.Hits
            | Table -> renderRankedTable result.Hits
            for w in result.Warnings do eprintfn "warning: %s" w
            if result.Truncated then
                eprintfn "(showing the best %d; use --limit to see more)" request.Limit

            // Answer now with lexical results; fetch the model afterwards so the next search is semantic.
            match RankedSearch.resolveMode mode eff with
            | Ok wanted when wanted <> Lex && not searcher.IsModelAvailable && Environment.GetEnvironmentVariable "FERRET_OFFLINE" <> "1" ->
                eprintfn "Downloading the embedding model so the next search can use %s..." (SearchMode.toString wanted)
                try
                    searcher.EnsureModelAsync(cts.Token).GetAwaiter().GetResult() |> ignore
                    eprintfn "Model ready."
                with
                | :? OperationCanceledException -> eprintfn "Model download cancelled."
                | ex -> eprintfn "warning: model download failed: %s" (ex.GetBaseException().Message)
            | _ -> ()
            0

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match cmd.Query.Terms with
    | [] ->
        match LocalSearch.execute deps cmd.Query with
        | Error e -> renderError e; 1
        | Ok results ->
            match cmd.Format with
            | Text  -> renderText results
            | Json  -> renderJson results
            | Table -> renderTable results
            0
    | terms -> runRanked deps cmd terms
