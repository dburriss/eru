namespace Eru

module InboxAdd =

    type Command = {
        Name           : string
        Path           : string
        RawPath        : string option
        DefaultChannel : string option
        Branch         : string option
        IsGlobal       : bool
        DryRun         : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        let isRemote = InboxConfig.isRemotePath cmd.Path
        if not isRemote && not (deps.DirectoryExists cmd.Path) then
            Error $"'{cmd.Path}' is not an existing local directory."
        elif not isRemote && cmd.Branch.IsSome then
            Error "--branch only applies to a remote git inbox (a URL path)."
        else

        let newInbox : InboxConfig = {
            Path           = cmd.Path
            RawPath        = cmd.RawPath
            DefaultChannel = cmd.DefaultChannel
            Channels       = Map.empty
            Branch         = cmd.Branch
        }

        if cmd.IsGlobal then
            let globalCfg =
                match deps.ReadGlobalConfig () with
                | Ok (Some g) -> Ok g
                | Ok None     -> Ok { Version = 1; DefaultSources = []; Collections = []; DefaultInboxes = Map.empty; Defaults = None }
                | Error e     -> Error e
            match globalCfg with
            | Error e -> Error e
            | Ok g ->
                if Map.containsKey cmd.Name g.DefaultInboxes then
                    Error $"inbox '{cmd.Name}' already exists."
                elif cmd.DryRun then
                    Ok $"Would add inbox '{cmd.Name}' -> {cmd.Path} to global config."
                else
                    let updated = { g with DefaultInboxes = Map.add cmd.Name newInbox g.DefaultInboxes }
                    match deps.WriteGlobalConfig updated with
                    | Error e -> Error e
                    | Ok ()   -> Ok $"Added inbox '{cmd.Name}' -> {cmd.Path} to global config."
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                if Map.containsKey cmd.Name local.Inboxes then
                    Error $"inbox '{cmd.Name}' already exists."
                elif cmd.DryRun then
                    Ok $"Would add inbox '{cmd.Name}' -> {cmd.Path} to .eru/config.json."
                else
                    let updated = { local with Inboxes = Map.add cmd.Name newInbox local.Inboxes }
                    match deps.WriteLocalConfig updated with
                    | Error e -> Error e
                    | Ok ()   -> Ok $"Added inbox '{cmd.Name}' -> {cmd.Path} to .eru/config.json."
