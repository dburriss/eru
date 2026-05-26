namespace Eru

module Add =

    type Command = {
        RemotePath     : string option
        Tags           : string list
        SourceName     : string option
        CollectionName : string option
        Target         : string option
    }

    let private parseDiscriminator (value: string) : string option * string =
        match value.IndexOf(':') with
        | -1 -> None, value
        | i  -> Some value.[..i-1], value.[i+1..]

    let private deriveLocalPath (basePath: string option) (target: string option) (remotePath: string) : string =
        let stripped =
            match basePath with
            | None -> remotePath
            | Some bp ->
                let prefix = if bp.EndsWith('/') || bp.EndsWith('\\') then bp else bp + "/"
                if remotePath.StartsWith(prefix) then remotePath.[prefix.Length..]
                else remotePath
        match target with
        | None    -> stripped
        | Some t  ->
            let t' = if t.EndsWith('/') || t.EndsWith('\\') then t else t + "/"
            t' + stripped

    let private findSource (sources: SourceConfig list) (name: string) : Result<SourceConfig, string> =
        sources
        |> List.tryFind (fun s -> s.Name = name)
        |> Option.map Ok
        |> Option.defaultWith (fun () -> Error $"source '{name}' not configured")

    let private pullOne
        (deps: Deps)
        (sources: SourceConfig list)
        (target: string option)
        (sourceName: string)
        (remotePath: string) : Result<LockEntry, string> =
        findSource sources sourceName
        |> Result.bind (fun source ->
            match source.Url with
            | None -> Error $"source '{sourceName}' has no URL"
            | Some url ->
                let branch = source.Branch |> Option.defaultValue "HEAD"
                deps.FetchRemoteContent url branch remotePath
                |> Result.bind (fun content ->
                    let localPath = deriveLocalPath source.BasePath target remotePath
                    let hash = deps.HashContent content
                    deps.WriteLocalFile localPath content
                    |> Result.map (fun () ->
                        { LocalPath = localPath; SourceName = sourceName; RemotePath = remotePath; ContentHash = hash })))

    let private pullMany
        (deps: Deps)
        (sources: SourceConfig list)
        (target: string option)
        (pairs: (string * string) list) : Result<LockEntry list, string> =
        pairs
        |> List.fold (fun acc (sourceName, remotePath) ->
            acc |> Result.bind (fun entries ->
                pullOne deps sources target sourceName remotePath
                |> Result.map (fun e -> entries @ [e])))
            (Ok [])

    let private updateLockEntries (existing: LockEntry list) (newEntries: LockEntry list) : LockEntry list =
        let newPaths = newEntries |> List.map (fun e -> e.LocalPath) |> Set.ofList
        let kept = existing |> List.filter (fun e -> not (Set.contains e.LocalPath newPaths))
        kept @ newEntries

    let run (deps: Deps) (cmd: Command) : int =
        match cmd.RemotePath, cmd.Tags, cmd.CollectionName with
        | None, [], None ->
            eprintfn "Error: specify a remote path, --collection, or at least one --tag."
            1
        | _ ->

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e ->
            eprintfn "Error: %s" e
            1
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e ->
            eprintfn "Error: %s" e
            1
        | Ok eff ->

        let pullResult =
            match cmd.CollectionName with
            | Some rawCol ->
                match globalCfg with
                | None -> Error "no global config found; collections require global config"
                | Some gcfg ->
                    let filterSrc, colName = parseDiscriminator rawCol
                    match gcfg.Collections |> List.tryFind (fun c -> c.Name = colName) with
                    | None -> Error $"collection '{colName}' not found in global config"
                    | Some col ->
                        let files =
                            match filterSrc with
                            | None     -> col.Files
                            | Some src -> col.Files |> List.filter (fun f -> f.Source = src)
                        if files.IsEmpty then
                            match filterSrc with
                            | Some src -> Error $"no files in collection '{colName}' from source '{src}'"
                            | None     -> Error $"collection '{colName}' has no files"
                        else
                            pullMany deps eff.Sources cmd.Target (files |> List.map (fun f -> f.Source, f.RemotePath))

            | None when cmd.Tags <> [] ->
                match globalCfg with
                | None -> Error "no global config found; tag-based pull requires collections in global config"
                | Some gcfg ->
                    let pairs = Config.resolveByTags cmd.Tags gcfg
                    if pairs.IsEmpty then
                        let tagList = cmd.Tags |> String.concat ", "
                        Error $"no files found matching tags: {tagList}"
                    else
                        pullMany deps eff.Sources cmd.Target pairs

            | _ ->
                let rawPath = cmd.RemotePath.Value
                let embeddedSrc, remotePath = parseDiscriminator rawPath
                let srcName =
                    match embeddedSrc with
                    | Some s -> Ok s
                    | None   ->
                        match cmd.SourceName with
                        | Some s -> Ok s
                        | None   ->
                            match eff.Sources with
                            | []    -> Error "no sources configured. Run 'eru source add' first"
                            | s :: _ -> Ok s.Name
                srcName
                |> Result.bind (fun sn ->
                    pullOne deps eff.Sources cmd.Target sn remotePath
                    |> Result.map List.singleton)

        match pullResult with
        | Error e ->
            eprintfn "Error: %s" e
            1
        | Ok entries ->

        match deps.ReadLockEntries eff.StateFile with
        | Error e ->
            eprintfn "Error reading lock file: %s" e
            1
        | Ok existing ->

        match deps.WriteLockEntries eff.StateFile (updateLockEntries existing entries) with
        | Error e ->
            eprintfn "Error writing lock file: %s" e
            1
        | Ok () ->
            match entries with
            | [e] -> printfn "Pulled %s → %s" e.RemotePath e.LocalPath
            | _   -> printfn "Pulled %d file(s)" entries.Length
            0
