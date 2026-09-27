namespace Eru

module InboxChannelAdd =

    type Command = {
        InboxName   : string
        ChannelName : string
        Agent       : string option
        Description : string option
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

        if Map.containsKey cmd.ChannelName inbox.Channels then
            Error $"channel '{cmd.ChannelName}' already exists on inbox '{cmd.InboxName}'."
        elif cmd.DryRun then
            Ok $"Would add channel '{cmd.ChannelName}' to inbox '{cmd.InboxName}'."
        else
            let newChannel : InboxChannelConfig = { Description = cmd.Description; Agent = cmd.Agent }
            let updatedInbox = { inbox with Channels = Map.add cmd.ChannelName newChannel inbox.Channels }

            let writeResult =
                // The channel is written to whichever config (local or global) already
                // owns this inbox's record — mirroring how bundles are added to sources.
                match scope, localCfg, globalCfg with
                | "local", Some local, _ ->
                    deps.WriteLocalConfig { local with Inboxes = Map.add cmd.InboxName updatedInbox local.Inboxes }
                | _, _, Some g ->
                    deps.WriteGlobalConfig { g with DefaultInboxes = Map.add cmd.InboxName updatedInbox g.DefaultInboxes }
                | _ -> Error $"could not locate the config owning inbox '{cmd.InboxName}'."

            writeResult
            |> Result.map (fun () -> $"Added channel '{cmd.ChannelName}' to inbox '{cmd.InboxName}'.")
