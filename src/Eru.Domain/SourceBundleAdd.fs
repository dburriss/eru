namespace Eru

module SourceBundleAdd =

    type Command = {
        SourceName : string
        Path       : string
        Kind       : string option   // explicit --kind ("manifest" | "okf"); None = auto-detect
        DryRun     : bool
    }

    let private normalizePath (raw: string) : string =
        if raw = "." || raw = "/" then "" else raw

    let private parseKind (s: string) : Result<BundleKind, string> =
        match s with
        | "manifest" -> Ok Manifest
        | "okf"      -> Ok Okf
        | other      -> Error $"unknown bundle kind '{other}' — expected 'manifest' or 'okf'."

    let private detectKind (deps: Deps) (url: string) (branch: string option) (path: string) : BundleKind =
        let actualBranch = branch |> Option.defaultValue "HEAD"
        let indexPath = if path = "" then "index.md" else $"{path}/index.md"
        let hasOkf =
            match deps.FetchRemoteContent url actualBranch [indexPath] with
            | Ok ((_, content) :: _) ->
                Frontmatter.parse deps.ParseYamlBlock content |> Frontmatter.okfVersion |> Option.isSome
            | _ -> false
        BundleDetect.detectKind hasOkf

    let private kindLabel = function Manifest -> "manifest" | Okf -> "okf"

    // One-bundle discovery walk, merged directly into the source's index —
    // cheaper than triggering a full `eru sync` just to register one bundle.
    let private discoverAndMergeBundle (deps: Deps) (ignorePatterns: string list) (sourceName: string) (url: string) (branch: string) (bundles: Bundle list) (bundle: Bundle) =
        match BundleDiscovery.walkBundle deps ignorePatterns sourceName url branch bundle with
        | Error _ -> ()
        | Ok discovered ->
            let existingIdx =
                match deps.ReadSourceIndex sourceName with
                | Ok (Some idx) -> idx
                | _ -> { Version = 1; SourceHeadSha = None; ConsecutiveShaCheckFailures = 0; Entries = Map.empty }
            let mutable entries = existingIdx.Entries
            for d in discovered do
                let contentHash = deps.HashContent d.Content
                let cacheRelPath = match deps.CacheSourceContent sourceName contentHash d.Content with Ok p -> Some p | Error _ -> None
                let existing = Map.tryFind d.RemotePath entries |> Option.defaultValue IndexEntry.empty
                let contributions = existing.Contributions |> Map.add ContributionKey.frontmatter d.Contribution
                let blended = IndexBlend.blend bundles d.RemotePath contributions
                entries <-
                    entries
                    |> Map.add d.RemotePath {
                        existing with
                            Contributions = contributions
                            Tags          = blended.Tags
                            Description   = blended.Description
                            CacheRelPath  = cacheRelPath
                            ContentHash   = Some contentHash
                            Type          = d.Type
                            Title         = d.Title
                            OkfStatus     = d.OkfStatus
                            Generated     = d.Generated
                            Verified      = d.Verified
                            StaleAfter    = d.StaleAfter
                            Resource      = d.Resource
                    }
                match cacheRelPath with
                | Some relPath -> deps.BuildSearchIndex sourceName relPath
                | None         -> ()
            deps.WriteSourceIndex sourceName { existingIdx with Entries = entries } |> ignore

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        let path = normalizePath cmd.Path

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let localMatch  = localCfg  |> Option.bind (fun l -> l.Sources |> List.tryFind (fun s -> s.Name = cmd.SourceName)) |> Option.map (fun s -> s, "local")
        let globalMatch = globalCfg |> Option.bind (fun g -> g.DefaultSources |> List.tryFind (fun s -> s.Name = cmd.SourceName)) |> Option.map (fun s -> s, "global")

        match localMatch |> Option.orElse globalMatch with
        | None -> Error $"source '{cmd.SourceName}' not found."
        | Some (src, scope) ->

        if src.Bundles |> List.exists (fun b -> b.Path = path) then
            Error $"bundle '{path}' already exists on source '{cmd.SourceName}'."
        else

        let explicitKind =
            match cmd.Kind with
            | None -> Ok None
            | Some raw -> parseKind raw |> Result.map Some

        match explicitKind with
        | Error e -> Error e
        | Ok explicitKind ->

        let kind =
            match explicitKind with
            | Some k -> k
            | None ->
                match src.Url with
                | Some url -> detectKind deps url src.Branch path
                | None     -> Manifest

        let newBundle = { Path = path; Kind = kind }

        // Auto-detected manifest with no manifest file would register a bundle that
        // can publish nothing; say so instead of staying silent.
        let warning =
            match explicitKind, kind, src.Url with
            | None, Manifest, Some url ->
                BundleKindWarning.noManifestWarning deps url src.Branch path
                |> Option.map (fun w -> "\n" + w)
                |> Option.defaultValue ""
            | _ -> ""

        if cmd.DryRun then
            Ok $"Would add bundle '{path}' (kind: {kindLabel kind}) to source '{cmd.SourceName}'.{warning}"
        else
            let updatedSrc = { src with Bundles = src.Bundles @ [newBundle] }

            let replaceSource (sources: SourceConfig list) =
                sources |> List.map (fun s -> if s.Name = cmd.SourceName then updatedSrc else s)

            let writeResult =
                // The bundle is appended to whichever config (local or global) already
                // owns this source's record — mirroring how the source itself was created.
                match scope, localCfg, globalCfg with
                | "local", Some local, _ ->
                    deps.WriteLocalConfig { local with Sources = replaceSource local.Sources }
                | _, _, Some g ->
                    deps.WriteGlobalConfig { g with DefaultSources = replaceSource g.DefaultSources }
                | _ -> Error $"could not locate the config owning source '{cmd.SourceName}'."

            match writeResult with
            | Error e -> Error e
            | Ok () ->
                match kind, src.Url with
                | Okf, Some url ->
                    let branch = src.Branch |> Option.defaultValue "HEAD"
                    discoverAndMergeBundle deps (Config.resolveOkfIgnorePatterns globalCfg localCfg) cmd.SourceName url branch updatedSrc.Bundles newBundle
                | _ -> ()
                Ok $"Added bundle '{path}' (kind: {kindLabel kind}) to source '{cmd.SourceName}'.{warning}"
