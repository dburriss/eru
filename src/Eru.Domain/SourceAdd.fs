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

    let private detectBundles (deps: Deps) (url: string) (branch: string option) : Bundle list =
        let candidate =
            match deps.ListRemoteTopLevel url branch with
            | Ok entries -> BundleDetect.candidatePath entries
            | Error _    -> None
        match candidate with
        | None -> []
        | Some cp ->
            let actualBranch = branch |> Option.defaultValue "HEAD"
            let indexPath = if cp = "" then "index.md" else $"{cp}/index.md"
            let hasOkf =
                match deps.FetchRemoteContent url actualBranch [indexPath] with
                | Ok ((_, content) :: _) ->
                    Frontmatter.parse deps.ParseYamlBlock content |> Frontmatter.okfVersion |> Option.isSome
                | _ -> false
            [ { Path = cp; Kind = BundleDetect.detectKind hasOkf } ]

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        let name = cmd.Name |> Option.defaultWith (fun () -> deriveNameFromUrl cmd.Url)

        let bundles =
            match cmd.BasePath with
            | Some bp -> [ { Path = bp; Kind = Manifest } ]
            | None    -> detectBundles deps cmd.Url cmd.Branch

        let newSource : SourceConfig = {
            Name     = name
            Url      = Some cmd.Url
            Branch   = cmd.Branch
            Bundles  = bundles
        }

        let cacheManifest () =
            let branch = cmd.Branch |> Option.defaultValue "HEAD"
            match deps.FetchRemoteContent cmd.Url branch [".eru/manifest.json"] with
            | Ok ((_, raw) :: _) -> deps.CacheSourceManifest name raw |> ignore
            | _ -> ()

        let detectionNote =
            match bundles, cmd.BasePath with
            | [ b ], None ->
                let kindStr = match b.Kind with Manifest -> "manifest" | Okf -> "okf"
                let warning =
                    match b.Kind with
                    | Manifest ->
                        BundleKindWarning.noManifestWarning deps cmd.Url cmd.Branch b.Path
                        |> Option.map (fun w -> "\n" + w)
                        |> Option.defaultValue ""
                    | Okf -> ""
                $"\nDetected KNOWLEDGE/ convention — bundle at \"{b.Path}\" (kind: {kindStr}){warning}"
            | _ -> ""

        if cmd.IsGlobal then
            let globalCfg =
                match deps.ReadGlobalConfig () with
                | Ok (Some g) -> Ok g
                | Ok None     -> Ok { Version = 1; DefaultSources = []; Collections = []; DefaultInboxes = Map.empty; Defaults = None }
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
