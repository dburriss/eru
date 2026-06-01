namespace Eru

module SourceAdd =

    type Command = {
        Url      : string
        Name     : string option
        Branch   : string option
        BasePath : string option
        IsGlobal : bool
        DryRun   : bool
    }

    let private deriveNameFromUrl (url: string) : string =
        let segment = url.TrimEnd('/').Split([| '/'; ':' |]) |> Array.last
        if segment.EndsWith(".git") then segment.[..segment.Length - 5]
        else segment

    let private detectBasePath (topLevel: string list) : string option =
        topLevel |> List.tryFind (fun e -> e = "KNOWLEDGE" || e = "knowledge")

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        let name = cmd.Name |> Option.defaultWith (fun () -> deriveNameFromUrl cmd.Url)

        let basePath =
            match cmd.BasePath with
            | Some _ -> cmd.BasePath
            | None   ->
                let topLevel =
                    match deps.ListRemoteTopLevel cmd.Url cmd.Branch with
                    | Ok entries -> entries
                    | Error _    -> []
                detectBasePath topLevel

        let newSource : SourceConfig = {
            Name     = name
            Url      = Some cmd.Url
            Branch   = cmd.Branch
            BasePath = basePath
        }

        let cacheManifest () =
            let branch = cmd.Branch |> Option.defaultValue "HEAD"
            match deps.FetchRemoteContent cmd.Url branch [".eru/manifest.json"] with
            | Ok ((_, raw) :: _) -> deps.CacheSourceManifest name raw |> ignore
            | _ -> ()

        let detectionNote =
            if basePath.IsSome && cmd.BasePath.IsNone then
                $"\nDetected KNOWLEDGE/ convention — basePath set to \"{basePath.Value}\""
            else ""

        if cmd.IsGlobal then
            let globalCfg =
                match deps.ReadGlobalConfig () with
                | Ok (Some g) -> Ok g
                | Ok None     -> Ok { Version = 1; DefaultSources = []; Collections = []; Defaults = None }
                | Error e     -> Error e
            match globalCfg with
            | Error e -> Error e
            | Ok g ->
                if g.DefaultSources |> List.exists (fun s -> s.Name = name) then
                    Error $"source '{name}' already exists."
                elif cmd.DryRun then
                    Ok $"Would add source '{name}' to global config.{detectionNote}"
                else
                    let updated = { g with DefaultSources = g.DefaultSources @ [newSource] }
                    match deps.WriteGlobalConfig updated with
                    | Error e -> Error e
                    | Ok () ->
                        cacheManifest ()
                        Ok $"Added source '{name}' to global config.{detectionNote}"
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                if local.Sources |> List.exists (fun s -> s.Name = name) then
                    Error $"source '{name}' already exists."
                elif cmd.DryRun then
                    Ok $"Would add source '{name}' to .eru/config.json.{detectionNote}"
                else
                    let updated = { local with Sources = local.Sources @ [newSource] }
                    match deps.WriteLocalConfig updated with
                    | Error e -> Error e
                    | Ok () ->
                        cacheManifest ()
                        Ok $"Added source '{name}' to .eru/config.json.{detectionNote}"
