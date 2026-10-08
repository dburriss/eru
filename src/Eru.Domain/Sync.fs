namespace Eru

module Sync =

    type Options = { DryRun: bool }

    type SyncStatus =
        | Current
        | Drifted       // "would update" on dry-run; "updated" on actual run
        | LocalDrifted  // "would restore" on dry-run; "restored" on actual run
        | Missing
        | Skipped of string
        | Blocked

    type SyncEntry = {
        Status    : SyncStatus
        LocalPath : string
    }

    // What a source's index holds after a sync. Bundles are "<kind>:<path>" ("/" = root).
    type IndexSummary = {
        Source  : string
        Entries : int
        Bundles : string list
    }

    type SyncResult = {
        Entries : SyncEntry list
        DryRun  : bool
        Errors  : string list
        Indexes : IndexSummary list
    }

    // A source that indexed nothing is almost always a setup problem, so say why it might be.
    let indexWarning (s: IndexSummary) : string option =
        if s.Entries > 0 then None
        elif s.Bundles.IsEmpty then
            Some $"source '{s.Source}': 0 entries indexed and no bundles registered (use 'eru source bundle add' or 'eru source add --scan')"
        else
            let bundles = String.concat ", " s.Bundles
            Some $"source '{s.Source}': 0 entries indexed (bundles: {bundles}); okf concept files need a non-empty 'type' frontmatter"

    type private EntryResult =
        | ECurrent      of LockEntry
        | EDrifted      of LockEntry * string
        | ELocalDrifted of LockEntry * string
        | EMissing      of LockEntry
        | ESkipped      of LockEntry * string
        | EBlocked      of LockEntry

    let private toSyncEntry (r: EntryResult) : SyncEntry =
        match r with
        | ECurrent e            -> { Status = Current;       LocalPath = e.LocalPath }
        | EDrifted (e, _)       -> { Status = Drifted;       LocalPath = e.LocalPath }
        | ELocalDrifted (e, _)  -> { Status = LocalDrifted;  LocalPath = e.LocalPath }
        | EMissing e            -> { Status = Missing;       LocalPath = e.LocalPath }
        | ESkipped (e, rs)      -> { Status = Skipped rs;    LocalPath = e.LocalPath }
        | EBlocked e            -> { Status = Blocked;       LocalPath = e.LocalPath }

    let private emptyIndexEntry = {
        Contributions = Map.empty
        Tags         = []
        Description  = None
        LocalPath    = None
        CacheRelPath = None
        ContentHash  = None
        Type         = None
        Title        = None
        OkfStatus    = None
        Generated    = None
        Verified     = []
        StaleAfter   = None
        Resource     = None
    }

    let private emptySourceIndex = {
        Version                     = 1
        SourceHeadSha               = None
        ConsecutiveShaCheckFailures = 0
        Entries                     = Map.empty
    }

    let private isGlob (path: string) = path.Contains('*') || path.Contains('?') || path.Contains('[')

    // A distinct cache key (not a real source name) for a non-root Manifest bundle's
    // own .eru/manifest.json, kept separate from the source's root-level manifest cache.
    let private bundleManifestCacheKey (sourceName: string) (bundlePath: string) : string =
        $"{sourceName}/_bundles/{bundlePath}"

    let private normalizeTags (tags: string list) : string list =
        tags |> List.map (fun t -> t.ToLowerInvariant()) |> List.distinct

    let private readIndexOrEmpty (deps: Deps) (sourceName: string) : SourceIndex =
        match deps.ReadSourceIndex sourceName with
        | Ok (Some idx) -> idx
        | _             -> emptySourceIndex

    // Per (remotePath, manifestBundlePath, Contribution) — the same remotePath may
    // appear once per covering Manifest bundle, folded into one Contributions map keyed
    // by ContributionKey.manifest bundlePath before it reaches the index.
    let private resolveManifestContributions (deps: Deps) (src: SourceConfig) : (string * string * Contribution) list =
        let manifestBundles = src.Bundles |> List.filter (fun b -> b.Kind = Manifest)
        if manifestBundles.IsEmpty then []
        else
            let rootManifest = match deps.ReadCachedManifest src.Name with Ok (Some m) -> Some m | _ -> None
            let toContribution (f: ManifestFileRef) : Contribution =
                { Tags = normalizeTags f.Tags; Description = f.Description }
            manifestBundles
            |> List.collect (fun b ->
                if b.Path = "" then
                    match rootManifest with
                    | None -> []
                    | Some m ->
                        m.Files
                        |> List.filter (fun f -> not (isGlob f.Path))
                        |> List.map (fun f -> f.Path, b.Path, toContribution f)
                else
                    match deps.ReadCachedManifest (bundleManifestCacheKey src.Name b.Path) with
                    | Ok (Some m) ->
                        m.Files
                        |> List.filter (fun f -> not (isGlob f.Path))
                        |> List.map (fun f -> (b.Path + "/" + f.Path), b.Path, toContribution f)
                    | _ ->
                        // No bundle-local manifest — fall back to partitioning the
                        // shared root manifest's entries by longest-matching prefix.
                        match rootManifest with
                        | None -> []
                        | Some m ->
                            m.Files
                            |> List.filter (fun f -> not (isGlob f.Path))
                            |> List.filter (fun f -> Bundle.mostSpecificManifestBundle manifestBundles f.Path = Some b)
                            |> List.map (fun f -> f.Path, b.Path, toContribution f))

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
                           AllowBinaries = Config.defaultAllowBinaries
                           SiteIgnorePatterns = Config.defaultSiteIgnorePatterns
                           SiteHideEmptyBundles = Config.defaultSiteHideEmptyBundles
                           OkfIgnorePatterns = Config.defaultOkfIgnorePatterns
                           Inboxes = Map.empty
                           DefaultInbox = None
                           InboxWatchIntervalSeconds = 30 }

        let errors = System.Collections.Generic.List<string>()

        // Step 1a: Fetch and cache manifests — the shared root manifest, plus each
        // non-root Manifest bundle's own .eru/manifest.json (decision #1).
        for src in baseEff.Sources do
            match src.Url with
            | None -> ()
            | Some url ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch [".eru/manifest.json"] with
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest src.Name raw |> ignore
                | _ -> ()
                for b in src.Bundles do
                    if b.Kind = Manifest && b.Path <> "" then
                        let bundleManifestPath = $"{b.Path}/.eru/manifest.json"
                        match deps.FetchRemoteContent url branch [bundleManifestPath] with
                        | Ok ((_, raw) :: _) -> deps.CacheSourceManifest (bundleManifestCacheKey src.Name b.Path) raw |> ignore
                        | _ -> ()

        // Step 1b: Reload eff with fresh manifests
        let eff = Config.withManifests deps.ReadCachedManifest baseEff

        // Step 1c: Rebuild index.json for each source from manifest metadata (no content fetch).
        // Glob patterns are excluded — they are replaced by resolved paths in Step 2.
        // This wipes and reseeds Entries every sync, but preserves SourceHeadSha /
        // ConsecutiveShaCheckFailures — the discovery SHA cache must survive a
        // manifest-only sync.
        for src in eff.Sources do
            let existingIdx = readIndexOrEmpty deps src.Name
            let contribs = resolveManifestContributions deps src
            let initialEntries =
                contribs
                |> List.groupBy (fun (remotePath, _, _) -> remotePath)
                |> List.map (fun (remotePath, group) ->
                    let contributions =
                        group |> List.fold (fun acc (_, bundlePath, c) -> Map.add (ContributionKey.manifest bundlePath) c acc) Map.empty
                    let blended = IndexBlend.blend src.Bundles remotePath contributions
                    remotePath, { emptyIndexEntry with
                                    Contributions = contributions
                                    Tags          = blended.Tags
                                    Description   = blended.Description })
                |> Map.ofList
            // Entries discovered from an Okf bundle are only rebuilt when the remote HEAD
            // moves (Step 1d), so they must survive this reseed or an unchanged remote
            // would leave the index empty.
            let okfBundles = src.Bundles |> List.filter (fun b -> b.Kind = Okf)
            let discovered =
                existingIdx.Entries
                |> Map.filter (fun path e ->
                    not (Map.containsKey path initialEntries)
                    && e.ContentHash.IsSome
                    && okfBundles |> List.exists (fun b -> Bundle.covers b path))
            let entries = discovered |> Map.fold (fun acc k v -> Map.add k v acc) initialEntries
            deps.WriteSourceIndex src.Name { existingIdx with Entries = entries } |> ignore

        // Step 1d: OKF bundle discovery — SHA-gated, fails open, escalates after 3
        // consecutive GetRemoteHeadSha failures (decision #3).
        for src in eff.Sources do
            let okfBundles = src.Bundles |> List.filter (fun b -> b.Kind = Okf)
            if not okfBundles.IsEmpty then
                match src.Url with
                | None -> ()
                | Some url ->
                    let branch = src.Branch |> Option.defaultValue "HEAD"
                    let existingIdx = readIndexOrEmpty deps src.Name
                    match deps.GetRemoteHeadSha url src.Branch with
                    | Error e ->
                        let failures = existingIdx.ConsecutiveShaCheckFailures + 1
                        if failures >= 3 then
                            errors.Add($"source '{src.Name}': discovery SHA check failed {failures} times in a row: {e}")
                        deps.WriteSourceIndex src.Name { existingIdx with ConsecutiveShaCheckFailures = failures } |> ignore
                    | Ok headSha ->
                        if existingIdx.SourceHeadSha = Some headSha then
                            if existingIdx.ConsecutiveShaCheckFailures <> 0 then
                                deps.WriteSourceIndex src.Name { existingIdx with ConsecutiveShaCheckFailures = 0 } |> ignore
                        else
                            let mutable idx = existingIdx
                            let mutable discoveryFailed = false
                            let seen = System.Collections.Generic.HashSet<string>()
                            // A bundle nested under one already walked is skipped: the outer walk
                            // listed every .md beneath it, so a second walk would repeat the work.
                            let walked = System.Collections.Generic.List<Bundle>()
                            for bundle in okfBundles |> List.sortBy (fun b -> b.Path.Length) do
                              if not (walked |> Seq.exists (fun w -> Bundle.covers w bundle.Path)) then
                                match BundleDiscovery.walkBundle deps eff.OkfIgnorePatterns src.Name url branch bundle with
                                | Error e ->
                                    discoveryFailed <- true
                                    errors.Add($"source '{src.Name}': bundle discovery for '{bundle.Path}' failed: {e}")
                                | Ok discovered ->
                                    walked.Add bundle
                                    for d in discovered do
                                        seen.Add d.RemotePath |> ignore
                                        let contentHash = deps.HashContent d.Content
                                        let cacheRelPath =
                                            match deps.CacheSourceContent src.Name contentHash d.Content with
                                            | Ok p -> Some p
                                            | Error _ -> None
                                        let existing = Map.tryFind d.RemotePath idx.Entries |> Option.defaultValue emptyIndexEntry
                                        let contributions = existing.Contributions |> Map.add ContributionKey.frontmatter d.Contribution
                                        let blended = IndexBlend.blend src.Bundles d.RemotePath contributions
                                        idx <- { idx with
                                                    Entries =
                                                        idx.Entries
                                                        |> Map.add d.RemotePath {
                                                            existing with
                                                                Contributions = contributions
                                                                Tags          = blended.Tags
                                                                Description   = blended.Description
                                                                CacheRelPath  = cacheRelPath
                                                                ContentHash   = Some contentHash
                                                                Type          = d.Type
                                                                Title         = d.Title
                                                                OkfStatus     = d.OkfStatus
                                                                Generated     = d.Generated
                                                                Verified      = d.Verified
                                                                StaleAfter    = d.StaleAfter
                                                                Resource      = d.Resource
                                                        } }
                                        match cacheRelPath with
                                        | Some relPath -> deps.BuildSearchIndex src.Name relPath
                                        | None         -> ()
                            // Only after every walk succeeded: drop previously discovered entries the
                            // walk no longer saw, so files removed upstream disappear. A failed walk
                            // keeps the old entries rather than leaving the index empty.
                            if not discoveryFailed then
                                idx <- { idx with
                                            Entries =
                                                idx.Entries
                                                |> Map.filter (fun path e ->
                                                    seen.Contains path
                                                    || not (e.ContentHash.IsSome
                                                            && e.Contributions |> Map.forall (fun k _ -> k = ContributionKey.frontmatter)
                                                            && okfBundles |> List.exists (fun b -> Bundle.covers b path))) }
                            // Leave SourceHeadSha unset on failure so the next sync retries discovery.
                            let sha = if discoveryFailed then existingIdx.SourceHeadSha else Some headSha
                            deps.WriteSourceIndex src.Name { idx with SourceHeadSha = sha; ConsecutiveShaCheckFailures = 0 } |> ignore

        // Step 2: Fetch and cache collection files; merge frontmatter into the
        // "frontmatter" contribution (a full overwrite of that one key every sync —
        // fixes the staleness bug where a dropped tag used to never disappear).
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
                        let existingIdx = readIndexOrEmpty deps sourceName
                        let mutable entries = existingIdx.Entries
                        for (resolvedPath, content) in files do
                            let contentHash = deps.HashContent content
                            let cacheRelPath =
                                match deps.CacheSourceContent sourceName contentHash content with
                                | Ok p  -> Some p
                                | Error _ -> None
                            let fm = Frontmatter.parse deps.ParseYamlBlock content
                            let existing = Map.tryFind resolvedPath entries |> Option.defaultValue emptyIndexEntry
                            let contributions =
                                existing.Contributions
                                |> Map.add ContributionKey.frontmatter { Tags = normalizeTags (Frontmatter.tags fm); Description = Frontmatter.description fm }
                            let blended = IndexBlend.blend src.Bundles resolvedPath contributions
                            entries <- entries |> Map.add resolvedPath {
                                existing with
                                    Contributions = contributions
                                    Tags          = blended.Tags
                                    Description   = blended.Description
                                    CacheRelPath  = cacheRelPath
                                    ContentHash   = Some contentHash
                                    Type          = Frontmatter.type_ fm
                                    Title         = Frontmatter.title fm
                                    OkfStatus     = Frontmatter.status fm
                                    Generated     = Frontmatter.generated fm
                                    Verified      = Frontmatter.verified fm
                                    StaleAfter    = Frontmatter.staleAfter fm
                                    Resource      = Frontmatter.resource fm
                            }
                            match cacheRelPath with
                            | Some relPath -> deps.BuildSearchIndex sourceName relPath
                            | None         -> ()
                        deps.WriteSourceIndex sourceName { existingIdx with Entries = entries } |> ignore)

        // Step 3: Fetch and cache lock-only entries (not covered by manifest or collection)
        let collectionPaths =
            eff.Collections
            |> List.map CollectionFileRef.id
            |> Set.ofList

        match deps.ReadLockEntries eff.StateFile with
        | Error _ -> ()
        | Ok lockEntries ->
            lockEntries
            |> List.filter (fun e -> not (Set.contains (LockEntry.id e) collectionPaths))
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
                            let existingIdx = readIndexOrEmpty deps sourceName
                            let mutable entries = existingIdx.Entries
                            for (resolvedPath, content) in files do
                                let contentHash = deps.HashContent content
                                let cacheRelPath =
                                    match deps.CacheSourceContent sourceName contentHash content with
                                    | Ok p  -> Some p
                                    | Error _ -> None
                                let fm = Frontmatter.parse deps.ParseYamlBlock content
                                let existing = Map.tryFind resolvedPath entries |> Option.defaultValue emptyIndexEntry
                                let contributions =
                                    existing.Contributions
                                    |> Map.add ContributionKey.frontmatter { Tags = normalizeTags (Frontmatter.tags fm); Description = Frontmatter.description fm }
                                let blended = IndexBlend.blend src.Bundles resolvedPath contributions
                                entries <- entries |> Map.add resolvedPath {
                                    existing with
                                        Contributions = contributions
                                        Tags          = blended.Tags
                                        Description   = blended.Description
                                        CacheRelPath  = cacheRelPath
                                        ContentHash   = Some contentHash
                                        Type          = Frontmatter.type_ fm
                                        Title         = Frontmatter.title fm
                                        OkfStatus     = Frontmatter.status fm
                                        Generated     = Frontmatter.generated fm
                                        Verified      = Frontmatter.verified fm
                                        StaleAfter    = Frontmatter.staleAfter fm
                                        Resource      = Frontmatter.resource fm
                                }
                                match cacheRelPath with
                                | Some relPath -> deps.BuildSearchIndex sourceName relPath
                                | None         -> ()
                            deps.WriteSourceIndex sourceName { existingIdx with Entries = entries } |> ignore)

            // Step 4: Set LocalPath on index entries from lock file
            lockEntries
            |> List.groupBy (fun e -> e.SourceName)
            |> List.iter (fun (sourceName, entries) ->
                let existingIdx = readIndexOrEmpty deps sourceName
                let mutable idxEntries = existingIdx.Entries
                let mutable changed = false
                for entry in entries do
                    match Map.tryFind entry.RemotePath idxEntries with
                    | Some existing when existing.LocalPath <> Some entry.LocalPath ->
                        idxEntries <- idxEntries |> Map.add entry.RemotePath { existing with LocalPath = Some entry.LocalPath }
                        changed <- true
                    | None ->
                        idxEntries <- idxEntries |> Map.add entry.RemotePath {
                            emptyIndexEntry with
                                Contributions = Map.ofList [ ContributionKey.frontmatter, { Tags = normalizeTags entry.Tags; Description = entry.Description } ]
                                Tags        = normalizeTags entry.Tags
                                Description = entry.Description
                                LocalPath   = Some entry.LocalPath
                        }
                        changed <- true
                    | _ -> ()
                if changed then
                    deps.WriteSourceIndex sourceName { existingIdx with Entries = idxEntries } |> ignore)

        errors |> Seq.toList

    let private executeLocked (deps: Deps) (opts: Options) : Result<SyncResult, string> =
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
                                match Map.tryFind e.RemotePath idx.Entries with
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
                                | Ok files ->
                                    // Persist freshly fetched content to the cache/index so subsequent
                                    // syncs don't need to hit the network for these paths again.
                                    // Read-and-preserve: never clobber SourceHeadSha /
                                    // ConsecutiveShaCheckFailures with defaults here.
                                    let baseIdx = idxOpt |> Option.defaultValue emptySourceIndex
                                    let mutable idxEntries = baseIdx.Entries
                                    for (resolvedPath, content) in files do
                                        let contentHash = deps.HashContent content
                                        let cacheRelPath =
                                            match deps.CacheSourceContent sourceName contentHash content with
                                            | Ok p    -> Some p
                                            | Error _ -> None
                                        let existing = Map.tryFind resolvedPath idxEntries |> Option.defaultValue emptyIndexEntry
                                        idxEntries <- idxEntries |> Map.add resolvedPath {
                                            existing with
                                                CacheRelPath = cacheRelPath
                                                ContentHash  = Some contentHash
                                        }
                                    deps.WriteSourceIndex sourceName { baseIdx with Entries = idxEntries } |> ignore
                                    files |> Map.ofList
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
                                if hash <> entry.ContentHash then
                                    EDrifted (entry, content)
                                else
                                    let localHash =
                                        match deps.ReadLocalFile entry.LocalPath with
                                        | Ok (Some c) -> Some (deps.HashContent c)
                                        | _           -> None
                                    match localHash with
                                    | Some h when h = entry.ContentHash -> ECurrent entry
                                    | _                                 -> ELocalDrifted (entry, content))

        if opts.DryRun then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = true; Errors = []; Indexes = [] }
        else

        let drifted      = classified |> List.choose (function EDrifted (e, c)      -> Some (e, c) | _ -> None)
        let localDrifted = classified |> List.choose (function ELocalDrifted (e, c) -> Some (e, c) | _ -> None)
        let toWrite      = drifted @ localDrifted

        if toWrite.IsEmpty then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = false; Errors = []; Indexes = [] }
        else

        let writeError =
            toWrite |> List.tryPick (fun (entry, content) ->
                match deps.WriteLocalFile entry.LocalPath content with
                | Error e -> Some e
                | Ok ()   -> None)

        match writeError with
        | Some e -> Error $"Error writing file: {e}"
        | None ->

        if drifted.IsEmpty then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = false; Errors = []; Indexes = [] }
        else

        let updatedEntries =
            entries |> List.map (fun entry ->
                match drifted |> List.tryFind (fun (e, _) -> e.LocalPath = entry.LocalPath) with
                | Some (_, content) -> { entry with ContentHash = deps.HashContent content }
                | None              -> entry)

        match deps.WriteLockEntries eff.StateFile updatedEntries with
        | Error e -> Error $"Error writing lock file: {e}"
        | Ok () -> Ok { Entries = classified |> List.map toSyncEntry; DryRun = false; Errors = []; Indexes = [] }

    // One summary per configured source, read back from the index populateIndex just wrote.
    let summarizeIndexes (deps: Deps) : IndexSummary list =
        let globalCfg = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfg  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        match Config.merge globalCfg localCfg with
        | Error _ -> []
        | Ok eff ->
            eff.Sources
            |> List.map (fun src ->
                { Source  = src.Name
                  Entries = (readIndexOrEmpty deps src.Name).Entries.Count
                  Bundles =
                    src.Bundles
                    |> List.map (fun b ->
                        let kind = match b.Kind with Manifest -> "manifest" | Okf -> "okf"
                        let path = if b.Path = "" then "/" else b.Path
                        $"{kind}:{path}") })

    let execute (deps: Deps) (opts: Options) : Result<SyncResult, string> =
        // Populate index and cache; failures are reported on the result rather than aborting the lock sync.
        let errors = populateIndex deps
        let indexes = summarizeIndexes deps
        executeLocked deps opts |> Result.map (fun r -> { r with Errors = errors; Indexes = indexes })
