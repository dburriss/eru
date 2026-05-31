namespace Eru

module SourceRemove =

    type Command = {
        Name     : string
        IsGlobal : bool
        DryRun   : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        if cmd.IsGlobal then
            match deps.ReadGlobalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no global config found."
            | Ok (Some g) ->
                if not (g.DefaultSources |> List.exists (fun s -> s.Name = cmd.Name)) then
                    Error $"source '{cmd.Name}' not found in global config."
                elif cmd.DryRun then
                    Ok $"Would remove source '{cmd.Name}' from global config."
                else
                    let updated = { g with DefaultSources = g.DefaultSources |> List.filter (fun s -> s.Name <> cmd.Name) }
                    match deps.WriteGlobalConfig updated with
                    | Ok ()   -> Ok $"Removed source '{cmd.Name}' from global config."
                    | Error e -> Error e
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                if not (local.Sources |> List.exists (fun s -> s.Name = cmd.Name)) then
                    Error $"source '{cmd.Name}' not found in .eru/config.json."
                elif cmd.DryRun then
                    Ok $"Would remove source '{cmd.Name}' from .eru/config.json."
                else
                    let updated = { local with Sources = local.Sources |> List.filter (fun s -> s.Name <> cmd.Name) }
                    match deps.WriteLocalConfig updated with
                    | Ok ()   -> Ok $"Removed source '{cmd.Name}' from .eru/config.json."
                    | Error e -> Error e
