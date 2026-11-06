namespace Eru

/// `eru source enable|disable <name>`: flips `enabled` on a source without touching its cache.
module SourceToggle =

    type Command = {
        Name     : string
        Enable   : bool
        IsGlobal : bool
        DryRun   : bool
    }

    let private verb (enable: bool) = if enable then "enable" else "disable"

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let globalSources = globalCfg |> Option.map GlobalConfig.sources |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map LocalConfig.sources  |> Option.defaultValue []
        let past = if cmd.Enable then "Enabled" else "Disabled"
        let would = $"Would {verb cmd.Enable} source '{cmd.Name}'"

        if cmd.IsGlobal then
            match globalCfg with
            | None -> Error "no global config found."
            | Some g ->
                if not (globalSources |> List.exists (fun s -> s.Name = cmd.Name)) then
                    Error $"source '{cmd.Name}' not found in global config."
                elif cmd.DryRun then Ok $"{would} in global config."
                else
                    let value = if cmd.Enable then None else Some false
                    let update (s: SourceConfig) = if s.Name = cmd.Name then { s with Enabled = value } else s
                    deps.WriteGlobalConfig (GlobalConfig.withSources (globalSources |> List.map update) g)
                    |> Result.map (fun () -> $"{past} source '{cmd.Name}' in global config.")
        else
            match localCfg with
            | None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Some local ->
                let inLocal  = localSources  |> List.exists (fun s -> s.Name = cmd.Name)
                let globalSrc = globalSources |> List.tryFind (fun s -> s.Name = cmd.Name)
                if not inLocal && globalSrc.IsNone then
                    Error $"source '{cmd.Name}' not found."
                elif cmd.DryRun then Ok $"{would} in .eru/config.json."
                else
                    // Re-enabling clears the key, unless the global entry is itself disabled and
                    // the local entry has to say otherwise.
                    let globalOff = globalSrc |> Option.exists (SourceConfig.isEnabled >> not)
                    let value = if not cmd.Enable then Some false elif globalOff then Some true else None
                    let updated =
                        if inLocal then
                            localSources |> List.map (fun s -> if s.Name = cmd.Name then { s with Enabled = value } else s)
                        else
                            // A global source gets a url-less local entry, so this project alone is affected.
                            localSources @ [ { SourceConfig.empty with Name = cmd.Name; Enabled = value } ]
                    deps.WriteLocalConfig (LocalConfig.withSources updated local)
                    |> Result.map (fun () -> $"{past} source '{cmd.Name}' in .eru/config.json.")
