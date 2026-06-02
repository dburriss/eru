namespace Eru

module SourceFiles =

    type SourceFileRow = {
        Hash        : string
        Path        : string
        Tags        : string list
        Description : string option
    }

    let private filesForSourceFromIndex (deps: Deps) (src: SourceConfig) : Result<string * SourceFileRow list, string> =
        match deps.ReadSourceIndex src.Name with
        | Error e -> Error $"Error reading index for '{src.Name}': {e}"
        | Ok None -> Error $"No index cached for '{src.Name}'. Run 'eru sync' to fetch source metadata."
        | Ok (Some idx) ->
            let rows =
                idx |> Map.toList |> List.map (fun (path, entry) -> {
                    Hash        = Patterns.pathShortHash path
                    Path        = path
                    Tags        = entry.Tags
                    Description = entry.Description
                })
            Ok (src.Name, rows)

    let execute (deps: Deps) (sourceName: string option) : Result<(string * SourceFileRow list) list, string> =
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
            | Some src -> filesForSourceFromIndex deps src |> Result.map List.singleton
        | None ->
            match allSources with
            | [] -> Error "No sources configured. Run 'eru source add' first."
            | _  ->
                let results = allSources |> List.map (filesForSourceFromIndex deps)
                let successes = results |> List.choose (function Ok r -> Some r | Error _ -> None)
                let errors    = results |> List.choose (function Error e -> Some e | Ok _ -> None)
                match successes, errors with
                | [], _  -> Error (errors |> String.concat "\n")
                | _,  _  -> Ok successes
