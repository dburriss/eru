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
            for KeyValue(remotePath, entry) in idx do
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
                candidates.Add({
                    AbsPath     = file
                    RelPath     = relPath
                    RemotePath  = None
                    Source      = Local
                    SourceName  = None
                    Tags        = fm.Tags
                    Description = fm.Description
                })

    candidates |> Seq.toList
