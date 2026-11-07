namespace Eru

module SourceAdd =

    type Command = {
        Url      : string
        Name     : string option
        Branch   : string option
        BasePath : string option
        Scan     : bool
        Nested   : bool
        Exclude  : string list
        IsGlobal : bool
        DryRun   : bool
    }

    let private deriveNameFromUrl (url: string) : string =
        let segment = url.TrimEnd('/', '\\').Split([| '/'; '\\'; ':' |]) |> Array.last
        if segment.EndsWith(".git") then segment.[..segment.Length - 5]
        else segment

    // How bundle detection reads a source: through git for a remote/repo source, straight from disk for a folder.
    type private Access = {
        TopLevel : unit -> Result<string list, string>
        Files    : unit -> Result<string list, string>              // every file path, source-root-relative
        Fetch    : string list -> Result<(string * string) list, string>
        // Folder sources are OKF-only, so a convention folder without an okf index is not registered.
        OkfOnly  : bool
    }

    let private gitAccess (deps: Deps) (url: string) (branch: string option) : Access =
        { TopLevel = fun () -> deps.ListRemoteTopLevel url branch
          Files    = fun () -> deps.ListRemoteFiles url branch None
          Fetch    = deps.FetchRemoteContent url (branch |> Option.defaultValue "HEAD")
          OkfOnly  = false }

    let private folderAccess (deps: Deps) (root: string) : Access =
        { TopLevel = fun () ->
            let dirs = deps.ListLocalDirectories root |> Result.map (List.map PathUtil.fileName)
            let files = deps.ListLocalFiles root |> Result.map (List.map PathUtil.fileName)
            match dirs, files with
            | Ok d, Ok f -> Ok (d @ f)
            | Error e, _ | _, Error e -> Error e
          Files = fun () ->
            deps.ScanLocalFolder root "" Config.defaultOkfIgnorePatterns |> Result.map (List.map fst)
          Fetch = fun paths ->
            let read p =
                match deps.ReadLocalFile (PathJoin.Combine(root, p)) with
                | Ok (Some c) -> Some (p, c)
                | _ -> None
            Ok (paths |> List.choose read)
          OkfOnly = true }

    // Fallback: the KNOWLEDGE/ (or knowledge/) top-level folder convention.
    let private detectConventionBundle (deps: Deps) (access: Access) : Bundle list =
        let candidate =
            match access.TopLevel () with
            | Ok entries -> BundleDetect.candidatePath entries
            | Error _    -> None
        match candidate with
        | None -> []
        | Some cp ->
            let indexPath = if cp = "" then "index.md" else $"{cp}/index.md"
            let hasOkf =
                match access.Fetch [indexPath] with
                | Ok ((_, content) :: _) ->
                    Frontmatter.parse deps.ParseYamlBlock content |> Frontmatter.okfVersion |> Option.isSome
                | _ -> false
            if access.OkfOnly && not hasOkf then []
            else [ { Path = cp; Kind = BundleDetect.detectKind hasOkf } ]

    // Directory part of a repo-relative `.../index.md` path ("" for the root).
    let private indexDir (indexPath: string) : string =
        match indexPath.LastIndexOf '/' with
        | -1 -> ""
        | i  -> indexPath.Substring(0, i)

    // `.` or `/` names the repo root (same convention as SourceBundleAdd); other patterns are globs.
    let private isExcluded (patterns: string list) (dir: string) : bool =
        let isRoot p = p = "." || p = "/"
        (dir = "" && patterns |> List.exists isRoot)
        || Patterns.matchesAny (patterns |> List.filter (isRoot >> not)) dir

    // --scan: every index.md carrying okf_version marks an okf bundle. A root bundle
    // covers its descendants, so nested ones are dropped unless `nested` is set. Falls back to the
    // knowledge/ convention when no okf index is found.
    // With `nested`, every okf bundle is kept, even under a covering root bundle.
    // Directories matching `exclude` are dropped before nesting is resolved, so excluding a root
    // bundle lets its nested bundles be registered on their own.
    let private scanBundles (deps: Deps) (access: Access) (nested: bool) (exclude: string list) : Bundle list =
        let indexPaths =
            match access.Files () with
            | Ok files ->
                files
                |> List.filter (fun p -> p = "index.md" || p.EndsWith "/index.md")
                |> List.filter (fun p -> not (p.Split('/') |> Array.exists (fun seg -> seg.StartsWith ".")))
            | Error _ -> []
        let okfDirs =
            match indexPaths with
            | [] -> []
            | _ ->
                match access.Fetch indexPaths with
                | Ok files ->
                    files
                    |> List.filter (fun (_, content) ->
                        Frontmatter.parse deps.ParseYamlBlock content |> Frontmatter.okfVersion |> Option.isSome)
                    |> List.map (fst >> indexDir)
                    |> List.sort
                | Error _ -> []
            |> List.filter (isExcluded exclude >> not)
        let rec dropNested (dirs: string list) (kept: string list) =
            match dirs with
            | [] -> List.rev kept
            | d :: rest ->
                let covered = kept |> List.exists (fun k -> k = "" || d = k || d.StartsWith(k + "/"))
                dropNested rest (if covered then kept else d :: kept)
        match (if nested then okfDirs else dropNested okfDirs []) with
        | [] -> detectConventionBundle deps access |> List.filter (fun b -> not (isExcluded exclude b.Path))
        | dirs -> dirs |> List.map (fun d -> { Path = d; Kind = Okf })

    let private executeValid (deps: Deps) (cmd: Command) : Result<string, string> =
        let name = cmd.Name |> Option.defaultWith (fun () -> deriveNameFromUrl cmd.Url)
        let isFolder = deps.IsPlainFolder cmd.Url

        let bundles =
            match cmd.BasePath with
            | Some bp -> [ { Path = bp; Kind = Manifest } ]
            | None when cmd.Scan ->
                let access = if isFolder then folderAccess deps cmd.Url else gitAccess deps cmd.Url cmd.Branch
                scanBundles deps access cmd.Nested cmd.Exclude
            | None    -> []

        let newSource : SourceConfig = {
            Name     = name
            Url      = Some cmd.Url
            Branch   = cmd.Branch
            Bundles  = bundles
            Files = None; Search = None; Okf = None; Site = None; Enabled = None
        }

        let cacheManifest () =
            if not isFolder then
                let branch = cmd.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent cmd.Url branch [".eru/manifest.json"] with
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest name raw |> ignore
                | _ -> ()

        let detectionNote =
            match bundles, cmd.BasePath with
            | [], None when isFolder -> "\nLocal folder (read in place, okf bundles only). No bundles registered: use --scan to detect them, or 'eru source bundle add <name> <path> --kind okf'."
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
                | Ok None     -> Ok GlobalConfig.empty
                | Error e     -> Error e
            match globalCfg with
            | Error e -> Error e
            | Ok g ->
                if (GlobalConfig.sources g) |> List.exists (fun s -> s.Name = name) then
                    Error $"source '{name}' already exists."
                elif cmd.DryRun then
                    Ok $"Would add source '{name}' to global config.{detectionNote}"
                else
                    let updated = GlobalConfig.withSources ((GlobalConfig.sources g) @ [newSource]) g
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
                if (LocalConfig.sources local) |> List.exists (fun s -> s.Name = name) then
                    Error $"source '{name}' already exists."
                elif cmd.DryRun then
                    Ok $"Would add source '{name}' to .eru/config.json.{detectionNote}"
                else
                    let updated = LocalConfig.withSources ((LocalConfig.sources local) @ [newSource]) local
                    match deps.WriteLocalConfig updated with
                    | Error e -> Error e
                    | Ok () ->
                        cacheManifest ()
                        Ok $"Added source '{name}' to .eru/config.json.{detectionNote}"

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        if deps.IsPlainFolder cmd.Url && cmd.Branch.IsSome then Error "--branch does not apply to a local folder (it is not a git repository)."
        elif deps.IsPlainFolder cmd.Url && cmd.BasePath.IsSome then Error "--basepath does not apply to a local folder: folder sources support okf bundles only (use --scan)."
        elif cmd.Nested && not cmd.Scan then Error "--nested only applies together with --scan."
        elif not (List.isEmpty cmd.Exclude) && not cmd.Scan then Error "--exclude only applies together with --scan."
        else executeValid deps cmd
