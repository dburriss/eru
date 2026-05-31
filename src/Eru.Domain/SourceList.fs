namespace Eru

module SourceList =

    type SourceRow = {
        Name     : string
        Url      : string option
        Branch   : string option
        BasePath : string option
        Scope    : string
        Tags     : string list
    }

    let private rowTags (deps: Deps) (src: SourceConfig) : string list =
        match deps.ReadCachedManifest src.Name with
        | Ok (Some manifest) ->
            manifest.Files
            |> List.collect (fun f -> f.Tags)
            |> List.distinct
            |> List.sort
        | _ -> []

    let execute (deps: Deps) : Result<SourceRow list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []
        let localNames    = localSources |> List.map (fun s -> s.Name) |> Set.ofList

        let toRow (src: SourceConfig) (scope: string) : SourceRow = {
            Name     = src.Name
            Url      = src.Url
            Branch   = src.Branch
            BasePath = src.BasePath
            Scope    = scope
            Tags     = rowTags deps src
        }

        let localRows =
            localSources |> List.map (fun src ->
                let scope = if src.Url.IsSome then "local" else "local → global alias"
                toRow src scope)

        let globalRows =
            globalSources
            |> List.filter (fun s -> not (Set.contains s.Name localNames))
            |> List.map (fun src -> toRow src "global")

        Ok (localRows @ globalRows)
