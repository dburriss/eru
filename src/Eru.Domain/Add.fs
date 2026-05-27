namespace Eru

module Add =

    type Command = {
        RemotePath     : string option
        Tags           : string list
        SourceName     : string option
        CollectionName : string option
        Target         : string option
        DryRun         : bool
        IsGlobal       : bool
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

    let private resolveRemotePath (source: SourceConfig) (remotePath: string) : string =
        let isGlob = remotePath.Contains('*') || remotePath.Contains('?')
        let isBare = not (remotePath.Contains('/'))
        let withPrefix =
            if isBare then
                match source.BasePath with
                | None -> remotePath
                | Some bp ->
                    let prefix = if bp.EndsWith('/') then bp else bp + "/"
                    if remotePath.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) then remotePath
                    else prefix + remotePath
            else remotePath
        if isGlob || withPrefix.Contains('.') then withPrefix
        else withPrefix + ".md"

    let private findSource (sources: SourceConfig list) (name: string) : Result<SourceConfig, string> =
        sources
        |> List.tryFind (fun s -> s.Name = name)
        |> Option.map Ok
        |> Option.defaultWith (fun () -> Error $"source '{name}' not configured")

    let private pullOne
        (deps: Deps)
        (sources: SourceConfig list)
        (target: string option)
        (dryRun: bool)
        (sourceName: string)
        (remotePath: string) : Result<LockEntry list, string> =
        findSource sources sourceName
        |> Result.bind (fun source ->
            match source.Url with
            | None -> Error $"source '{sourceName}' has no URL"
            | Some url ->
                let branch = source.Branch |> Option.defaultValue "HEAD"
                deps.FetchRemoteContent url branch remotePath
                |> Result.bind (fun files ->
                    files
                    |> List.fold (fun acc (resolvedPath, content) ->
                        acc |> Result.bind (fun entries ->
                            let localPath = deriveLocalPath source.BasePath target resolvedPath
                            let hash = deps.HashContent content
                            if dryRun then
                                Ok (entries @ [{ LocalPath = localPath; SourceName = sourceName; RemotePath = resolvedPath; ContentHash = hash }])
                            else
                                deps.WriteLocalFile localPath content
                                |> Result.map (fun () ->
                                    entries @ [{ LocalPath = localPath; SourceName = sourceName; RemotePath = resolvedPath; ContentHash = hash }])))
                        (Ok [])))

    let private pullMany
        (deps: Deps)
        (sources: SourceConfig list)
        (target: string option)
        (dryRun: bool)
        (pairs: (string * string) list) : Result<LockEntry list, string> =
        pairs
        |> List.fold (fun acc (sourceName, remotePath) ->
            acc |> Result.bind (fun entries ->
                pullOne deps sources target dryRun sourceName remotePath
                |> Result.map (fun newEntries -> entries @ newEntries)))
            (Ok [])

    let private updateLockEntries (existing: LockEntry list) (newEntries: LockEntry list) : LockEntry list =
        let newPaths = newEntries |> List.map (fun e -> e.LocalPath) |> Set.ofList
        let kept = existing |> List.filter (fun e -> not (Set.contains e.LocalPath newPaths))
        kept @ newEntries

    let private detectBasePath (topLevel: string list) : string option =
        topLevel |> List.tryFind (fun e -> e = "KNOWLEDGE" || e = "knowledge")

    let private ensureSource
        (deps: Deps)
        (isGlobal: bool)
        (effSources: SourceConfig list)
        (globalCfg: GlobalConfig option)
        (localCfg: LocalConfig option)
        (parsed: UrlParser.ParsedProviderUrl)
        : Result<SourceConfig list, string> =
        match effSources |> List.tryFind (fun s -> s.Name = parsed.SourceName) with
        | Some existing ->
            match existing.Url with
            | Some url when url = parsed.RepoUrl -> Ok effSources
            | Some url -> Error $"source '{parsed.SourceName}' already exists pointing to '{url}', not '{parsed.RepoUrl}'"
            | None -> Error $"source '{parsed.SourceName}' already exists without a URL"
        | None ->
            let basePath =
                match deps.ListRemoteTopLevel parsed.RepoUrl (Some parsed.Branch) with
                | Ok entries -> detectBasePath entries
                | Error _    -> None
            let newSource : SourceConfig = {
                Name     = parsed.SourceName
                Url      = Some parsed.RepoUrl
                Branch   = Some parsed.Branch
                BasePath = basePath
            }
            if isGlobal then
                let g = globalCfg |> Option.defaultValue { Version = 1; DefaultSources = []; Collections = []; Defaults = None }
                let updated = { g with DefaultSources = g.DefaultSources @ [newSource] }
                deps.WriteGlobalConfig updated
                |> Result.map (fun () -> effSources @ [newSource])
            else
                match localCfg with
                | None -> Error "no eru.json found. Run 'eru init' first."
                | Some local ->
                    let updated = { local with Sources = local.Sources @ [newSource] }
                    deps.WriteLocalConfig updated
                    |> Result.map (fun () -> effSources @ [newSource])

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
                            pullMany deps eff.Sources cmd.Target cmd.DryRun (files |> List.map (fun f -> f.Source, f.RemotePath))

            | None when cmd.Tags <> [] ->
                match globalCfg with
                | None -> Error "no global config found; tag-based pull requires collections in global config"
                | Some gcfg ->
                    let pairs = Config.resolveByTags cmd.Tags gcfg
                    if pairs.IsEmpty then
                        let tagList = cmd.Tags |> String.concat ", "
                        Error $"no files found matching tags: {tagList}"
                    else
                        pullMany deps eff.Sources cmd.Target cmd.DryRun pairs

            | _ ->
                let rawPath = cmd.RemotePath.Value
                match UrlParser.tryParse rawPath with
                | Some parsed ->
                    ensureSource deps cmd.IsGlobal eff.Sources globalCfg localCfg parsed
                    |> Result.bind (fun updatedSources ->
                        pullOne deps updatedSources cmd.Target cmd.DryRun parsed.SourceName parsed.RemotePath)
                | None ->
                    if rawPath.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) then
                        Error "unsupported URL provider; supported providers: GitHub (https://github.com/...), GitLab (https://gitlab.com/...)"
                    else
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
                            findSource eff.Sources sn
                            |> Result.bind (fun source ->
                                let expandedPath = resolveRemotePath source remotePath
                                pullOne deps eff.Sources cmd.Target cmd.DryRun sn expandedPath))

        match pullResult with
        | Error e ->
            eprintfn "Error: %s" e
            1
        | Ok entries ->

        if cmd.DryRun then
            match entries with
            | [e] -> printfn "Would pull %s → %s" e.RemotePath e.LocalPath
            | _   -> printfn "Would pull %d file(s)" entries.Length
            0
        else

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
