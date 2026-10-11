namespace Eru

module SourceBundleRemove =

    type Command = {
        SourceName : string
        Path       : string
        DryRun     : bool
    }

    let private normalizePath (raw: string) : string =
        if raw = "." || raw = "/" then "" else raw

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        let path = normalizePath cmd.Path

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let localMatch  = localCfg  |> Option.bind (fun l -> (LocalConfig.sources l) |> List.tryFind (fun s -> s.Name = cmd.SourceName)) |> Option.map (fun s -> s, "local")
        let globalMatch = globalCfg |> Option.bind (fun g -> (GlobalConfig.sources g) |> List.tryFind (fun s -> s.Name = cmd.SourceName)) |> Option.map (fun s -> s, "global")

        match localMatch |> Option.orElse globalMatch with
        | None -> Error $"source '{cmd.SourceName}' not found."
        | Some (src, scope) ->

        if not (src.Bundles |> List.exists (fun b -> b.Path = path)) then
            Error $"bundle '{path}' not found on source '{cmd.SourceName}'."
        elif cmd.DryRun then
            Ok $"Would remove bundle '{path}' from source '{cmd.SourceName}'."
        else
            let updatedSrc = { src with Bundles = src.Bundles |> List.filter (fun b -> b.Path <> path) }
            let replaceSource (sources: SourceConfig list) =
                sources |> List.map (fun s -> if s.Name = cmd.SourceName then updatedSrc else s)

            // No immediate index cleanup needed — the next `eru sync`'s full
            // contribution rebuild naturally drops the removed bundle's contributions.
            match scope, localCfg, globalCfg with
            | "local", Some local, _ ->
                match deps.WriteLocalConfig (LocalConfig.withSources (replaceSource (LocalConfig.sources local)) local) with
                | Ok ()   -> Ok $"Removed bundle '{path}' from source '{cmd.SourceName}'."
                | Error e -> Error e
            | _, _, Some g ->
                match deps.WriteGlobalConfig (GlobalConfig.withSources (replaceSource (GlobalConfig.sources g)) g) with
                | Ok ()   -> Ok $"Removed bundle '{path}' from source '{cmd.SourceName}'."
                | Error e -> Error e
            | _ -> Error $"could not locate the config owning source '{cmd.SourceName}'."
