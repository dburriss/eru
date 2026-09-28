namespace Eru

module InboxChannelAdd =

    type Command = {
        InboxName        : string
        ChannelName      : string
        AgentProtocol    : string option
        AgentCommand     : string option
        AgentArgs        : string list
        AgentInstructions: string option
        Description      : string option
        DryRun           : bool
    }

    let private buildAgent (cmd: Command) : Result<AgentConfig option, string> =
        match cmd.AgentCommand with
        | None ->
            if cmd.AgentProtocol.IsSome || not cmd.AgentArgs.IsEmpty || cmd.AgentInstructions.IsSome then
                Error "--agent-command is required when specifying --agent-protocol, --agent-args, or --agent-instructions."
            else
                Ok None
        | Some command ->
            let protocol = cmd.AgentProtocol |> Option.defaultValue "acp"
            if protocol <> "acp" then
                Error $"unsupported agent protocol '{protocol}' — only 'acp' is supported."
            else
                Ok (Some { Protocol = protocol; Command = command; Args = cmd.AgentArgs; InstructionsPath = cmd.AgentInstructions })

    // `inbox send` falls back to the literal channel "default" whenever no `-c` is given
    // (InboxSend.fs) — so an item can land there without the user ever having run
    // `channel add` for it. Configuring an agent on any OTHER channel silently wires the
    // same agent onto "default" too, as long as "default" doesn't already have one of its
    // own (never overwrites an explicit choice), so `inbox process`'s default scope doesn't
    // quietly skip whatever landed in the fallback channel.
    let private withDefaultAgentFallback (channelName: string) (agent: AgentConfig option) (channels: Map<string, InboxChannelConfig>) : Map<string, InboxChannelConfig> =
        match agent with
        | None -> channels
        | Some _ when channelName = "default" -> channels
        | Some a ->
            match Map.tryFind "default" channels with
            | Some existing when existing.Agent.IsSome -> channels
            | Some existing -> Map.add "default" { existing with Agent = Some a } channels
            | None -> Map.add "default" { Description = None; Agent = Some a } channels

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

        let defaultAutoPopulated =
            agent.IsSome
            && cmd.ChannelName <> "default"
            && (inbox.Channels |> Map.tryFind "default" |> Option.forall (fun c -> c.Agent.IsNone))

        let successMessage =
            let baseMsg = $"Added channel '{cmd.ChannelName}' to inbox '{cmd.InboxName}'."
            if defaultAutoPopulated then
                baseMsg + " Also wired channel 'default' to the same agent, since it had none configured."
            else baseMsg

        if cmd.DryRun then
            Ok ("Would do the following: " + successMessage)
        else
            let newChannel : InboxChannelConfig = { Description = cmd.Description; Agent = agent }
            let updatedChannels =
                inbox.Channels
                |> Map.add cmd.ChannelName newChannel
                |> withDefaultAgentFallback cmd.ChannelName agent
            let updatedInbox = { inbox with Channels = updatedChannels }

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
