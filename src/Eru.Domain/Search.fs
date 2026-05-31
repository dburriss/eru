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

    let private mergeResults (collResults: SearchResult list) (lockResults: SearchResult list) =
        let lockMap =
            lockResults
            |> List.map (fun r -> (r.SourceName, r.RemotePath), r.LocalPath)
            |> Map.ofList
        let enriched =
            collResults
            |> List.map (fun r ->
                match Map.tryFind (r.SourceName, r.RemotePath) lockMap with
                | Some lp -> { r with LocalPath = lp }
                | None    -> r)
        let collSet = collResults |> List.map (fun r -> (r.SourceName, r.RemotePath)) |> Set.ofList
        let lockOnly = lockResults |> List.filter (fun r -> not (Set.contains (r.SourceName, r.RemotePath) collSet))
        enriched @ lockOnly

    let execute (deps: Deps) (query: Query) : Result<SearchResult list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        let collectionResults =
            globalCfg
            |> Option.map (fun gcfg ->
                gcfg.Collections
                |> List.collect (fun col ->
                    col.Files
                    |> List.map (fun f -> {
                        SourceName  = f.Source
                        RemotePath  = f.RemotePath
                        Tags        = (f.Tags @ col.Tags) |> List.distinct
                        Description = f.Description |> Option.orElse col.Description
                        LocalPath   = None
                    })))
            |> Option.defaultValue []

        let lockResults =
            match deps.ReadLockEntries eff.StateFile with
            | Error _ -> []
            | Ok entries ->
                entries
                |> List.map (fun e -> {
                    SourceName  = e.SourceName
                    RemotePath  = e.RemotePath
                    Tags        = []
                    Description = None
                    LocalPath   = Some e.LocalPath
                })

        let allResults = mergeResults collectionResults lockResults

        let filtered =
            allResults
            |> List.filter (matchesTags query.Tags)
            |> List.filter (matchesTerm query.Terms)

        Ok filtered
