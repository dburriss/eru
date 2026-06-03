namespace Eru

module Sync =

    type Options = { DryRun: bool }

    type SyncStatus =
        | Current
        | Drifted   // "would update" on dry-run; "updated" on actual run
        | Missing
        | Skipped of string
        | Blocked

    type SyncEntry = {
        Status    : SyncStatus
        LocalPath : string
    }

    type SyncResult = {
        Entries : SyncEntry list
        DryRun  : bool
    }

    type private EntryResult =
        | ECurrent of LockEntry
        | EDrifted of LockEntry * string
        | EMissing of LockEntry
        | ESkipped of LockEntry * string
        | EBlocked of LockEntry

    let private toSyncEntry (r: EntryResult) : SyncEntry =
        match r with
        | ECurrent e       -> { Status = Current;      LocalPath = e.LocalPath }
        | EDrifted (e, _)  -> { Status = Drifted;      LocalPath = e.LocalPath }
        | EMissing e       -> { Status = Missing;      LocalPath = e.LocalPath }
        | ESkipped (e, rs) -> { Status = Skipped rs;   LocalPath = e.LocalPath }
        | EBlocked e       -> { Status = Blocked;      LocalPath = e.LocalPath }

    let private emptyIndexEntry = {
        Tags         = []
        Description  = None
        LocalPath    = None
        CacheRelPath = None
        ContentHash  = None
    }

    // Populate sources/<name>/index.json and sources/<name>/files/ cache.
    // Called by execute and by KnowledgeSyncService. Non-fatal errors are returned in the result list.
    let populateIndex (deps: Deps) : string list =
        let globalCfg = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfg  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None

        let baseEff =
            match Config.merge globalCfg localCfg with
            | Ok e  -> e
            | Error _ -> { Sources = []; CommitOnPull = false; StateFile = "eru.lock"
                           Collections = []; McpRefreshIntervalMinutes = 60
                           BlockPatterns = Config.defaultBlockPatterns
                           AllowPatterns = Config.defaultAllowPatterns
                           AllowBinaries = Config.defaultAllowBinaries }

        // Step 1a: Fetch and cache manifests
        for src in baseEff.Sources do
            match src.Url with
            | None -> ()
            | Some url ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch [".eru/manifest.json"] with
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest src.Name raw |> ignore
                | _ -> ()

        // Step 1b: Reload eff with fresh manifests
        let eff = Config.withManifests deps.ReadCachedManifest baseEff

        let errors = System.Collections.Generic.List<string>()

        // Step 1c: Rebuild index.json for each source from manifest metadata (no content fetch).
        // Glob patterns are excluded — they are replaced by resolved paths in Step 2.
        let isGlob (path: string) = path.Contains('*') || path.Contains('?') || path.Contains('[')
        for src in eff.Sources do
            let initialIndex =
                match deps.ReadCachedManifest src.Name with
                | Ok (Some manifest) ->
                    manifest.Files
                    |> List.filter (fun f -> not (isGlob f.Path))
                    |> List.map (fun f ->
                        f.Path, { emptyIndexEntry with
                                    Tags        = f.Tags |> List.map (fun t -> t.ToLowerInvariant()) |> List.distinct
                                    Description = f.Description })
                    |> Map.ofList
                | _ -> Map.empty
            deps.WriteSourceIndex src.Name initialIndex |> ignore

        // Step 2: Fetch and cache collection files; merge frontmatter tags into index
        eff.Collections
        |> List.groupBy (fun f -> f.Source)
        |> List.iter (fun (sourceName, sourceFiles) ->
            match eff.Sources |> List.tryFind (fun s -> s.Name = sourceName) with
            | None ->
                errors.Add($"unknown source '{sourceName}'")
            | Some src ->
                match src.Url with
                | None ->
                    errors.Add($"source '{sourceName}' has no URL configured")
                | Some url ->
                    let branch = src.Branch |> Option.defaultValue "HEAD"
                    let remotePaths = sourceFiles |> List.map (fun f -> f.RemotePath)
                    match deps.FetchRemoteContent url branch remotePaths with
                    | Error e ->
                        errors.Add($"fetch failed for source '{sourceName}': {e}")
                    | Ok files ->
                        let mutable idx =
                            match deps.ReadSourceIndex sourceName with
                            | Ok (Some m) -> m
                            | _ -> Map.empty
                        for (resolvedPath, content) in files do
                            let contentHash = deps.HashContent content
                            let cacheRelPath =
                                match deps.CacheSourceContent sourceName contentHash content with
                                | Ok p  -> Some p
                                | Error _ -> None
                            let fm = Frontmatter.parse content
                            let existing = Map.tryFind resolvedPath idx |> Option.defaultValue emptyIndexEntry
                            let mergedTags =
                                (existing.Tags @ (fm.Tags |> List.map (fun t -> t.ToLowerInvariant())))
                                |> List.distinct
                            idx <- idx |> Map.add resolvedPath {
                                existing with
                                    Tags         = mergedTags
                                    Description  = existing.Description |> Option.orElse fm.Description
                                    CacheRelPath = cacheRelPath
                                    ContentHash  = Some contentHash
                            }
                            match cacheRelPath with
                            | Some relPath -> deps.BuildSearchIndex sourceName relPath
                            | None         -> ()
                        deps.WriteSourceIndex sourceName idx |> ignore)

        // Step 3: Fetch and cache lock-only entries (not covered by manifest or collection)
        let collectionPaths =
            eff.Collections
            |> List.map (fun f -> (f.Source, f.RemotePath))
            |> Set.ofList

        match deps.ReadLockEntries eff.StateFile with
        | Error _ -> ()
        | Ok lockEntries ->
            lockEntries
            |> List.filter (fun e -> not (Set.contains (e.SourceName, e.RemotePath) collectionPaths))
            |> List.groupBy (fun e -> e.SourceName)
            |> List.iter (fun (sourceName, orphans) ->
                match eff.Sources |> List.tryFind (fun s -> s.Name = sourceName) with
                | None -> ()
                | Some src ->
                    match src.Url with
                    | None -> ()
                    | Some url ->
                        let branch = src.Branch |> Option.defaultValue "HEAD"
                        let remotePaths = orphans |> List.map (fun e -> e.RemotePath)
                        match deps.FetchRemoteContent url branch remotePaths with
                        | Error _ -> ()
                        | Ok files ->
                            let mutable idx =
                                match deps.ReadSourceIndex sourceName with
                                | Ok (Some m) -> m
                                | _ -> Map.empty
                            for (resolvedPath, content) in files do
                                let contentHash = deps.HashContent content
                                let cacheRelPath =
                                    match deps.CacheSourceContent sourceName contentHash content with
                                    | Ok p  -> Some p
                                    | Error _ -> None
                                let fm = Frontmatter.parse content
                                let existing = Map.tryFind resolvedPath idx |> Option.defaultValue emptyIndexEntry
                                idx <- idx |> Map.add resolvedPath {
                                    existing with
                                        Tags = (existing.Tags @ (fm.Tags |> List.map (fun t -> t.ToLowerInvariant()))) |> List.distinct
                                        Description = existing.Description |> Option.orElse fm.Description
                                        CacheRelPath = cacheRelPath
                                        ContentHash  = Some contentHash
                                }
                                match cacheRelPath with
                                | Some relPath -> deps.BuildSearchIndex sourceName relPath
                                | None         -> ()
                            deps.WriteSourceIndex sourceName idx |> ignore)

            // Step 4: Set LocalPath on index entries from lock file
            lockEntries
            |> List.groupBy (fun e -> e.SourceName)
            |> List.iter (fun (sourceName, entries) ->
                let mutable idx =
                    match deps.ReadSourceIndex sourceName with
                    | Ok (Some m) -> m
                    | _ -> Map.empty
                let mutable changed = false
                for entry in entries do
                    match Map.tryFind entry.RemotePath idx with
                    | Some existing when existing.LocalPath <> Some entry.LocalPath ->
                        idx <- idx |> Map.add entry.RemotePath { existing with LocalPath = Some entry.LocalPath }
                        changed <- true
                    | None ->
                        idx <- idx |> Map.add entry.RemotePath {
                            Tags        = entry.Tags |> List.map (fun t -> t.ToLowerInvariant())
                            Description = entry.Description
                            LocalPath   = Some entry.LocalPath
                            CacheRelPath = None
                            ContentHash  = None
                        }
                        changed <- true
                    | _ -> ()
                if changed then
                    deps.WriteSourceIndex sourceName idx |> ignore)

        errors |> Seq.toList

    let execute (deps: Deps) (opts: Options) : Result<SyncResult, string> =
        // Populate index and cache (best-effort, errors are non-fatal for local sync)
        populateIndex deps |> ignore

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        let eff = Config.withManifests deps.ReadCachedManifest eff

        match deps.ReadLockEntries eff.StateFile with
        | Error e -> Error $"Error reading lock file: {e}"
        | Ok entries ->

        // Build content map: try cache first, then fall back to network per source
        let sourceIndices =
            entries
            |> List.map (fun e -> e.SourceName)
            |> List.distinct
            |> List.choose (fun sn ->
                match deps.ReadSourceIndex sn with
                | Ok (Some idx) -> Some (sn, idx)
                | _ -> None)
            |> Map.ofList

        let contentBySource : Map<string, Map<string, string>> =
            entries
            |> List.groupBy (fun e -> e.SourceName)
            |> List.choose (fun (sourceName, sourceEntries) ->
                match eff.Sources |> List.tryFind (fun s -> s.Name = sourceName) with
                | None -> None
                | Some src ->
                    let idxOpt = Map.tryFind sourceName sourceIndices

                    let cachedContent =
                        match idxOpt with
                        | None -> Map.empty
                        | Some idx ->
                            sourceEntries
                            |> List.choose (fun e ->
                                match Map.tryFind e.RemotePath idx with
                                | Some entry when entry.CacheRelPath.IsSome ->
                                    match deps.ReadCachedSourceContent sourceName entry.CacheRelPath.Value with
                                    | Ok (Some content) -> Some (e.RemotePath, content)
                                    | _ -> None
                                | _ -> None)
                            |> Map.ofList

                    let uncachedPaths =
                        sourceEntries
                        |> List.filter (fun e -> not (Map.containsKey e.RemotePath cachedContent))
                        |> List.map (fun e -> e.RemotePath)

                    let fetchedContent =
                        if uncachedPaths.IsEmpty then Map.empty
                        else
                            match src.Url with
                            | None -> Map.empty
                            | Some url ->
                                let branch = src.Branch |> Option.defaultValue "HEAD"
                                match deps.FetchRemoteContent url branch uncachedPaths with
                                | Ok files -> files |> Map.ofList
                                | Error _  -> Map.empty

                    let allContent = Map.fold (fun acc k v -> Map.add k v acc) cachedContent fetchedContent
                    Some (sourceName, allContent))
            |> Map.ofList

        let classified =
            entries |> List.map (fun entry ->
                if Patterns.isPathBlocked eff.BlockPatterns eff.AllowPatterns entry.RemotePath then
                    EBlocked entry
                else
                match eff.Sources |> List.tryFind (fun s -> s.Name = entry.SourceName) with
                | None -> ESkipped (entry, $"source '{entry.SourceName}' not configured")
                | Some source ->
                    match source.Url with
                    | None -> ESkipped (entry, $"source '{entry.SourceName}' has no URL")
                    | Some _ ->
                        let contentMap = contentBySource |> Map.tryFind entry.SourceName |> Option.defaultValue Map.empty
                        match Map.tryFind entry.RemotePath contentMap with
                        | None -> EMissing entry
                        | Some content ->
                            if Patterns.isBlocked eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries entry.RemotePath content then
                                EBlocked entry
                            else
                                let hash = deps.HashContent content
                                if hash = entry.ContentHash then ECurrent entry
                                else EDrifted (entry, content))

        if opts.DryRun then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = true }
        else

        let drifted = classified |> List.choose (function EDrifted (e, c) -> Some (e, c) | _ -> None)

        if drifted.IsEmpty then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = false }
        else

        let writeError =
            drifted |> List.tryPick (fun (entry, content) ->
                match deps.WriteLocalFile entry.LocalPath content with
                | Error e -> Some e
                | Ok ()   -> None)

        match writeError with
        | Some e -> Error $"Error writing file: {e}"
        | None ->

        let updatedEntries =
            entries |> List.map (fun entry ->
                match drifted |> List.tryFind (fun (e, _) -> e.LocalPath = entry.LocalPath) with
                | Some (_, content) -> { entry with ContentHash = deps.HashContent content }
                | None              -> entry)

        match deps.WriteLockEntries eff.StateFile updatedEntries with
        | Error e -> Error $"Error writing lock file: {e}"
        | Ok () -> Ok { Entries = classified |> List.map toSyncEntry; DryRun = false }
