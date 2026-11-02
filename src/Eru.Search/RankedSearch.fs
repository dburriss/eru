module Eru.Search.RankedSearch

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Eru
open Eru.Adapters

type RankedHit = {
    Candidate : CandidateFile
    /// 0 for listings and metadata-only hits.
    Score     : float
    Excerpts  : string list
}

type RankedResult = {
    /// Best first: ranked file hits, then metadata-only matches.
    Hits      : RankedHit list
    Truncated : bool
    Warnings  : string list
    /// The mode that actually ran, which can differ from the one asked for.
    Mode      : SearchMode
}

type Request = {
    /// Free-text terms. Empty lists every candidate (after tag filtering) without ranking.
    Query : string
    /// All must be present on a file (case-insensitive).
    Tags  : string list
    /// None uses the configured mode.
    Mode  : SearchMode option
    /// Maximum hits returned, at least 1.
    Limit : int
}

/// Files returned when a caller gives no limit: what a person or an agent reads in one go.
let defaultLimit = 15

// ── pure helpers ───────────────────────────────────────────────────────────────

let private pathKey (c: CandidateFile) = c.RemotePath |> Option.defaultValue c.RelPath

/// Drops candidates matching the `search.ignorePatterns` globs (see `Patterns.matchesAny`).
let applyIgnore (patterns: string list) (candidates: CandidateFile list) : CandidateFile list =
    if patterns.IsEmpty then candidates
    else candidates |> List.filter (fun c -> not (Patterns.matchesAny patterns (pathKey c)))

/// Like `applyIgnore`, but a source's own `search.ignorePatterns` replace the global ones for its
/// files; candidates that belong to no configured source use the global patterns.
let applyIgnoreFor (eff: EffectiveConfig) (candidates: CandidateFile list) : CandidateFile list =
    let perSource =
        eff.Sources |> List.map (fun s -> s.Name, (Config.effectiveFor eff s).SearchIgnorePatterns) |> Map.ofList
    candidates
    |> List.filter (fun c ->
        let patterns =
            c.SourceName |> Option.bind (fun n -> Map.tryFind n perSource) |> Option.defaultValue eff.SearchIgnorePatterns
        patterns.IsEmpty || not (Patterns.matchesAny patterns (pathKey c)))

let hasTags (required: string list) (itemTags: string list) : bool =
    required.IsEmpty ||
    required |> List.forall (fun t ->
        itemTags |> List.exists (fun it -> String.Equals(it, t, StringComparison.OrdinalIgnoreCase)))

let parseTags (tags: string) : string list =
    if String.IsNullOrWhiteSpace tags then []
    else
        tags.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.toList
        |> List.map (fun t -> t.ToLowerInvariant())

let private terms (query: string) : string list =
    if String.IsNullOrWhiteSpace query then []
    else query.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList |> List.map (fun t -> t.ToLowerInvariant())

/// Metadata-only candidates match when any term is a substring of the path or description.
let matchesMetadata (queryTerms: string list) (c: CandidateFile) : bool =
    queryTerms.IsEmpty ||
    queryTerms |> List.exists (fun t ->
        c.RelPath.ToLowerInvariant().Contains t ||
        (c.Description |> Option.exists (fun d -> d.ToLowerInvariant().Contains t)))

let mutable private legacyWarned = false

/// The mode to run: an explicit one, else `ERU_SEARCH_MODE`, else the deprecated
/// `ERU_SEARCH_BACKEND` (`ck` meant semantic, anything else lexical), else the configured one.
/// The deprecation note is written to stderr once per process (never stdout: the MCP server
/// speaks its protocol there).
let resolveMode (explicit: SearchMode option) (eff: EffectiveConfig) : Result<SearchMode, string> =
    let parseEnv (name: string) =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> None
        | v -> Some v
    match explicit with
    | Some m -> Ok m
    | None ->
        match parseEnv "ERU_SEARCH_MODE" with
        | Some v ->
            SearchMode.tryParse v
            |> Option.map Ok
            |> Option.defaultValue (Error $"ERU_SEARCH_MODE must be one of lex, sem, hybrid (got '{v}')")
        | None ->
            match parseEnv "ERU_SEARCH_BACKEND" with
            | Some v ->
                if not legacyWarned then
                    legacyWarned <- true
                    eprintfn "[eru] WARNING: ERU_SEARCH_BACKEND is deprecated and ignored as a backend choice; use search.mode (lex, sem or hybrid) or ERU_SEARCH_MODE. Treating '%s' as %s." v (if v = "ck" then "hybrid" else "lex")
                Ok (if v = "ck" then Hybrid else Lex)
            | None -> Ok eff.SearchMode

