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

    type PullEntry =
        | Pulled  of LockEntry
        | Blocked of string

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
        | None   -> stripped
        | Some t ->
            if t.EndsWith('/') || t.EndsWith('\\') then t + System.IO.Path.GetFileName stripped
            else t

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

    let private isShortHash (s: string) =
        s.Length >= 3 && s.Length <= 8 && s |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

    let private resolveShortHash
        (deps: Deps) (source: SourceConfig) (prefix: string) : Result<string, string> =
        match source.Url with
        | None -> Error $"source '{source.Name}' has no URL"
        | Some url ->
            match deps.ListRemoteFiles url source.Branch source.BasePath with
            | Error e -> Error e
            | Ok paths ->
                let matches = paths |> List.filter (fun p -> (Patterns.pathShortHash p).StartsWith prefix)
                match matches with
                | []  -> Error $"no file found for hash prefix '{prefix}'"
                | [p] -> Ok p
                | _   -> Error $"ambiguous short hash '{prefix}' — {matches.Length} files match, be more specific"

    let private resolveShortHashAcrossSources
        (deps: Deps) (sources: SourceConfig list) (prefix: string) : Result<string * string, string> =
        let matches =
            sources
            |> List.choose (fun source ->
                match resolveShortHash deps source prefix with
                | Ok path -> Some (source.Name, path)
                | Error _ -> None)
        match matches with
        | []        -> Error $"no file found for hash prefix '{prefix}'"
        | [(sn, p)] -> Ok (sn, p)
        | many      ->
            let sourceList = many |> List.map fst |> String.concat ", "
            Error $"ambiguous short hash '{prefix}' — found in multiple sources: {sourceList}"

    let private pullOne
        (deps: Deps)
        (sources: SourceConfig list)
        (target: string option)
        (dryRun: bool)
        (blockPatterns: string list)
        (allowPatterns: string list)
        (allowBinaries: bool)
        (sourceName: string)
        (remotePath: string) : Result<PullEntry list, string> =
        findSource sources sourceName
        |> Result.bind (fun source ->
            (if isShortHash remotePath then resolveShortHash deps source remotePath
             else Ok remotePath)
            |> Result.bind (fun actualPath ->
            match source.Url with
            | None -> Error $"source '{sourceName}' has no URL"
            | Some url ->
                let branch = source.Branch |> Option.defaultValue "HEAD"
                deps.FetchRemoteContent url branch [actualPath]
                |> Result.bind (fun files ->
                    let allowed, blocked =
                        files |> List.partition (fun (path, content) ->
                            not (Patterns.isBlocked blockPatterns allowPatterns allowBinaries path content))
                    let blockedEntries = blocked |> List.map (fun (path, _) -> Blocked path)
                    allowed
                    |> List.fold (fun acc (resolvedPath, content) ->
                        acc |> Result.bind (fun entries ->
                            let localPath = deriveLocalPath source.BasePath target resolvedPath
                            let hash = deps.HashContent content
                            if dryRun then
                                Ok (entries @ [Pulled { LocalPath = localPath; SourceName = sourceName; RemotePath = resolvedPath; ContentHash = hash }])
                            else
                                deps.WriteLocalFile localPath content
                                |> Result.map (fun () ->
                                    entries @ [Pulled { LocalPath = localPath; SourceName = sourceName; RemotePath = resolvedPath; ContentHash = hash }])))
                        (Ok blockedEntries))))

    let private pullMany
        (deps: Deps)
        (sources: SourceConfig list)
        (target: string option)
        (dryRun: bool)
        (blockPatterns: string list)
        (allowPatterns: string list)
        (allowBinaries: bool)
        (pairs: (string * string) list) : Result<PullEntry list, string> =
        pairs
        |> List.fold (fun acc (sourceName, remotePath) ->
            acc |> Result.bind (fun entries ->
                pullOne deps sources target dryRun blockPatterns allowPatterns allowBinaries sourceName remotePath
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
                | None -> Error "no .eru/config.json found. Run 'eru init' first."
                | Some local ->
                    let updated = { local with Sources = local.Sources @ [newSource] }
                    deps.WriteLocalConfig updated
                    |> Result.map (fun () -> effSources @ [newSource])

    let execute (deps: Deps) (cmd: Command) : Result<PullEntry list, string> =
        match cmd.RemotePath, cmd.Tags, cmd.CollectionName with
        | None, [], None -> Error "specify a remote path, --collection, or at least one --tag."
        | _ ->

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
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
                            pullMany deps eff.Sources cmd.Target cmd.DryRun eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries (files |> List.map (fun f -> f.Source, f.RemotePath))

            | None when cmd.Tags <> [] ->
                match globalCfg with
                | None -> Error "no global config found; tag-based pull requires collections in global config"
                | Some gcfg ->
                    let pairs = Config.resolveByTags cmd.Tags gcfg
                    if pairs.IsEmpty then
                        let tagList = cmd.Tags |> String.concat ", "
                        Error $"no files found matching tags: {tagList}"
                    else
                        pullMany deps eff.Sources cmd.Target cmd.DryRun eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries pairs

            | _ ->
                let rawPath = cmd.RemotePath.Value
                match UrlParser.tryParse rawPath with
                | Some parsed ->
                    ensureSource deps cmd.IsGlobal eff.Sources globalCfg localCfg parsed
                    |> Result.bind (fun updatedSources ->
                        pullOne deps updatedSources cmd.Target cmd.DryRun eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries parsed.SourceName parsed.RemotePath)
                | None ->
                    if rawPath.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) then
                        Error "unsupported URL provider; supported providers: GitHub (https://github.com/...), GitLab (https://gitlab.com/...)"
                    else
                        let embeddedSrc, remotePath = parseDiscriminator rawPath
                        let noSourceSpecified = embeddedSrc.IsNone && cmd.SourceName.IsNone
                        if noSourceSpecified && isShortHash remotePath then
                            if eff.Sources.IsEmpty then
                                Error "no sources configured. Run 'eru source add' first"
                            else
                                resolveShortHashAcrossSources deps eff.Sources remotePath
                                |> Result.bind (fun (sn, resolvedPath) ->
                                    pullOne deps eff.Sources cmd.Target cmd.DryRun eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries sn resolvedPath)
                        else
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
                                let expandedPath =
                                    if isShortHash remotePath then remotePath
                                    else resolveRemotePath source remotePath
                                pullOne deps eff.Sources cmd.Target cmd.DryRun eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries sn expandedPath))

        match pullResult with
        | Error e -> Error e
        | Ok pullEntries ->

        if cmd.DryRun then Ok pullEntries
        else

        let lockEntries = pullEntries |> List.choose (function Pulled e -> Some e | Blocked _ -> None)

        match deps.ReadLockEntries eff.StateFile with
        | Error e -> Error $"Error reading lock file: {e}"
        | Ok existing ->

        match deps.WriteLockEntries eff.StateFile (updateLockEntries existing lockEntries) with
        | Error e -> Error $"Error writing lock file: {e}"
        | Ok () -> Ok pullEntries
