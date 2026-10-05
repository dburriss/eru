namespace Eru

open System.IO
open System.Reflection

module InboxProcess =

    type Options = {
        InboxName : string option
        Channel   : string option   // -c; None = every agent-enabled channel
        ItemName  : string option   // <name>, if given
        All       : bool
        DryRun    : bool
        Timing     : bool           // include each item's RunAgent phase timings in the result
        Append     : string list    // --append pieces (literal text, `@file`, or `@@` escape), in flag order
    }

    type ProcessedItem = {
        Channel     : string
        ItemPath    : string   // original raw path
        ArchivePath : string   // where it landed (or would land, for --dryrun)
        Agent       : AgentConfig
        Timings     : AgentTimings option   // Some only when Options.Timing is set (dry-run: always None, RunAgent never runs)
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
            | Some inbox when InboxConfig.isRemote inbox ->
                Error $"inbox '{name}' is a remote git inbox ({inbox.Path}); process and watch only support local inboxes."
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
                    Error $"multiple inboxes configured ({names}) — specify one with -i/--inbox or run 'eru inbox default <name>'."

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
        let dir = PathJoin.Combine(inbox.Path, rawPath, channel)
        match deps.ListLocalFiles dir with
        | Error e -> Error e
        | Ok files ->
            let names = files |> List.map PathUtil.fileName
            let nameSet = Set.ofList names
            names
            |> List.filter (fun name -> not (isSidecar name) && not (isHidden name))
            |> List.map (fun name ->
                let sidecarName = name + ".meta.json"
                let sidecar = if Set.contains sidecarName nameSet then Some (PathJoin.Combine(dir, sidecarName)) else None
                { Channel = channel; Agent = agent; FileName = name; FullPath = PathJoin.Combine(dir, name); SidecarPath = sidecar })
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

    // Tier 2 of the implicit fallback chain: known coding-agent CLIs each already have
    // their own well-known home for a project's agent/subagent definitions. Keyed by the
    // launched command's basename (directory components and a trailing `.exe` stripped) —
    // `Args` is ignored entirely, since it's the executable identity, not how it's
    // invoked, that determines which tool's convention applies. The curated file is always
    // named "ingestor" (matching this repo's own `agents/ingestor.agent.md`), not a
    // per-agent registry of arbitrary names.
    let private apmInstructionsPaths =
        Map.ofList [
            "claude",       ".claude/agents/ingestor.md"
            "opencode",     ".opencode/agents/ingestor.md"
            "cursor-agent", ".cursor/agents/ingestor.md"
            "codex",        ".codex/agents/ingestor.md"
            "copilot",      ".github/agents/ingestor.agent.md"
        ]

    // Strips directory components and a trailing `.exe` (case-insensitive) from an
    // `AgentConfig.Command` string, so "claude", "/usr/local/bin/claude" and
    // "C:\tools\claude.exe" all resolve to the same apm lookup key.
    let private commandBasename (command: string) : string =
        let name = PathUtil.fileName command
        if name.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase)
        then name.Substring(0, name.Length - 4)
        else name

    // Tier 3, the final fallback: eru's own built-in curation instructions, embedded from
    // the real, checked-in `agents/ingestor.agent.md` (repo root) at build time — see
    // `Eru.Domain.fsproj`'s `EmbeddedResource`. Single source of truth; never duplicated
    // as a string literal here. Always succeeds (barring a build misconfiguration), so
    // it's read once and memoized rather than re-read per item.
    let private defaultInstructionsResourceName = "Eru.Domain.DefaultIngestorInstructions.md"

    let private defaultInstructions : Lazy<string> =
        lazy (
            let asm = Assembly.GetExecutingAssembly()
            use stream = asm.GetManifestResourceStream(defaultInstructionsResourceName)
            if isNull stream then
                failwith $"embedded resource '{defaultInstructionsResourceName}' not found in {asm.FullName} — was it removed from Eru.Domain.fsproj?"
            use reader = new StreamReader(stream)
            reader.ReadToEnd()
        )

    // The bare raw content alone carries no instructions for what an otherwise-generic ACP
    // agent should actually do with it — this resolves the instructions to prepend to the
    // prompt so the agent knows to curate rather than just reply conversationally. An
    // explicitly-configured `InstructionsPath` that doesn't resolve is a real error
    // (misconfiguration). The implicit chain (repo convention → tool-specific apm
    // convention → eru's own built-in default) is best-effort at each of its first two
    // tiers but always terminates in real content, since the built-in default is
    // unconditional.
    let private resolveInstructions (deps: Deps) (inbox: InboxConfig) (agent: AgentConfig) : Result<string, string> =
        match agent.InstructionsPath with
        | Some p ->
            let path = if Path.IsPathRooted p then p else PathJoin.Combine(inbox.Path, p)
            match deps.ReadLocalFile path with
            | Error e -> Error e
            | Ok (Some content) -> Ok content
            | Ok None -> Error $"agent instructions file '{path}' not found."
        | None ->
            let defaultPath = PathJoin.Combine(inbox.Path, defaultInstructionsRelPath)
            match deps.ReadLocalFile defaultPath with
            | Error e -> Error e
            | Ok (Some content) -> Ok content
            | Ok None ->
                match Map.tryFind (commandBasename agent.Command) apmInstructionsPaths with
                | None -> Ok defaultInstructions.Value
                | Some apmRelPath ->
                    let apmPath = PathJoin.Combine(inbox.Path, apmRelPath)
                    match deps.ReadLocalFile apmPath with
                    | Error e -> Error e
                    | Ok (Some content) -> Ok content
                    | Ok None -> Ok defaultInstructions.Value

    // Resolves one `--append` value: `@@text` is a literal "@text", `@path` reads the file
    // (relative paths against the current directory, where the user typed the flag), and
    // anything else is used as-is.
    let private resolveAppendOne (deps: Deps) (raw: string) : Result<string, string> =
        if raw.StartsWith "@@" then Ok (raw.Substring 1)
        elif raw.StartsWith "@" then
            let p = raw.Substring 1
            let path = if Path.IsPathRooted p then p else PathJoin.Combine(deps.GetCwd (), p)
            match deps.ReadLocalFile path with
            | Error e -> Error e
            | Ok None -> Error $"append file '{path}' not found."
            | Ok (Some content) -> Ok content
        else Ok raw

    let private resolveAppends (deps: Deps) (raws: string list) : Result<string list, string> =
        raws
        |> List.fold (fun acc raw ->
            match acc with
            | Error e -> Error e
            | Ok list -> resolveAppendOne deps raw |> Result.map (fun a -> list @ [ a ]))
            (Ok [])

    let private buildPrompt (deps: Deps) (inbox: InboxConfig) (appends: string list) (item: PooledItem) : Result<string, string> =
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
        | Ok instructions ->
            let appended = appends |> List.map (fun a -> "\n\n" + a.Trim()) |> String.concat ""
            Ok (instructions.TrimEnd() + appended + "\n\n---\n\nCurate the following raw inbox item:\n\n" + itemContent)

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
            Ok (PathJoin.Combine(inbox.Path, archiveRawPath, channel))

    // An agent driven with real curation instructions (e.g. `ingestor.md`) may well archive
    // (and commit) the raw item itself as part of following them — its own doc says exactly
    // that (an ingestor-style agent follows its curation cycle through to archiving). So a
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

    let private processOne (deps: Deps) (inbox: InboxConfig) (debug: bool) (appends: string list) (onChunk: string -> unit) (item: PooledItem) : Result<ProcessedItem, string> =
        match buildPrompt deps inbox appends item with
        | Error e -> Error e
        | Ok prompt ->
        match archiveChannelDir inbox item.Channel with
        | Error e -> Error e
        | Ok archiveDir ->
        match deps.RunAgent item.Agent inbox.Path prompt onChunk with
        | Error e -> Error e
        | Ok runResult ->
        let archivePath = PathJoin.Combine(archiveDir, item.FileName)
        match moveOrAcceptAlreadyDone deps item.FullPath archivePath with
        | Error e -> Error e
        | Ok () ->
        let sidecarResult =
            match item.SidecarPath with
            | None -> Ok ()
            | Some sc -> moveOrAcceptAlreadyDone deps sc (archivePath + ".meta.json")
        match sidecarResult with
        | Error e -> Error e
        | Ok () ->
            let timings = if debug then Some runResult.Timings else None
            Ok { Channel = item.Channel; ItemPath = item.FullPath; ArchivePath = archivePath; Agent = item.Agent; Timings = timings }

    let rec private processAll (deps: Deps) (inbox: InboxConfig) (debug: bool) (appends: string list) (onItemStart: int -> int -> string -> unit) (onChunk: string -> unit) (total: int) (succeeded: ProcessedItem list) (items: PooledItem list) : Result<ProcessedItem list, string> =
        match items with
        | [] -> Ok (List.rev succeeded)
        | item :: rest ->
            onItemStart (total - List.length rest) total item.FileName
            match processOne deps inbox debug appends onChunk item with
            | Ok p -> processAll deps inbox debug appends onItemStart onChunk total (p :: succeeded) rest
            | Error e -> Error $"processed {List.length succeeded} item(s) before failing on '{item.FileName}': {e}"

    let private previewOne (inbox: InboxConfig) (item: PooledItem) : Result<ProcessedItem, string> =
        archiveChannelDir inbox item.Channel
        |> Result.map (fun archiveDir ->
            { Channel = item.Channel; ItemPath = item.FullPath; ArchivePath = PathJoin.Combine(archiveDir, item.FileName); Agent = item.Agent; Timings = None })

    let private executeCore (deps: Deps) (opts: Options) (onItemStart: int -> int -> string -> unit) (onChunk: string -> unit) : Result<ProcessedItem list, string> =
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

        match resolveAppends deps opts.Append with
        | Error e -> Error e
        | Ok appends ->

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
            processAll deps inbox opts.Timing appends onItemStart onChunk (List.length targets) [] targets

    let execute (deps: Deps) (opts: Options) : Result<ProcessedItem list, string> =
        executeCore deps opts (fun _ _ _ -> ()) (fun _ -> ())

    // Same as `execute`, but with two live-progress hooks so a caller (e.g. the CLI) can
    // render feedback for what would otherwise be a silent, potentially slow batch of
    // external agent calls: `onItemStart` fires once per item just before it starts
    // processing (1-based index, total, file name); `onChunk` fires with each fragment
    // of text the current item's agent streams back as it works.
    let executeWithProgress (deps: Deps) (opts: Options) (onItemStart: int -> int -> string -> unit) (onChunk: string -> unit) : Result<ProcessedItem list, string> =
        executeCore deps opts onItemStart onChunk

    type WatchTarget = {
        RawRootDir : string   // <inbox.Path>/<rawPath> — one level above every channel's raw subdirectory
        InboxName  : string
        Channels   : string list
    }

    // Resolves inbox/channel scope the same way `execute` does — same errors (inbox/channel
    // not configured, no agent configured) — without pooling or processing anything. Used by
    // `inbox watch` to fail fast on start and to learn the single directory a `FileSystemWatcher`
    // needs to root at to cover every channel in scope at once.
    let resolveWatchTarget (deps: Deps) (opts: Options) : Result<WatchTarget, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        match resolveInbox eff opts.InboxName with
        | Error e -> Error e
        | Ok (inboxName, inbox) ->

        match channelsInScope inbox opts.Channel with
        | Error e -> Error e
        | Ok scope ->

        let rawPath = inbox.RawPath |> Option.defaultValue "inbox/raw"
        Ok {
            RawRootDir = PathJoin.Combine(inbox.Path, rawPath)
            InboxName  = inboxName
            Channels   = scope |> List.map fst
        }

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
        let rawRoot = PathJoin.Combine(inbox.Path, rawPath)

        match deps.ListLocalDirectories rawRoot with
        | Error e -> Error e
        | Ok dirs ->

        let agentChannels =
            inbox.Channels
            |> Map.toList
            |> List.choose (fun (name, cc) -> if cc.Agent.IsSome then Some name else None)
            |> Set.ofList

        dirs
        |> List.map PathUtil.fileName
        |> List.filter (fun name -> not (Set.contains name agentChannels))
        |> List.fold (fun acc name ->
            match acc with
            | Error e -> Error e
            | Ok pairs ->
                match deps.ListLocalFiles (PathJoin.Combine(rawRoot, name)) with
                | Error e -> Error e
                | Ok files ->
                    let count = files |> List.map PathUtil.fileName |> List.filter (fun n -> not (isSidecar n) && not (isHidden n)) |> List.length
                    Ok (if count > 0 then pairs @ [ (name, count) ] else pairs))
            (Ok [])
