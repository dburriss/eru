namespace Eru

module SourceBundleList =

    let execute (deps: Deps) (sourceName: string) : Result<Bundle list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []
        let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []

        let found =
            localSources |> List.tryFind (fun s -> s.Name = sourceName)
            |> Option.orElseWith (fun () -> globalSources |> List.tryFind (fun s -> s.Name = sourceName))

        match found with
        | None -> Error $"source '{sourceName}' not found."
        | Some src -> Ok src.Bundles
