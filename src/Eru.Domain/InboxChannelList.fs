namespace Eru

module InboxChannelList =

    type ChannelRow = {
        Name        : string
        Description : string option
        Agent       : AgentConfig option
    }

    let execute (deps: Deps) (inboxName: string) : Result<ChannelRow list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let localInboxes  = localCfg  |> Option.map (fun l -> l.Inboxes)        |> Option.defaultValue Map.empty
        let globalInboxes = globalCfg |> Option.map (fun g -> g.DefaultInboxes) |> Option.defaultValue Map.empty

        let found =
            Map.tryFind inboxName localInboxes
            |> Option.orElseWith (fun () -> Map.tryFind inboxName globalInboxes)

        match found with
        | None -> Error $"inbox '{inboxName}' not found."
        | Some inbox ->
            inbox.Channels
            |> Map.toList
            |> List.map (fun (name, c) -> { Name = name; Description = c.Description; Agent = c.Agent })
            |> List.sortBy (fun r -> r.Name)
            |> Ok
