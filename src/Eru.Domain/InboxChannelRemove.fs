namespace Eru

module InboxChannelRemove =

    type Command = {
        InboxName   : string
        ChannelName : string
        DryRun      : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let localMatch  = localCfg  |> Option.bind (fun l -> Map.tryFind cmd.InboxName l.Inboxes)        |> Option.map (fun i -> i, "local")
        let globalMatch = globalCfg |> Option.bind (fun g -> Map.tryFind cmd.InboxName g.DefaultInboxes) |> Option.map (fun i -> i, "global")

        match localMatch |> Option.orElse globalMatch with
        | None -> Error $"inbox '{cmd.InboxName}' not found."
        | Some (inbox, scope) ->

        if not (Map.containsKey cmd.ChannelName inbox.Channels) then
            Error $"channel '{cmd.ChannelName}' not found on inbox '{cmd.InboxName}'."
        elif cmd.DryRun then
            Ok $"Would remove channel '{cmd.ChannelName}' from inbox '{cmd.InboxName}'."
        else
            let updatedInbox = { inbox with Channels = Map.remove cmd.ChannelName inbox.Channels }
            match scope, localCfg, globalCfg with
            | "local", Some local, _ ->
                match deps.WriteLocalConfig { local with Inboxes = Map.add cmd.InboxName updatedInbox local.Inboxes } with
                | Ok ()   -> Ok $"Removed channel '{cmd.ChannelName}' from inbox '{cmd.InboxName}'."
                | Error e -> Error e
            | _, _, Some g ->
                match deps.WriteGlobalConfig { g with DefaultInboxes = Map.add cmd.InboxName updatedInbox g.DefaultInboxes } with
                | Ok ()   -> Ok $"Removed channel '{cmd.ChannelName}' from inbox '{cmd.InboxName}'."
                | Error e -> Error e
            | _ -> Error $"could not locate the config owning inbox '{cmd.InboxName}'."
