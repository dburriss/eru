namespace Eru

module InboxDefault =

    type Command = {
        Name     : string
        IsGlobal : bool
        DryRun   : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let known =
            Set.union
                (globalCfg |> Option.map (fun g -> GlobalConfig.inboxes g |> Map.keys |> Set.ofSeq) |> Option.defaultValue Set.empty)
                (localCfg  |> Option.map (fun l -> LocalConfig.inboxes l |> Map.keys |> Set.ofSeq) |> Option.defaultValue Set.empty)

        if not (Set.contains cmd.Name known) then
            Error $"inbox '{cmd.Name}' not configured."
        elif cmd.IsGlobal then
            if cmd.DryRun then Ok $"Would set default inbox to '{cmd.Name}' in global config."
            else
                let g = globalCfg |> Option.defaultValue GlobalConfig.empty
                match deps.WriteGlobalConfig (GlobalConfig.withDefaultInbox cmd.Name g) with
                | Ok ()   -> Ok $"Default inbox set to '{cmd.Name}' in global config."
                | Error e -> Error e
        else
            match localCfg with
            | None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Some local ->
                if cmd.DryRun then Ok $"Would set default inbox to '{cmd.Name}' in .eru/config.json."
                else
                    match deps.WriteLocalConfig (LocalConfig.withDefaultInbox cmd.Name local) with
                    | Ok ()   -> Ok $"Default inbox set to '{cmd.Name}' in .eru/config.json."
                    | Error e -> Error e
