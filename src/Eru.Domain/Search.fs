namespace Eru

module Search =

    type Query = {
        Terms : string list
        Tags  : string list
    }

    type SearchResult = {
        SourceName  : string
        RemotePath  : string
        Tags        : string list
        Description : string option
        LocalPath   : string option
    }

    let private matchesTerm (terms: string list) (r: SearchResult) =
        terms = [] ||
        terms |> List.exists (fun t ->
            let t' = t.ToLowerInvariant()
            r.RemotePath.ToLowerInvariant().Contains(t') ||
            (r.LocalPath   |> Option.exists (fun lp -> lp.ToLowerInvariant().Contains(t'))) ||
            (r.Description |> Option.exists (fun d  -> d.ToLowerInvariant().Contains(t'))))

    let private matchesTags (tags: string list) (r: SearchResult) =
        tags = [] ||
        let normalised = tags |> List.map (fun t -> t.ToLowerInvariant())
        normalised |> List.forall (fun qt ->
            r.Tags |> List.exists (fun rt -> rt.ToLowerInvariant() = qt))

    let execute (deps: Deps) (query: Query) : Result<SearchResult list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        // Build collection tags map for enriching index results at search time
        // (collection-side tags are NOT stored in the index — joined here)
        let globalCols = globalCfg |> Option.map (fun g -> g.Collections) |> Option.defaultValue []
        let localCols  = localCfg  |> Option.map (fun l -> l.Collections) |> Option.defaultValue []
        let collectionTagsMap =
            (globalCols @ localCols)
            |> List.collect (fun col ->
                col.Files |> List.map (fun f ->
                    (f.Source, f.RemotePath),
                    (f.Tags @ col.Tags) |> List.distinct,
                    f.Description |> Option.orElse col.Description))
            |> List.map (fun (key, tags, desc) -> key, (tags, desc))
            |> Map.ofList

        // Lock file map for LocalPath resolution
        let lockMap =
            match deps.ReadLockEntries eff.StateFile with
            | Ok entries ->
                entries |> List.map (fun e -> (e.SourceName, e.RemotePath), e) |> Map.ofList
            | Error _ -> Map.empty

        // Index-based results (enriched with collection tags and lock LocalPath)
        let indexResults =
            eff.Sources
            |> List.collect (fun src ->
                match deps.ReadSourceIndex src.Name with
                | Ok (Some idx) ->
                    idx |> Map.toList |> List.map (fun (remotePath, entry) ->
                        let (colTags, colDesc) =
                            collectionTagsMap
                            |> Map.tryFind (src.Name, remotePath)
                            |> Option.defaultValue ([], None)
                        let allTags = (entry.Tags @ colTags) |> List.distinct
                        let lockEntry = lockMap |> Map.tryFind (src.Name, remotePath)
                        let localPath =
                            entry.LocalPath
                            |> Option.orElse (lockEntry |> Option.map (fun e -> e.LocalPath))
                        {
                            SourceName  = src.Name
                            RemotePath  = remotePath
                            Tags        = allTags
                            Description = entry.Description |> Option.orElse colDesc
                            LocalPath   = localPath
                        })
                | _ -> [])

        let indexedKeys =
            indexResults |> List.map (fun r -> (r.SourceName, r.RemotePath)) |> Set.ofList

        // Config-based results for files not covered by any source index
        let configResults =
            (globalCols @ localCols)
            |> List.collect (fun col ->
                col.Files
                |> List.map (fun f ->
                    let lockEntry = lockMap |> Map.tryFind (f.Source, f.RemotePath)
                    {
                        SourceName  = f.Source
                        RemotePath  = f.RemotePath
                        Tags        = (f.Tags @ col.Tags) |> List.distinct
                        Description = f.Description |> Option.orElse col.Description
                        LocalPath   = lockEntry |> Option.map (fun e -> e.LocalPath)
                    }))
            |> List.filter (fun r -> not (Set.contains (r.SourceName, r.RemotePath) indexedKeys))

        let coveredKeys =
            Set.union indexedKeys
                (configResults |> List.map (fun r -> (r.SourceName, r.RemotePath)) |> Set.ofList)

        // Lock-only results: lock entries not in index or config
        let lockOnlyResults =
            lockMap |> Map.toList
            |> List.filter (fun ((sn, rp), _) -> not (Set.contains (sn, rp) coveredKeys))
            |> List.map (fun ((sn, rp), e) -> {
                SourceName  = sn
                RemotePath  = rp
                Tags        = e.Tags |> List.map (fun t -> t.ToLowerInvariant())
                Description = e.Description
                LocalPath   = Some e.LocalPath
            })

        let allResults = indexResults @ configResults @ lockOnlyResults

        let filtered =
            allResults
            |> List.distinctBy (fun r -> (r.SourceName, r.RemotePath))
            |> List.filter (matchesTags query.Tags)
            |> List.filter (matchesTerm query.Terms)

        Ok filtered
