namespace Eru

module InboxChannelAdd =

    type Command = {
        InboxName        : string
        ChannelName      : string
        AgentProtocol    : string option
        AgentCommand     : string option
        AgentArgs        : string list
        AgentInstructions: string option
        AgentTimeout     : int option
        Description      : string option
        DryRun           : bool
    }

    let private buildAgent (cmd: Command) : Result<AgentConfig option, string> =
        match cmd.AgentCommand with
        | None ->
            if cmd.AgentProtocol.IsSome || not cmd.AgentArgs.IsEmpty || cmd.AgentInstructions.IsSome || cmd.AgentTimeout.IsSome then
                Error "--agent-command is required when specifying --agent-protocol, --agent-args, --agent-instructions, or --agent-timeout."
            else
                Ok None
        | Some command ->
            let protocol = cmd.AgentProtocol |> Option.defaultValue "acp"
            if protocol <> "acp" then
                Error $"unsupported agent protocol '{protocol}' — only 'acp' is supported."
            else
                Ok (Some { Protocol = protocol; Command = command; Args = cmd.AgentArgs; InstructionsPath = cmd.AgentInstructions; Timeout = cmd.AgentTimeout })

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match buildAgent cmd with
        | Error e -> Error e
        | Ok agent ->

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
        else

        // The first agent channel becomes the inbox's default channel, so `inbox send` without
        // `-c` lands somewhere `inbox process` looks — without duplicating the agent elsewhere.
        let setsDefault = agent.IsSome && inbox.DefaultChannel.IsNone

        let successMessage =
            let baseMsg = $"Added channel '{cmd.ChannelName}' to inbox '{cmd.InboxName}'."
            if setsDefault then baseMsg + " Set it as the inbox's default channel."
            else baseMsg

        if cmd.DryRun then
            Ok ("Would do the following: " + successMessage)
        else
            let newChannel : InboxChannelConfig = { Description = cmd.Description; Agent = agent }
            let updatedInbox =
                { inbox with
                    Channels = Map.add cmd.ChannelName newChannel inbox.Channels
                    DefaultChannel = if setsDefault then Some cmd.ChannelName else inbox.DefaultChannel }

            let writeResult =
                // The channel is written to whichever config (local or global) already
                // owns this inbox's record — mirroring how bundles are added to sources.
                match scope, localCfg, globalCfg with
                | "local", Some local, _ ->
                    deps.WriteLocalConfig { local with Inboxes = Map.add cmd.InboxName updatedInbox local.Inboxes }
                | _, _, Some g ->
                    deps.WriteGlobalConfig { g with DefaultInboxes = Map.add cmd.InboxName updatedInbox g.DefaultInboxes }
                | _ -> Error $"could not locate the config owning inbox '{cmd.InboxName}'."

            writeResult |> Result.map (fun () -> successMessage)
