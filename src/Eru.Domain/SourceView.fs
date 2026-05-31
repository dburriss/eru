namespace Eru

module SourceView =

    type SourceFileEntry = {
        Path        : string
        Tags        : string list
        Description : string option
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
        BasePath : string option
        Manifest : ManifestState
    }

    let execute (deps: Deps) (sourceName: string) (showFull: bool) : Result<SourceDetail, string> =
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

        let manifest =
            match deps.ReadCachedManifest src.Name with
            | Error e -> LoadError e
            | Ok None -> NotCached
            | Ok (Some m) ->
                let cap = 20
                let files = m.Files
                let display = if showFull then files else files |> List.truncate cap
                let total = files.Length
                let capped = not showFull && total > cap
                let entries =
                    display |> List.map (fun f -> {
                        Path        = f.Path
                        Tags        = f.Tags
                        Description = f.Description
                    })
                Files (entries, total, capped)

        Ok {
            Name     = src.Name
            Scope    = origin
            Url      = src.Url
            Branch   = src.Branch
            BasePath = src.BasePath
            Manifest = manifest
        }
