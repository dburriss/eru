namespace Eru

module InboxDefault =

    type Command = {
        Name     : string
        IsGlobal : bool
        DryRun   : bool
    }

    let private emptyGlobalDefaults : GlobalDefaults =
        { Branch = None
          CommitOnPull = None
          McpRefreshIntervalMinutes = None
          BlockPatterns = None
          AllowPatterns = None
          AllowBinaries = None
          SiteIgnorePatterns = None
          OkfIgnorePatterns = None
          DefaultInbox = None
          InboxWatchIntervalSeconds = None }

    let private emptyLocalSettings : LocalSettings =
        { CommitOnPull = None
          StateFile = None
          BlockPatterns = None
          AllowPatterns = None
          AllowBinaries = None
          SiteIgnorePatterns = None
          OkfIgnorePatterns = None
          DefaultInbox = None
          InboxWatchIntervalSeconds = None }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        let known =
            Set.union
                (globalCfg |> Option.map (fun g -> g.DefaultInboxes |> Map.keys |> Set.ofSeq) |> Option.defaultValue Set.empty)
                (localCfg  |> Option.map (fun l -> l.Inboxes        |> Map.keys |> Set.ofSeq) |> Option.defaultValue Set.empty)

        if not (Set.contains cmd.Name known) then
            Error $"inbox '{cmd.Name}' not configured."
        elif cmd.IsGlobal then
            if cmd.DryRun then Ok $"Would set default inbox to '{cmd.Name}' in global config."
            else
                let g = globalCfg |> Option.defaultValue { Version = 1; DefaultSources = []; Collections = []; DefaultInboxes = Map.empty; Defaults = None }
                let d = g.Defaults |> Option.defaultValue emptyGlobalDefaults
                match deps.WriteGlobalConfig { g with Defaults = Some { d with DefaultInbox = Some cmd.Name } } with
                | Ok ()   -> Ok $"Default inbox set to '{cmd.Name}' in global config."
                | Error e -> Error e
        else
            match localCfg with
            | None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Some local ->
                if cmd.DryRun then Ok $"Would set default inbox to '{cmd.Name}' in .eru/config.json."
                else
                    let s = local.Settings |> Option.defaultValue emptyLocalSettings
                    match deps.WriteLocalConfig { local with Settings = Some { s with DefaultInbox = Some cmd.Name } } with
                    | Ok ()   -> Ok $"Default inbox set to '{cmd.Name}' in .eru/config.json."
                    | Error e -> Error e
