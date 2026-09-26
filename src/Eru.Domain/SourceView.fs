namespace Eru

module SourceView =

    type SourceFileEntry = {
        Path        : string
        Tags        : string list
        Description : string option
        Bundle      : string option   // owning bundle's Path (Some "" for the repo-root bundle); None = unassigned
    }

    type ManifestState =
        | NotCached
        | LoadError of string
        | Files     of entries: SourceFileEntry list * total: int * capped: bool

    type SourceDetail = {
        Name     : string
        Scope    : string
        Url      : string option
        Branch   : string option
        Bundles  : Bundle list
        Manifest : ManifestState
    }

    // Normalizes the CLI's "." / "/" repo-root sentinel to the internal "" bundle path.
    let normalizeBundlePathFilter (raw: string) : string =
        if raw = "." || raw = "/" then "" else raw

    let execute (deps: Deps) (sourceName: string) (showFull: bool) (bundleFilter: string option) : Result<SourceDetail, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []

        let found =
            localSources |> List.tryFind (fun s -> s.Name = sourceName) |> Option.map (fun s -> s, "local")
            |> Option.orElseWith (fun () ->
                globalSources |> List.tryFind (fun s -> s.Name = sourceName) |> Option.map (fun s -> s, "global"))

        match found with
        | None -> Error $"source '{sourceName}' not found."
        | Some (src, origin) ->

        let bundleFilter = bundleFilter |> Option.map normalizeBundlePathFilter

        let manifest =
            match deps.ReadCachedManifest src.Name with
            | Error e -> LoadError e
            | Ok None -> NotCached
            | Ok (Some m) ->
                let withBundle =
                    m.Files
                    |> List.map (fun f ->
                        let owner = Bundle.owningBundleForDisplay src.Bundles f.Path |> Option.map (fun b -> b.Path)
                        f, owner)
                let filtered =
                    match bundleFilter with
                    | None -> withBundle
                    | Some bp -> withBundle |> List.filter (fun (_, owner) -> owner = Some bp)
                let cap = 20
                let display = if showFull then filtered else filtered |> List.truncate cap
                let total = filtered.Length
                let capped = not showFull && total > cap
                let entries =
                    display |> List.map (fun (f, owner) -> {
                        Path        = f.Path
                        Tags        = f.Tags
                        Description = f.Description
                        Bundle      = owner
                    })
                Files (entries, total, capped)

        Ok {
            Name     = src.Name
            Scope    = origin
            Url      = src.Url
            Branch   = src.Branch
            Bundles  = src.Bundles
            Manifest = manifest
        }
