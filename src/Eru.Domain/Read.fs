namespace Eru

module Read =

    type Document = {
        Source     : string
        RemotePath : string
        Hash       : string
        Content    : string
    }

    // Resolves 'source:path' or a path short hash (3-8 hex chars) to an EntryId.
    // Hashes are matched against every source's index, so they can be ambiguous across sources.
    let resolveTarget (deps: Deps) (eff: EffectiveConfig) (target: string) : Result<EntryId, string> =
        match EntryId.tryParse target with
        | Some id when id.Source <> "" ->
            if eff.Sources |> List.exists (fun s -> s.Name = id.Source) then Ok id
            else Error $"unknown source '{id.Source}'"
        | _ ->
        let prefix = target.ToLowerInvariant()
        if not (Patterns.isShortHash prefix) then
            Error $"'{target}' is neither 'source:path' nor a path hash (3-8 hex characters)."
        else
            let matches =
                eff.Sources
                |> List.collect (fun src ->
                    match deps.ReadSourceIndex src.Name with
                    | Ok (Some idx) ->
                        idx.Entries
                        |> Map.toList
                        |> List.filter (fun (remotePath, _) -> (Patterns.pathShortHash remotePath).StartsWith prefix)
                        |> List.map (fun (remotePath, _) -> { Source = src.Name; RemotePath = remotePath })
                    | _ -> [])
            match matches with
            | []   -> Error $"no file found for hash prefix '{prefix}'"
            | [id] -> Ok id
            | many ->
                let candidates = many |> List.map EntryId.toString |> String.concat ", "
                Error $"ambiguous short hash '{prefix}' — {many.Length} files match, be more specific: {candidates}"

    // Reads content in order: local (added) file, source cache, live fetch from the remote.
    let readContent (deps: Deps) (eff: EffectiveConfig) (id: EntryId) : Result<string, string> =
        let indexEntry =
            match deps.ReadSourceIndex id.Source with
            | Ok (Some idx) -> Map.tryFind id.RemotePath idx.Entries
            | _ -> None

        let lockLocalPath =
            match deps.ReadLockEntries eff.StateFile with
            | Ok entries ->
                entries
                |> List.tryFind (fun e -> e.SourceName = id.Source && e.RemotePath = id.RemotePath)
                |> Option.map (fun e -> e.LocalPath)
            | Error _ -> None

        let tryLocal () =
            match indexEntry |> Option.bind (fun e -> e.LocalPath) |> Option.orElse lockLocalPath with
            | None -> Ok None
            | Some lp -> deps.ReadLocalFile (PathJoin.Combine(deps.GetCwd(), lp))

        let tryCache () =
            match indexEntry |> Option.bind (fun e -> e.CacheRelPath) with
            | None -> Ok None
            | Some rel -> deps.ReadCachedSourceContent id.Source rel

        let fetchRemote () =
            match eff.Sources |> List.tryFind (fun s -> s.Name = id.Source) with
            | None -> Error $"unknown source '{id.Source}'"
            | Some src ->
            match src.Url with
            | None -> Error $"source '{id.Source}' has no URL configured"
            | Some url ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch [id.RemotePath] with
                | Ok ((_, content) :: _) ->
                    if Patterns.isBlocked eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries id.RemotePath content then
                        Error $"'{id.RemotePath}' is blocked by the current block patterns"
                    else Ok content
                | Ok []   -> Error $"no content returned for {EntryId.toString id}"
                | Error e -> Error $"fetching {EntryId.toString id}: {e}"

        let cached =
            [ tryLocal; tryCache ]
            |> List.tryPick (fun attempt -> match attempt () with Ok (Some c) -> Some c | _ -> None)
        match cached with
        | Some content -> Ok content
        | None -> fetchRemote ()

    let execute (deps: Deps) (target: string) : Result<Document, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->
        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->
        match resolveTarget deps eff target with
        | Error e -> Error e
        | Ok id ->
        match readContent deps eff id with
        | Error e -> Error e
        | Ok content ->
            Ok { Source = id.Source; RemotePath = id.RemotePath; Hash = Patterns.pathShortHash id.RemotePath; Content = content }
