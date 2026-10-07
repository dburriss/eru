namespace Eru

module SourceAdd =

    type Command = {
        Url      : string
        Name     : string option
        Branch   : string option
        BasePath : string option
        Scan     : bool
        Nested   : bool
        IsGlobal : bool
        DryRun   : bool
    }

    let private deriveNameFromUrl (url: string) : string =
        let segment = url.TrimEnd('/').Split([| '/'; ':' |]) |> Array.last
        if segment.EndsWith(".git") then segment.[..segment.Length - 5]
        else segment

    // Fallback: the KNOWLEDGE/ (or knowledge/) top-level folder convention.
    let private detectConventionBundle (deps: Deps) (url: string) (branch: string option) : Bundle list =
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

    // Directory part of a repo-relative `.../index.md` path ("" for the root).
    let private indexDir (indexPath: string) : string =
        match indexPath.LastIndexOf '/' with
        | -1 -> ""
        | i  -> indexPath.Substring(0, i)

    // --scan: every index.md carrying okf_version marks an okf bundle. A root bundle
    // covers its descendants, so nested ones are dropped unless `nested` is set. Falls back to the
    // knowledge/ convention when no okf index is found.
    // With `nested`, every okf bundle is kept, even under a covering root bundle.
    let private scanBundles (deps: Deps) (url: string) (branch: string option) (nested: bool) : Bundle list =
        let actualBranch = branch |> Option.defaultValue "HEAD"
        let indexPaths =
            match deps.ListRemoteFiles url branch None with
            | Ok files ->
                files
                |> List.filter (fun p -> p = "index.md" || p.EndsWith "/index.md")
                |> List.filter (fun p -> not (p.Split('/') |> Array.exists (fun seg -> seg.StartsWith ".")))
            | Error _ -> []
        let okfDirs =
            match indexPaths with
            | [] -> []
            | _ ->
                match deps.FetchRemoteContent url actualBranch indexPaths with
                | Ok files ->
                    files
                    |> List.filter (fun (_, content) ->
                        Frontmatter.parse deps.ParseYamlBlock content |> Frontmatter.okfVersion |> Option.isSome)
                    |> List.map (fst >> indexDir)
                    |> List.sort
                | Error _ -> []
        let rec dropNested (dirs: string list) (kept: string list) =
            match dirs with
            | [] -> List.rev kept
            | d :: rest ->
                let covered = kept |> List.exists (fun k -> k = "" || d = k || d.StartsWith(k + "/"))
                dropNested rest (if covered then kept else d :: kept)
        match (if nested then okfDirs else dropNested okfDirs []) with
        | [] -> detectConventionBundle deps url branch
        | dirs -> dirs |> List.map (fun d -> { Path = d; Kind = Okf })

    let private executeValid (deps: Deps) (cmd: Command) : Result<string, string> =
        let name = cmd.Name |> Option.defaultWith (fun () -> deriveNameFromUrl cmd.Url)

        let bundles =
            match cmd.BasePath with
            | Some bp -> [ { Path = bp; Kind = Manifest } ]
            | None when cmd.Scan -> scanBundles deps cmd.Url cmd.Branch cmd.Nested
            | None    -> []

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
            | [], None -> "\nNo bundles registered. Use --scan to detect them, or 'eru source bundle add'."
            | _, None ->
                bundles
                |> List.map (fun b ->
                    let kindStr = match b.Kind with Manifest -> "manifest" | Okf -> "okf"
                    let warning =
                        match b.Kind with
                        | Manifest ->
                            BundleKindWarning.noManifestWarning deps cmd.Url cmd.Branch b.Path
                            |> Option.map (fun w -> "\n" + w)
                            |> Option.defaultValue ""
                        | Okf -> ""
                    let shown = if b.Path = "" then "(root)" else b.Path
                    $"\nDetected bundle at \"{shown}\" (kind: {kindStr}){warning}")
                |> String.concat ""
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

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        if cmd.Nested && not cmd.Scan then Error "--nested only applies together with --scan."
        else executeValid deps cmd
