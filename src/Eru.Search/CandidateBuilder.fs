module Eru.Search.CandidateBuilder

open System.IO
open Eru
open Eru.Adapters

let private isGlob (path: string) = path.Contains('*') || path.Contains('?') || path.Contains('[')

let private resolveAbsPath (sourceName: string) (entry: IndexEntry) (localEntry: LockEntry option) : string option =
    let localPath = entry.LocalPath |> Option.orElse (localEntry |> Option.map (fun e -> e.LocalPath))
    match localPath with
    | Some p when File.Exists p -> Some p
    | _ ->
        match entry.CacheRelPath with
        | Some rel ->
            let abs = Path.Combine(Paths.sourceCacheDir sourceName, rel)
            if File.Exists abs then Some abs else None
        | None -> None

/// Build the full candidate list from the source index cache, lock file entries not
/// in any index, and local knowledge/ directories. Returns only candidates with
/// accessible file content (no metadata-only entries).
let build (deps: Deps) (eff: EffectiveConfig) (cwd: string) : CandidateFile list =
    let isPathAllowed path =
        not (Patterns.isPathBlocked eff.BlockPatterns eff.AllowPatterns path)

    let candidates = System.Collections.Generic.List<CandidateFile>()

    let lockEntryMap =
        match deps.ReadLockEntries eff.StateFile with
        | Ok entries -> entries |> List.map (fun e -> (e.SourceName, e.RemotePath), e) |> Map.ofList
        | Error _    -> Map.empty

    // 1. Index-based candidates from all sources
    for src in eff.Sources do
        match SourceIndexAdapter.readIndex src.Name with
        | Ok (Some idx) ->
            for KeyValue(remotePath, entry) in idx.Entries do
                if not (isGlob remotePath) && isPathAllowed remotePath then
                    let lockEntry = lockEntryMap |> Map.tryFind (src.Name, remotePath)
                    let colTags =
                        eff.Collections
                        |> List.tryFind (fun c -> c.Source = src.Name && c.RemotePath = remotePath)
                        |> Option.map (fun c -> c.Tags)
                        |> Option.defaultValue []
                    let allTags = (entry.Tags @ colTags) |> List.distinct
                    match resolveAbsPath src.Name entry lockEntry with
                    | Some absPath ->
                        let relPath =
                            lockEntry
                            |> Option.map (fun e -> e.LocalPath)
                            |> Option.defaultValue ($"{src.Name}/{remotePath}")
                        candidates.Add({
                            AbsPath     = absPath
                            RelPath     = relPath
                            RemotePath  = Some remotePath
                            Source      = Cache
                            SourceName  = Some src.Name
                            Tags        = allTags
                            Description = entry.Description
                        })
                    | None -> ()
        | _ -> ()

    // 2. Lock file entries not covered by any index
    let indexedPaths = candidates |> Seq.map (fun c -> c.RelPath) |> Set.ofSeq
    for KeyValue((sn, rp), lockEntry) in lockEntryMap do
        let relPath = lockEntry.LocalPath
        if not (Set.contains relPath indexedPaths) && isPathAllowed rp && File.Exists lockEntry.LocalPath then
            candidates.Add({
                AbsPath     = lockEntry.LocalPath
                RelPath     = lockEntry.LocalPath
                RemotePath  = Some rp
                Source      = Lock
                SourceName  = Some sn
                Tags        = lockEntry.Tags
                Description = lockEntry.Description
            })

    // 3. Local knowledge directories
    // Enumerate the real directory names: probing `knowledge` and `KNOWLEDGE` finds the same
    // directory twice on a case-insensitive file system and every file would be listed twice.
    let knowledgeDirs =
        if Directory.Exists cwd then
            Directory.EnumerateDirectories cwd
            |> Seq.filter (fun d -> System.String.Equals(Path.GetFileName d, "knowledge", System.StringComparison.OrdinalIgnoreCase))
            |> Seq.toList
        else []
    for dirPath in knowledgeDirs do
        for file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories) do
            let relPath = Path.GetRelativePath(cwd, file)
            if isPathAllowed relPath then
                let fm = Frontmatter.parse YamlAdapter.parse (File.ReadAllText file)
                candidates.Add({
                    AbsPath     = file
                    RelPath     = relPath
                    RemotePath  = None
                    Source      = Local
                    SourceName  = None
                    Tags        = Frontmatter.tags fm
                    Description = Frontmatter.description fm
                })

    candidates |> Seq.toList

/// Source-index entries with no readable local file or cached copy. They carry only a path,
/// tags and a description, so they can be matched on those but not on content.
let buildMetadataOnly (deps: Deps) (eff: EffectiveConfig) : CandidateFile list =
    let isPathAllowed path =
        not (Patterns.isPathBlocked eff.BlockPatterns eff.AllowPatterns path)

    let lockEntryMap =
        match deps.ReadLockEntries eff.StateFile with
        | Ok entries -> entries |> List.map (fun e -> (e.SourceName, e.RemotePath), e) |> Map.ofList
        | Error _    -> Map.empty

    [ for src in eff.Sources do
        match SourceIndexAdapter.readIndex src.Name with
        | Ok (Some idx) ->
            for KeyValue(remotePath, entry) in idx.Entries do
                if not (isGlob remotePath) && isPathAllowed remotePath then
                    let lockEntry = lockEntryMap |> Map.tryFind (src.Name, remotePath)
                    match resolveAbsPath src.Name entry lockEntry with
                    | Some _ -> ()
                    | None ->
                        let colTags =
                            eff.Collections
                            |> List.tryFind (fun c -> c.Source = src.Name && c.RemotePath = remotePath)
                            |> Option.map (fun c -> c.Tags)
                            |> Option.defaultValue []
                        yield {
                            AbsPath     = ""
                            RelPath     = $"{src.Name}/{remotePath}"
                            RemotePath  = Some remotePath
                            Source      = Cache
                            SourceName  = Some src.Name
                            Tags        = (entry.Tags @ colTags) |> List.distinct
                            Description = entry.Description
                        }
        | _ -> () ]
