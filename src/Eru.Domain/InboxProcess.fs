namespace Eru

open System.IO

module InboxProcess =

    type Options = {
        InboxName : string option
        Channel   : string option   // -c; None = every agent-enabled channel
        ItemName  : string option   // <name>, if given
        All       : bool
        DryRun    : bool
    }

    type ProcessedItem = {
        Channel     : string
        ItemPath    : string   // original raw path
        ArchivePath : string   // where it landed (or would land, for --dryrun)
        Agent       : AgentConfig
    }

    // A raw item pooled across every channel in scope, tagged with the channel/agent
    // it came from so downstream steps don't care how many channels contributed.
    type private PooledItem = {
        Channel     : string
        Agent       : AgentConfig
        FileName    : string
        FullPath    : string
        SidecarPath : string option
    }

    let private resolveInbox (eff: EffectiveConfig) (explicit_: string option) : Result<string * InboxConfig, string> =
        let byName name =
            match Map.tryFind name eff.Inboxes with
            | Some inbox -> Ok (name, inbox)
            | None       -> Error $"inbox '{name}' not configured."
        match explicit_ with
        | Some name -> byName name
        | None ->
            match eff.DefaultInbox with
            | Some name -> byName name
            | None ->
                match eff.Inboxes |> Map.toList with
                | [ single ] -> Ok single
                | []         -> Error "no inbox configured. Run 'eru inbox add <name> <path>' first."
                | many       ->
                    let names = many |> List.map fst |> String.concat ", "
                    Error $"multiple inboxes configured ({names}) — specify one with -i/--inbox or set settings.defaultInbox."

    let private channelsInScope (inbox: InboxConfig) (channel: string option) : Result<(string * AgentConfig) list, string> =
        match channel with
        | Some c ->
            match Map.tryFind c inbox.Channels with
            | None -> Error $"channel '{c}' not found on this inbox."
            | Some cc ->
                match cc.Agent with
                | None -> Error $"channel '{c}' has no agent configured. Run 'eru inbox channel add <inbox> {c} --agent-command <cmd>' to enable one."
                | Some agent -> Ok [ c, agent ]
        | None ->
            let scoped =
                inbox.Channels
                |> Map.toList
                |> List.choose (fun (name, cc) -> cc.Agent |> Option.map (fun a -> name, a))
            if scoped.IsEmpty then
                Error "no channel on this inbox has an agent configured. Run 'eru inbox channel add <inbox> <channel> --agent-command <cmd>' to enable one."
            else
                Ok scoped

    let private isSidecar (name: string) = name.EndsWith(".meta.json")

    // Repo/filesystem hygiene placeholders (`.gitkeep`, `.gitignore`, `.DS_Store`, ...) are
    // never something `inbox send` writes — every real capture filename is a compact UTC
    // timestamp (InboxSend.fs), never a dotfile — so treat any hidden file as noise rather
    // than a pending raw item.
    let private isHidden (name: string) = name.StartsWith "."

    let private listChannelItems (deps: Deps) (inbox: InboxConfig) (channel: string) (agent: AgentConfig) : Result<PooledItem list, string> =
        let rawPath = inbox.RawPath |> Option.defaultValue "inbox/raw"
        let dir = Path.Combine(inbox.Path, rawPath, channel)
        match deps.ListLocalFiles dir with
        | Error e -> Error e
        | Ok files ->
            let names = files |> List.map Path.GetFileName
            let nameSet = Set.ofList names
            names
            |> List.filter (fun name -> not (isSidecar name) && not (isHidden name))
            |> List.map (fun name ->
                let sidecarName = name + ".meta.json"
                let sidecar = if Set.contains sidecarName nameSet then Some (Path.Combine(dir, sidecarName)) else None
                { Channel = channel; Agent = agent; FileName = name; FullPath = Path.Combine(dir, name); SidecarPath = sidecar })
            |> Ok

    let private pool (deps: Deps) (inbox: InboxConfig) (scope: (string * AgentConfig) list) : Result<PooledItem list, string> =
        scope
        |> List.fold (fun acc (channel, agent) ->
            match acc with
            | Error e -> Error e
            | Ok items ->
                match listChannelItems deps inbox channel agent with
                | Error e -> Error e
                | Ok chItems -> Ok (items @ chItems))
            (Ok [])
        |> Result.map (List.sortBy (fun i -> i.FileName))

    let private stem (name: string) = Path.GetFileNameWithoutExtension name

    let private selectByName (name: string) (items: PooledItem list) : Result<PooledItem, string> =
        let matches = items |> List.filter (fun i -> i.FileName = name || stem i.FileName = stem name)
        match matches with
        | [] -> Error $"no pending item named '{name}' found in scope."
        | [ one ] -> Ok one
        | many ->
            let channels = many |> List.map (fun i -> i.Channel) |> List.distinct
            if channels.Length > 1 then
                let names = channels |> String.concat ", "
                Error $"item '{name}' matches more than one channel in scope ({names}) — specify -c/--channel to disambiguate."
            else
                Ok (List.head many)

    // Convention fallback when a channel's `AgentConfig.InstructionsPath` is unset — the
    // canonical, tool-agnostic home for agent/subagent definitions (`.claude/agents/*.md`
    // is typically a symlink into this directory).
    let private defaultInstructionsRelPath = ".agents/agents/ingestor.md"

    // The bare raw content alone carries no instructions for what an otherwise-generic ACP
    // agent should actually do with it — this resolves the instructions file (if any) to
    // prepend to the prompt so the agent knows to curate rather than just reply
    // conversationally. An explicitly-configured `InstructionsPath` that doesn't resolve is
    // a real error (misconfiguration); the implicit default is best-effort — its absence
    // just means no instructions get prepended.
    let private resolveInstructions (deps: Deps) (inbox: InboxConfig) (agent: AgentConfig) : Result<string option, string> =
        let isExplicit = agent.InstructionsPath.IsSome
        let path =
            match agent.InstructionsPath with
            | Some p when Path.IsPathRooted p -> p
            | Some p -> Path.Combine(inbox.Path, p)
            | None -> Path.Combine(inbox.Path, defaultInstructionsRelPath)
        match deps.ReadLocalFile path with
        | Error e -> Error e
        | Ok (Some content) -> Ok (Some content)
        | Ok None -> if isExplicit then Error $"agent instructions file '{path}' not found." else Ok None

    let private buildPrompt (deps: Deps) (inbox: InboxConfig) (item: PooledItem) : Result<string, string> =
        let itemContent =
            if Path.GetExtension(item.FileName).ToLowerInvariant() = ".md" then
                match deps.ReadLocalFile item.FullPath with
                | Error e -> Error e
                | Ok None -> Error $"raw item '{item.FullPath}' not found."
                | Ok (Some content) -> Ok (Frontmatter.body content)
            else
                Ok $"Curate the captured file at: {item.FullPath}"

        match itemContent with
        | Error e -> Error e
        | Ok itemContent ->

        match resolveInstructions deps inbox item.Agent with
        | Error e -> Error e
        | Ok None -> Ok itemContent
        | Ok (Some instructions) ->
            Ok (instructions.TrimEnd() + "\n\n---\n\nCurate the following raw inbox item:\n\n" + itemContent)

    // Replaces the last "raw" path segment of the inbox's configured RawPath with
    // "archive", mirroring the `knowledge/inbox/archive/<channel>/...` layout
    // `ingestor.md` already produces by hand.
    let private archiveChannelDir (inbox: InboxConfig) (channel: string) : Result<string, string> =
        let rawPath = inbox.RawPath |> Option.defaultValue "inbox/raw"
        let segments = rawPath.TrimEnd('/').Split('/')
        if segments.Length = 0 || segments.[segments.Length - 1] <> "raw" then
            Error $"inbox rawPath '{rawPath}' does not end in a 'raw' segment — archiving is not supported for this layout."
        else
            let archiveRawPath = (Array.toList segments.[.. segments.Length - 2] @ [ "archive" ]) |> String.concat "/"
            Ok (Path.Combine(inbox.Path, archiveRawPath, channel))

    // An agent driven with real curation instructions (e.g. `ingestor.md`) may well archive
    // (and commit) the raw item itself as part of following them — its own doc says exactly
    // that ("Runs until inbox/raw/ is empty... one commit per raw item processed"). So a
    // move failing because the source is already gone isn't necessarily *our* failure: if
    // the destination already exists, the agent got there first — treat that as success
    // rather than erroring on a race that isn't really a race, just two things agreeing on
    // where the file belongs.
    let private moveOrAcceptAlreadyDone (deps: Deps) (src: string) (dst: string) : Result<unit, string> =
        match deps.MoveLocalFile src dst with
        | Ok () -> Ok ()
        | Error moveErr ->
            match deps.ReadLocalFile dst with
            | Ok (Some _) -> Ok ()
            | _ -> Error moveErr

    let private processOne (deps: Deps) (inbox: InboxConfig) (item: PooledItem) : Result<ProcessedItem, string> =
        match buildPrompt deps inbox item with
        | Error e -> Error e
        | Ok prompt ->
        match archiveChannelDir inbox item.Channel with
        | Error e -> Error e
        | Ok archiveDir ->
        match deps.RunAgent item.Agent inbox.Path prompt with
        | Error e -> Error e
        | Ok _response ->
        let archivePath = Path.Combine(archiveDir, item.FileName)
        match moveOrAcceptAlreadyDone deps item.FullPath archivePath with
        | Error e -> Error e
        | Ok () ->
        let sidecarResult =
            match item.SidecarPath with
            | None -> Ok ()
            | Some sc -> moveOrAcceptAlreadyDone deps sc (archivePath + ".meta.json")
        match sidecarResult with
        | Error e -> Error e
        | Ok () -> Ok { Channel = item.Channel; ItemPath = item.FullPath; ArchivePath = archivePath; Agent = item.Agent }

    let rec private processAll (deps: Deps) (inbox: InboxConfig) (succeeded: ProcessedItem list) (items: PooledItem list) : Result<ProcessedItem list, string> =
        match items with
        | [] -> Ok (List.rev succeeded)
        | item :: rest ->
            match processOne deps inbox item with
            | Ok p -> processAll deps inbox (p :: succeeded) rest
            | Error e -> Error $"processed {List.length succeeded} item(s) before failing on '{item.FileName}': {e}"

    let private previewOne (inbox: InboxConfig) (item: PooledItem) : Result<ProcessedItem, string> =
        archiveChannelDir inbox item.Channel
        |> Result.map (fun archiveDir ->
            { Channel = item.Channel; ItemPath = item.FullPath; ArchivePath = Path.Combine(archiveDir, item.FileName); Agent = item.Agent })

    let execute (deps: Deps) (opts: Options) : Result<ProcessedItem list, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        match resolveInbox eff opts.InboxName with
        | Error e -> Error e
        | Ok (_, inbox) ->

        match channelsInScope inbox opts.Channel with
        | Error e -> Error e
        | Ok scope ->

        match pool deps inbox scope with
        | Error e -> Error e
        | Ok items ->

        let targets =
            match opts.ItemName with
            | Some name -> selectByName name items |> Result.map List.singleton
            | None ->
                if opts.All then Ok items
                else
                    match items with
                    | [] -> Ok []
                    | first :: _ -> Ok [ first ]

        match targets with
        | Error e -> Error e
        | Ok targets ->

        if opts.DryRun then
            targets
            |> List.fold (fun acc item ->
                match acc with
                | Error e -> Error e
                | Ok list ->
                    match previewOne inbox item with
                    | Error e -> Error e
                    | Ok p -> Ok (list @ [ p ]))
                (Ok [])
        else
            processAll deps inbox [] targets

    // Diagnostic for the "nothing to process" case: `execute`'s default (no `-c`) scope
    // only ever looks at channels with an `Agent` configured, so a raw item sitting in any
    // other channel — including "default", which `inbox send` falls back to whenever no
    // `-c` is given — is silently invisible to it. This walks every subdirectory actually
    // present under `<inbox>/<rawPath>/` (not just ones registered in config) and reports
    // pending counts for whichever aren't in the agent-enabled scope, so the CLI can explain
    // *why* nothing was found instead of implying the inbox is genuinely empty. Only
    // meaningful when `-c` wasn't given explicitly — an explicit channel selection has no
    // such ambiguity to explain.
    let pendingWithoutAgent (deps: Deps) (opts: Options) : Result<(string * int) list, string> =
        match opts.Channel with
        | Some _ -> Ok []
        | None ->

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        match resolveInbox eff opts.InboxName with
        | Error e -> Error e
        | Ok (_, inbox) ->

        let rawPath = inbox.RawPath |> Option.defaultValue "inbox/raw"
        let rawRoot = Path.Combine(inbox.Path, rawPath)

        match deps.ListLocalDirectories rawRoot with
        | Error e -> Error e
        | Ok dirs ->

        let agentChannels =
            inbox.Channels
            |> Map.toList
            |> List.choose (fun (name, cc) -> if cc.Agent.IsSome then Some name else None)
            |> Set.ofList

        dirs
        |> List.map Path.GetFileName
        |> List.filter (fun name -> not (Set.contains name agentChannels))
        |> List.fold (fun acc name ->
            match acc with
            | Error e -> Error e
            | Ok pairs ->
                match deps.ListLocalFiles (Path.Combine(rawRoot, name)) with
                | Error e -> Error e
                | Ok files ->
                    let count = files |> List.map Path.GetFileName |> List.filter (fun n -> not (isSidecar n) && not (isHidden n)) |> List.length
                    Ok (if count > 0 then pairs @ [ (name, count) ] else pairs))
            (Ok [])