// ── the Ferret-backed searcher ─────────────────────────────────────────────────

let private ferretMode (m: SearchMode) : Ferret.SearchMode =
    match m with
    | Lex    -> Ferret.SearchMode.Lexical
    | Sem    -> Ferret.SearchMode.Semantic
    | Hybrid -> Ferret.SearchMode.Hybrid

let private excerptLines (doc: Ferret.Domain.ScoredDoc) : string list =
    doc.Excerpts |> List.map (fun h -> h.Line.Trim())

/// Owns one `FerretClient`: it holds the loaded embedding model, so keep a single instance
/// alive (MCP server, serve) and share it. Calls are serialised because indexes are
/// files shared by every search and build.
type Searcher(cacheDir: string, progress: Action<string>) =
    let source = CandidateFileSource(Path.Combine(cacheDir, "candidates"))
    let gate = new SemaphoreSlim(1, 1)
    let client =
        Directory.CreateDirectory cacheDir |> ignore
        let options = Ferret.FerretOptions(Roots = [| source.Root |], CacheDir = cacheDir, Progress = progress)
        let index = Ferret.DiskIndexStore.DiskIndexStore(Path.Combine(cacheDir, "lexical.idx"))
        new Ferret.FerretClient(options, source, index)

    let withGate (ct: CancellationToken) (f: unit -> 'a) : 'a =
        gate.Wait ct
        try f () finally gate.Release() |> ignore

    new(cacheDir: string) = new Searcher(cacheDir, null)

    /// True when semantic and hybrid search can run without downloading the model.
    member _.IsModelAvailable : bool = client.IsModelAvailable

    /// Downloads the model if it is missing; cancelling leaves no partial file.
    member _.EnsureModelAsync(ct: CancellationToken) : Task<string> = client.EnsureModelAsync ct

    /// Builds or updates the index for `mode` over `candidates` (the files that will be searched).
    member _.BuildIndex(candidates: CandidateFile list, mode: SearchMode, ct: CancellationToken) : int =
        withGate ct (fun () ->
            source.Set(candidates |> List.map (fun c -> c.AbsPath))
            client.BuildIndex(ferretMode mode, ct))

    /// Deletes the persisted indexes for `mode`; the model stays.
    member _.Clean(mode: SearchMode) : unit =
        withGate CancellationToken.None (fun () -> client.Clean(ferretMode mode))

    /// Ranks `candidates` (already ignore-filtered) against `query` and keeps those carrying all `tags`.
    /// Falls back to lexical, with a warning, when semantic search is asked for but not possible.
    member _.Rank(candidates: CandidateFile list, query: string, tags: string list, mode: SearchMode, limit: int, ct: CancellationToken) : RankedResult =
        let candidates = candidates |> List.distinctBy (fun c -> c.AbsPath)
        let byPath = candidates |> List.map (fun c -> c.AbsPath, c) |> dict
        // Ferret has no tag filter and keeps its indexes keyed by the searched set, so it is
        // always given the full set and asked for every ranked file when tags narrow it.
        let wanted = if tags.IsEmpty then limit else max limit (max 1 candidates.Length)

        let run (m: SearchMode) =
            withGate ct (fun () ->
                source.Set(candidates |> List.map (fun c -> c.AbsPath))
                client.Search(query, wanted, ferretMode m, ct))

        let result, usedMode, warnings =
            match mode with
            | Lex -> run Lex, Lex, []
            | _ when not client.IsModelAvailable ->
                run Lex, Lex,
                [ $"{SearchMode.toString mode} search needs the embedding model, which is not installed: used lexical search instead" ]
            | _ ->
                try run mode, mode, []
                with
                | :? OperationCanceledException -> reraise ()
                | ex ->
                    run Lex, Lex,
                    [ $"{SearchMode.toString mode} search failed ({ex.GetBaseException().Message}): used lexical search instead" ]

        let hits =
            result.Docs
            |> Array.toList
            |> List.choose (fun d ->
                match byPath.TryGetValue d.File with
                | true, c when hasTags tags c.Tags -> Some { Candidate = c; Score = d.Score; Excerpts = excerptLines d }
                | _ -> None)

        let kept = hits |> List.truncate limit
        { Hits      = kept
          Truncated = if tags.IsEmpty then result.Truncated else hits.Length > limit
          Warnings  = warnings
          Mode      = usedMode }

    interface IDisposable with
        member _.Dispose() =
            (client :> IDisposable).Dispose()
            gate.Dispose()

// ── orchestration ──────────────────────────────────────────────────────────────

/// The candidates Ferret searches: every readable file eru knows, minus ignored ones.
let readableCandidates (deps: Deps) (eff: EffectiveConfig) (cwd: string) : CandidateFile list =
    CandidateBuilder.build deps eff cwd |> applyIgnoreFor eff

/// Ranked search over everything eru knows about: files are ranked by content (path, headings
/// and front matter count extra), tag-filtered and bounded by `limit`; entries with no local
/// content are matched on path and description and listed after the ranked files.
let execute (searcher: Searcher) (deps: Deps) (eff: EffectiveConfig) (cwd: string) (req: Request) (ct: CancellationToken) : Result<RankedResult, string> =
    resolveMode req.Mode eff
    |> Result.map (fun mode ->
        let limit = max 1 req.Limit
        let queryTerms = terms req.Query
        let candidates = readableCandidates deps eff cwd

        let ranked =
            if queryTerms.IsEmpty then
                let all =
                    candidates
                    |> List.filter (fun c -> hasTags req.Tags c.Tags)
                    |> List.sortWith (fun a b -> String.CompareOrdinal(a.RelPath, b.RelPath))
                { Hits      = all |> List.truncate limit |> List.map (fun c -> { Candidate = c; Score = 0.0; Excerpts = [] })
                  Truncated = all.Length > limit
                  Warnings  = []
                  Mode      = mode }
            else
                searcher.Rank(candidates, req.Query, req.Tags, mode, limit, ct)

        let metadataHits =
            CandidateBuilder.buildMetadataOnly deps eff
            |> applyIgnoreFor eff
            |> List.filter (fun c -> hasTags req.Tags c.Tags && matchesMetadata queryTerms c)
            |> List.sortWith (fun a b -> String.CompareOrdinal(a.RelPath, b.RelPath))

        let room = limit - ranked.Hits.Length
        let kept = metadataHits |> List.truncate (max 0 room)
        { ranked with
            Hits      = ranked.Hits @ (kept |> List.map (fun c -> { Candidate = c; Score = 0.0; Excerpts = [] }))
            Truncated = ranked.Truncated || kept.Length < metadataHits.Length })

/// Builds the index the search modes need so the first search is fast. Downloads the model
/// first when `downloadModel` is set and a semantic mode is configured.
let warmUp (searcher: Searcher) (deps: Deps) (eff: EffectiveConfig) (cwd: string) (mode: SearchMode) (downloadModel: bool) (ct: CancellationToken) : Task<unit> =
    task {
        let! effective =
            task {
                match mode with
                | Lex -> return Lex
                | _ when searcher.IsModelAvailable -> return mode
                | _ when downloadModel ->
                    try
                        let! _ = searcher.EnsureModelAsync ct
                        return mode
                    with
                    | :? OperationCanceledException -> return raise (OperationCanceledException ct)
                    | _ -> return Lex
                | _ -> return Lex
            }
        let candidates = readableCandidates deps eff cwd
        do! Task.Run((fun () -> searcher.BuildIndex(candidates, effective, ct) |> ignore), ct)
    }
