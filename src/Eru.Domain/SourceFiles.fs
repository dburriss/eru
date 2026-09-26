namespace Eru

module SourceFiles =

    type SourceFileRow = {
        Hash        : string
        Path        : string
        Tags        : string list
        Description : string option
        Bundle      : string option   // owning bundle's Path (Some "" for the repo-root bundle); None = unassigned
    }

    let private filesForSourceFromIndex (deps: Deps) (bundleFilter: string option) (src: SourceConfig) : Result<string * SourceFileRow list, string> =
        match deps.ReadSourceIndex src.Name with
        | Error e -> Error $"Error reading index for '{src.Name}': {e}"
        | Ok None -> Error $"No index cached for '{src.Name}'. Run 'eru sync' to fetch source metadata."
        | Ok (Some idx) ->
            let rows =
                idx.Entries
                |> Map.toList
                |> List.map (fun (path, entry) ->
                    let owner = Bundle.owningBundleForDisplay src.Bundles path |> Option.map (fun b -> b.Path)
                    path, entry, owner)
                |> List.filter (fun (_, _, owner) ->
                    match bundleFilter with
                    | None -> true
                    | Some bp -> owner = Some bp)
                |> List.map (fun (path, entry, owner) -> {
                    Hash        = Patterns.pathShortHash path
                    Path        = path
                    Tags        = entry.Tags
                    Description = entry.Description
                    Bundle      = owner
                })
            Ok (src.Name, rows)

    let execute (deps: Deps) (sourceName: string option) (bundleFilter: string option) : Result<(string * SourceFileRow list) list, string> =
        let bundleFilter = bundleFilter |> Option.map SourceView.normalizeBundlePathFilter
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []
        let allSources    = localSources @ globalSources

        match sourceName with
        | Some name ->
            let found =
                localSources |> List.tryFind (fun s -> s.Name = name)
                |> Option.orElseWith (fun () -> globalSources |> List.tryFind (fun s -> s.Name = name))
            match found with
            | None     -> Error $"source '{name}' not found."
            | Some src -> filesForSourceFromIndex deps bundleFilter src |> Result.map List.singleton
        | None ->
            match allSources with
            | [] -> Error "No sources configured. Run 'eru source add' first."
            | _  ->
                let results = allSources |> List.map (filesForSourceFromIndex deps bundleFilter)
                let successes = results |> List.choose (function Ok r -> Some r | Error _ -> None)
                let errors    = results |> List.choose (function Error e -> Some e | Ok _ -> None)
                match successes, errors with
                | [], _  -> Error (errors |> String.concat "\n")
                | _,  _  -> Ok successes
