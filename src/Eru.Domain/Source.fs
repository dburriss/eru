namespace Eru

module Source =
    type AddCommand = {
        Url      : string
        Name     : string option
        Branch   : string option
        BasePath : string option
        IsGlobal : bool
    }

    let private deriveNameFromUrl (url: string) : string =
        let segment = url.TrimEnd('/').Split([| '/'; ':' |]) |> Array.last
        if segment.EndsWith(".git") then segment.[..segment.Length - 5]
        else segment

    let private detectBasePath (topLevel: string list) : string option =
        topLevel |> List.tryFind (fun e -> e = "KNOWLEDGE" || e = "knowledge")

    let add (deps: Deps) (cmd: AddCommand) : int =
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

        if basePath.IsSome && cmd.BasePath.IsNone then
            printfn $"Detected KNOWLEDGE/ convention — basePath set to \"{basePath.Value}\""

        let newSource : SourceConfig = {
            Name     = name
            Url      = Some cmd.Url
            Branch   = cmd.Branch
            BasePath = basePath
        }

        if cmd.IsGlobal then
            let globalCfg =
                match deps.ReadGlobalConfig () with
                | Ok (Some g) -> Ok g
                | Ok None     -> Ok { Version = 1; DefaultSources = []; Collections = []; Defaults = None }
                | Error e     -> Error e

            match globalCfg with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok g ->
                if g.DefaultSources |> List.exists (fun s -> s.Name = name) then
                    eprintfn $"Error: source '{name}' already exists."
                    1
                else
                    let updated = { g with DefaultSources = g.DefaultSources @ [newSource] }
                    match deps.WriteGlobalConfig updated with
                    | Ok () ->
                        let branch = cmd.Branch |> Option.defaultValue "HEAD"
                        match deps.FetchRemoteContent cmd.Url branch ".eru/manifest.json" with
                        | Ok ((_, raw) :: _) -> deps.CacheSourceManifest name raw |> ignore
                        | _ -> ()
                        printfn $"Added source '{name}' to global config."
                        0
                    | Error e ->
                        eprintfn $"Error: {e}"
                        1
        else
            match deps.ReadLocalConfig () with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok None ->
                eprintfn "Error: no .eru/config.json found. Run 'eru init' first."
                1
            | Ok (Some local) ->
                if local.Sources |> List.exists (fun s -> s.Name = name) then
                    eprintfn $"Error: source '{name}' already exists."
                    1
                else
                    let updated = { local with Sources = local.Sources @ [newSource] }
                    match deps.WriteLocalConfig updated with
                    | Ok () ->
                        let branch = cmd.Branch |> Option.defaultValue "HEAD"
                        match deps.FetchRemoteContent cmd.Url branch ".eru/manifest.json" with
                        | Ok ((_, raw) :: _) -> deps.CacheSourceManifest name raw |> ignore
                        | _ -> ()
                        printfn $"Added source '{name}' to .eru/config.json."
                        0
                    | Error e ->
                        eprintfn $"Error: {e}"
                        1
