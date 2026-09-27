namespace Eru

module InboxList =

    type InboxRow = {
        Name           : string
        Path           : string
        RawPath        : string
        DefaultChannel : string
        Channels       : string list
        Scope          : string
    }

    let execute (deps: Deps) : Result<InboxRow list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let globalInboxes = globalCfg |> Option.map (fun g -> g.DefaultInboxes) |> Option.defaultValue Map.empty
        let localInboxes  = localCfg  |> Option.map (fun l -> l.Inboxes)        |> Option.defaultValue Map.empty

        let toRow (name: string) (inbox: InboxConfig) (scope: string) : InboxRow = {
            Name           = name
            Path           = inbox.Path
            RawPath        = inbox.RawPath |> Option.defaultValue "inbox/raw"
            DefaultChannel = inbox.DefaultChannel |> Option.defaultValue "default"
            Channels       = inbox.Channels |> Map.toList |> List.map fst |> List.sort
            Scope          = scope
        }

        let localRows = localInboxes |> Map.toList |> List.map (fun (n, i) -> toRow n i "local")
        let globalRows =
            globalInboxes
            |> Map.toList
            |> List.filter (fun (n, _) -> not (Map.containsKey n localInboxes))
            |> List.map (fun (n, i) -> toRow n i "global")

        Ok (localRows @ globalRows)
