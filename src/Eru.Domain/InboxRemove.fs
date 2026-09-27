namespace Eru

module InboxRemove =

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
                if not (Map.containsKey cmd.Name g.DefaultInboxes) then
                    Error $"inbox '{cmd.Name}' not found in global config."
                elif cmd.DryRun then
                    Ok $"Would remove inbox '{cmd.Name}' from global config."
                else
                    let updated = { g with DefaultInboxes = Map.remove cmd.Name g.DefaultInboxes }
                    match deps.WriteGlobalConfig updated with
                    | Ok ()   -> Ok $"Removed inbox '{cmd.Name}' from global config."
                    | Error e -> Error e
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                if not (Map.containsKey cmd.Name local.Inboxes) then
                    Error $"inbox '{cmd.Name}' not found in .eru/config.json."
                elif cmd.DryRun then
                    Ok $"Would remove inbox '{cmd.Name}' from .eru/config.json."
                else
                    let updated = { local with Inboxes = Map.remove cmd.Name local.Inboxes }
                    match deps.WriteLocalConfig updated with
                    | Ok ()   -> Ok $"Removed inbox '{cmd.Name}' from .eru/config.json."
                    | Error e -> Error e
