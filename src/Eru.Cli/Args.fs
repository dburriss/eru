namespace Eru.Cli

open Argu

type InitArgs =
    | [<Unique>]                   Force
    | [<Unique>]                   Global
    | [<Unique; MainCommand>]      Path   of dir: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Force    -> "Overwrite existing .eru/config.json."
            | Global   -> "Create the global config (~/.config/eru/config.json)."
            | Path _   -> "Directory in which to create the config (default: current directory)."
            | Output _ -> "Output format: table (default), text, json."

type AddArgs =
    | [<MainCommand>]              Remote_Path of remotePath: string
    | [<AltCommandLine("-t")>]     Tag        of tag: string
    | [<AltCommandLine("-s")>]     Source     of sourceName: string
    | [<AltCommandLine("-c")>]     Collection of collectionName: string
    | [<AltCommandLine("-d")>]     Target     of targetPath: string
    | [<Unique>]                   Dryrun
    | [<Unique>]                   Global
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Remote_Path _ -> "Remote path to pull (e.g. shared/templates/adr.md or source:path)."
            | Tag _         -> "Filter by tag; repeat for multiple tags (AND semantics)."
            | Source _      -> "Source name fallback when no source: prefix is used."
            | Collection _  -> "Pull all files in a named collection (e.g. name or source:name)."
            | Target _      -> "Local path for the pulled file; append a trailing / to treat as a directory (keeps original filename)."
            | Dryrun        -> "Show what would be pulled without writing anything."
            | Global        -> "Write auto-created source to global config (~/.config/eru/config.json)."
            | Output _      -> "Output format: table (default), text, json."

type SearchArgs =
    | [<MainCommand>]              Terms  of term: string list
    | [<AltCommandLine("-t")>]     Tag    of tag: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Terms _  -> "Search terms."
            | Tag _    -> "Filter results by tag; repeat for multiple tags."
            | Output _ -> "Output format: table (default), text, json."

type SyncArgs =
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Dryrun   -> "Show what would change without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type SourceAddArgs =
    | [<MainCommand; ExactlyOnce>] Url      of url: string
    | [<AltCommandLine("-n")>]     Name     of name: string
    | [<AltCommandLine("-b")>]     Branch   of branch: string
    | [<AltCommandLine("-p")>]     Basepath of path: string
    | [<Unique>]                   Scan
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Url _      -> "Git URL or local path of the knowledge source."
            | Name _     -> "Override the derived source name."
            | Branch _   -> "Branch to track."
            | Basepath _ -> "Explicitly register this path as a manifest bundle."
            | Scan       -> "Scan the source for OKF bundles (index.md with okf_version) or the knowledge/ convention and register them."
            | Global     -> "Write to global config (~/.config/eru/config.json)."
            | Dryrun     -> "Show what would be added without writing anything."
            | Output _   -> "Output format: table (default), text, json."

type SourceListArgs =
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _ -> "Output format: table (default), text, json."

type SourceViewArgs =
    | [<MainCommand; ExactlyOnce>] Name of sourceName: string
    | [<Unique>]                   Full
    | [<Unique; AltCommandLine("-b")>] Bundle of path: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _   -> "Name of the source to view."
            | Full     -> "Show all files without the 20-entry cap."
            | Bundle _ -> "Restrict output to one bundle's group (path, or '.'/'/' for the repo root)."
            | Output _ -> "Output format: table (default), text, json."

type SourceFilesArgs =
    | [<MainCommand>]              Name    of sourceName: string
    | [<Unique>]                   Refresh
    | [<Unique; AltCommandLine("-b")>] Bundle of path: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _    -> "Name of the source. Omit to list files for all configured sources."
            | Refresh   -> "Fetch fresh metadata from the source before displaying."
            | Bundle _  -> "Restrict output to one bundle's group (path, or '.'/'/' for the repo root)."
            | Output _  -> "Output format: table (default), text, json."

type SourceRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Name   of name: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _   -> "Name of the source to remove."
            | Global   -> "Remove from global config (~/.config/eru/config.json)."
            | Dryrun   -> "Show what would be removed without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type SourceBundleAddArgs =
    | [<MainCommand; ExactlyOnce>] Source_And_Path of source: string * path: string
    | [<AltCommandLine("-k")>]     Kind            of kind: string
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output      of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Source_And_Path _ -> "Source name and bundle path (e.g. mysource docs/knowledge; '.' or '/' for the repo root)."
            | Kind _            -> "Bundle kind: manifest (default) or okf. Auto-detected from index.md when omitted."
            | Dryrun            -> "Show what would be added without writing anything."
            | Output _          -> "Output format: table (default), text, json."

type SourceBundleListArgs =
    | [<MainCommand; ExactlyOnce>] Source of source: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Source _ -> "Name of the source."
            | Output _ -> "Output format: table (default), text, json."

type SourceBundleRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Source_And_Path of source: string * path: string
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output      of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Source_And_Path _ -> "Source name and bundle path to remove (e.g. mysource docs/knowledge; '.' or '/' for the repo root)."
            | Dryrun            -> "Show what would be removed without writing anything."
            | Output _          -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type SourceBundleArgs =
    | [<SubCommand>] Add    of ParseResults<SourceBundleAddArgs>
    | [<SubCommand>] List   of ParseResults<SourceBundleListArgs>
    | [<SubCommand>] Remove of ParseResults<SourceBundleRemoveArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add    _ -> "Register a bundle (a directory a source publishes from) on an existing source."
            | List   _ -> "List a source's registered bundles."
            | Remove _ -> "Remove a bundle from a source."

[<CliPrefix(CliPrefix.None)>]
type SourceArgs =
    | [<SubCommand>] Add    of ParseResults<SourceAddArgs>
    | [<SubCommand>] List   of ParseResults<SourceListArgs>
    | [<SubCommand>] View   of ParseResults<SourceViewArgs>
    | [<SubCommand>] Files  of ParseResults<SourceFilesArgs>
    | [<SubCommand>] Remove of ParseResults<SourceRemoveArgs>
    | [<SubCommand>] Bundle of ParseResults<SourceBundleArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add    _ -> "Add a new knowledge source."
            | List   _ -> "List configured knowledge sources."
            | View   _ -> "Show details and available files for a source."
            | Files  _ -> "List all concrete files exposed by a source, resolving any manifest glob patterns."
            | Remove _ -> "Remove a knowledge source."
            | Bundle _ -> "Manage a source's bundles."

type InboxAddArgs =
    | [<MainCommand; ExactlyOnce>] Name_And_Path   of name: string * path: string
    | Raw_Path        of path: string
    | Default_Channel of channel: string
    | Branch          of branch: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name_And_Path _   -> "Inbox name and local directory path, or a git repo URL for a remote inbox (e.g. knowledge /path/to/knowledge, or knowledge https://github.com/org/repo)."
            | Branch _          -> "Remote inbox only: branch 'inbox send' pushes to (default: the repo's default branch)."
            | Raw_Path _        -> "Path within the inbox directory to the raw capture folder (default: inbox/raw)."
            | Default_Channel _ -> "Channel 'inbox send' falls back to when -c is omitted (default: default)."
            | Global            -> "Write to global config (~/.config/eru/config.json)."
            | Dryrun            -> "Show what would be added without writing anything."
            | Output _          -> "Output format: table (default), text, json."

type InboxListArgs =
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _ -> "Output format: table (default), text, json."

type InboxRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Name of name: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _   -> "Name of the inbox to remove."
            | Global   -> "Remove from global config (~/.config/eru/config.json)."
            | Dryrun   -> "Show what would be removed without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type InboxDefaultArgs =
    | [<MainCommand; ExactlyOnce>] Name of name: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _   -> "Name of the configured inbox to use as the default."
            | Global   -> "Set in global config (~/.config/eru/config.json)."
            | Dryrun   -> "Show what would be set without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type InboxChannelAddArgs =
    | [<MainCommand; ExactlyOnce>] Inbox_And_Channel of inbox: string * channel: string
    | [<Unique>]                    Agent_Protocol     of protocol: string
    | [<Unique>]                    Agent_Command      of command: string
    | Agent_Args                    of arg: string
    | [<Unique>]                    Agent_Instructions of path: string
    | [<Unique>]                    Agent_Timeout      of seconds: int
    | [<AltCommandLine("-d")>]      Description of desc: string
    | [<Unique>]                    Dryrun
    | [<Unique; AltCommandLine("-o")>] Output   of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Inbox_And_Channel _  -> "Inbox name and channel name (e.g. knowledge eru)."
            | Agent_Protocol _     -> "Protocol the channel's agent speaks — only 'acp' is supported (default: acp)."
            | Agent_Command _      -> "Executable that launches the channel's agent (e.g. opencode)."
            | Agent_Args _         -> "Argument to pass the agent's command (repeatable, e.g. --agent-args acp)."
            | Agent_Instructions _ -> "Path (absolute, or relative to the inbox) to a file prepended to every prompt this agent receives — e.g. an ingestor.md agent definition. Default: <inbox>/.agents/agents/ingestor.md if it exists, else a tool-specific convention for --agent-command (e.g. .claude/agents/ingestor.md, .opencode/agents/ingestor.md), else eru's own built-in curation instructions."
            | Agent_Timeout _      -> "Idle timeout in seconds: how long to wait with no activity from this agent before giving up (resets on every update it streams). Default: 120."
            | Description _        -> "Short description of the channel."
            | Dryrun                -> "Show what would be added without writing anything."
            | Output _              -> "Output format: table (default), text, json."

type InboxChannelListArgs =
    | [<MainCommand; ExactlyOnce>] Inbox of inbox: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Inbox _  -> "Name of the inbox."
            | Output _ -> "Output format: table (default), text, json."

type InboxChannelRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Inbox_And_Channel of inbox: string * channel: string
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output      of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Inbox_And_Channel _ -> "Inbox name and channel name to remove (e.g. knowledge eru)."
            | Dryrun              -> "Show what would be removed without writing anything."
            | Output _            -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type InboxChannelArgs =
    | [<SubCommand>] Add    of ParseResults<InboxChannelAddArgs>
    | [<SubCommand>] List   of ParseResults<InboxChannelListArgs>
    | [<SubCommand>] Remove of ParseResults<InboxChannelRemoveArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add    _ -> "Register a channel on an existing inbox."
            | List   _ -> "List an inbox's registered channels."
            | Remove _ -> "Remove a channel from an inbox."

type InboxSendArgs =
    | [<MainCommand>]              Content of content: string
    | [<AltCommandLine("-i")>]     Inbox   of inbox: string
    | [<AltCommandLine("-c")>]     Channel of channel: string
    | [<AltCommandLine("-t")>]     Title   of title: string
    | [<AltCommandLine("-n")>]     Note    of note: string
    | [<Unique>]                   As      of kind: string
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Content _ -> "Message text, a local file path, or a URL. Reads stdin if omitted."
            | Inbox _   -> "Name of the configured inbox to send into. Auto-resolved when only one is configured."
            | Channel _ -> "Channel within the inbox (default: the inbox's default channel, or \"default\")."
            | Title _   -> "Explicit filename slug, overriding the auto-derived one."
            | Note _    -> "Extra context text folded into the body of a message/url capture."
            | As _      -> "Force content-type classification: message, file, or url."
            | Dryrun    -> "Show the resolved target path without writing anything."
            | Output _  -> "Output format: table (default), text, json."

type InboxProcessArgs =
    | [<MainCommand>]              Item    of name: string
    | [<AltCommandLine("-i")>]     Inbox   of inbox: string
    | [<AltCommandLine("-c")>]     Channel of channel: string
    | [<Unique>]                   All
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    | Append of text: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Append _  -> "Text appended to the agent instructions, before the item (literal, or @file to read a file; @@ for a literal leading @). Repeatable."
            | Item _    -> "Process this specific item instead of the oldest (exact filename or stem)."
            | Inbox _   -> "Name of the configured inbox to process. Auto-resolved when only one is configured."
            | Channel _ -> "Restrict to one channel (default: every channel with an agent configured)."
            | All       -> "Process every pending item in scope, oldest first, stopping at the first failure."
            | Dryrun    -> "Show which item(s) and agent(s) would be used, without spawning anything or moving files."
            | Output _  -> "Output format: table (default), text, json."

type InboxWatchArgs =
    | [<AltCommandLine("-i")>]     Inbox    of inbox: string
    | [<AltCommandLine("-c")>]     Channel  of channel: string
    | [<Unique>]                   Interval of seconds: int
    | [<Unique>]                   Dryrun
    | Append of text: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Append _   -> "Text appended to the agent instructions, before each item (literal, or @file to read a file; @@ for a literal leading @). Repeatable."
            | Inbox _    -> "Name of the configured inbox to watch. Auto-resolved when only one is configured."
            | Channel _  -> "Restrict to one channel (default: every channel with an agent configured)."
            | Interval _ -> "Polling fallback interval in seconds, in case filesystem events are missed (default: 30, or settings.inboxWatchIntervalSeconds from config)."
            | Dryrun     -> "Log what each trigger would process without spawning an agent or moving files."

[<CliPrefix(CliPrefix.None)>]
type InboxArgs =
    | [<SubCommand>] Add     of ParseResults<InboxAddArgs>
    | [<SubCommand>] List    of ParseResults<InboxListArgs>
    | [<SubCommand>] Remove  of ParseResults<InboxRemoveArgs>
    | [<SubCommand>] Default of ParseResults<InboxDefaultArgs>
    | [<SubCommand>] Channel of ParseResults<InboxChannelArgs>
    | [<SubCommand>] Send    of ParseResults<InboxSendArgs>
    | [<SubCommand>] Process of ParseResults<InboxProcessArgs>
    | [<SubCommand>] Watch   of ParseResults<InboxWatchArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add     _ -> "Register a local directory as an inbox."
            | List    _ -> "List configured inboxes."
            | Remove  _ -> "Remove an inbox."
            | Default _ -> "Set the inbox used when -i is omitted."
            | Channel _ -> "Manage an inbox's channels."
            | Send    _ -> "Send a message, file, or URL into a configured inbox."
            | Process _ -> "Curate a raw inbox item via its channel's configured agent."
            | Watch   _ -> "Watch an inbox and auto-process new items as they arrive."

type CollectionCreateArgs =
    | [<MainCommand; ExactlyOnce>] Name        of name: string
    | [<AltCommandLine("-t")>]     Tag         of tag: string
    | [<AltCommandLine("-d")>]     Description of desc: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output  of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _        -> "Name of the new collection."
            | Tag _         -> "Tag for the collection; repeat for multiple tags."
            | Description _ -> "Short description of the collection."
            | Global        -> "Write to global config (~/.config/eru/config.json)."
            | Dryrun        -> "Show what would be created without writing anything."
            | Output _      -> "Output format: table (default), text, json."

type CollectionAddArgs =
    | [<MainCommand; ExactlyOnce>] Collection   of name: string
    | [<AltCommandLine("-f"); ExactlyOnce>] File of sourceAndPath: string
    | [<AltCommandLine("-t")>]     Tag          of tag: string
    | [<AltCommandLine("-d")>]     Description  of desc: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output   of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Collection _  -> "Name of the collection to add the file to."
            | File _        -> "File reference as source:remotePath (e.g. gh-repo:docs/guide.md)."
            | Tag _         -> "Tag for the file reference; repeat for multiple tags."
            | Description _ -> "Short description of the file reference."
            | Global        -> "Write to global config (~/.config/eru/config.json)."
            | Dryrun        -> "Show what would be added without writing anything."
            | Output _      -> "Output format: table (default), text, json."

type CollectionRemoveFileArgs =
    | [<MainCommand; ExactlyOnce>] Collection   of name: string
    | [<AltCommandLine("-f"); ExactlyOnce>] File of sourceAndPath: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output   of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Collection _  -> "Name of the collection."
            | File _        -> "File reference to remove as source:remotePath (e.g. gh-repo:docs/guide.md)."
            | Global        -> "Write to global config (~/.config/eru/config.json)."
            | Dryrun        -> "Show what would be removed without writing anything."
            | Output _      -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type CollectionArgs =
    | [<SubCommand>] Create of ParseResults<CollectionCreateArgs>
    | [<SubCommand>] Add    of ParseResults<CollectionAddArgs>
    | [<SubCommand>] Remove of ParseResults<CollectionRemoveFileArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Create _ -> "Create a new collection."
            | Add    _ -> "Add a file reference to an existing collection."
            | Remove _ -> "Remove a file reference from an existing collection."

type ManifestInitArgs =
    | [<Unique>]                   Force
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Force    -> "Overwrite an existing .eru/manifest.json."
            | Output _ -> "Output format: table (default), text, json."

type ManifestAddArgs =
    | [<MainCommand; ExactlyOnce>] Path        of path: string
    | [<AltCommandLine("-t")>]     Tag         of tag: string
    | [<AltCommandLine("-d")>]     Description of desc: string
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output  of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _        -> "File path or glob pattern to add (e.g. docs/*.md)."
            | Tag _         -> "Tag for the entry; repeat for multiple tags."
            | Description _ -> "Short description of the entry."
            | Dryrun        -> "Show what would be added without writing anything."
            | Output _      -> "Output format: table (default), text, json."

type ManifestRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Path   of path: string
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _   -> "Exact path to remove from the manifest."
            | Dryrun   -> "Show what would be removed without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type ManifestValidateArgs =
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _ -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type ManifestArgs =
    | [<SubCommand>] Init   of ParseResults<ManifestInitArgs>
    | [<SubCommand>] Add    of ParseResults<ManifestAddArgs>
    | [<SubCommand>] Remove of ParseResults<ManifestRemoveArgs>
    | [<SubCommand; AltCommandLine("verify")>] Validate of ParseResults<ManifestValidateArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Init     _ -> "Create a new .eru/manifest.json in the current directory."
            | Add      _ -> "Add a file reference to the manifest."
            | Remove   _ -> "Remove a file reference from the manifest."
            | Validate _ -> "Validate all manifest entries resolve to local files (alias: verify)."

type RemoveArgs =
    | [<MainCommand; ExactlyOnce>]           Target of target: string
    | [<Unique>]                             Dryrun
    | [<Unique; AltCommandLine("-o")>]       Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Target _ -> "Local path or path short hash of the artifact to remove."
            | Dryrun   -> "Show what would be removed without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type DisconnectArgs =
    | [<MainCommand; ExactlyOnce>]           Target of target: string
    | [<Unique>]                             Dryrun
    | [<Unique; AltCommandLine("-o")>]       Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Target _ -> "Local path or path short hash of the artifact to disconnect."
            | Dryrun   -> "Show what would be disconnected without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type CachePruneArgs =
    | [<Unique>] Force
    interface IArgParserTemplate with
        member a.Usage = match a with Force -> "Skip confirmation prompt and delete immediately."

type CacheClearArgs =
    | [<Unique>]                         Dryrun
    | [<Unique>]                         Force
    | [<Unique; AltCommandLine("-o")>]   Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Dryrun   -> "List what would be deleted without deleting anything."
            | Force    -> "Skip confirmation prompt and delete immediately."
            | Output _ -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type CacheArgs =
    | [<SubCommand>] Prune of ParseResults<CachePruneArgs>
    | [<SubCommand>] Clear of ParseResults<CacheClearArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Prune _ -> "Remove orphaned content files not referenced by any source index."
            | Clear _ -> "Delete all cached indexes and files."

type BrowseArgs =
    | [<Hidden>] Placeholder
    interface IArgParserTemplate with
        member a.Usage = match a with Placeholder -> ""

type McpArgs =
    | [<Hidden>] Placeholder
    interface IArgParserTemplate with
        member a.Usage = match a with Placeholder -> ""

type SiteGenerateArgs =
    | [<Unique; AltCommandLine("-o")>] Output     of dir: string
    | [<Unique>]                       Open
    | [<Unique>]                       Custom_Css of path: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _     -> "Output directory (default: ./cache-site/)."
            | Open         -> "Open the generated site in the default browser."
            | Custom_Css _ -> "Path to a CSS file appended after style.css on every run."

type SiteServeArgs =
    | [<AltCommandLine("-o")>] Output        of dir: string
    | [<AltCommandLine("-p")>] Port          of port: int
    | Open
    | Sync_Interval                          of minutes: int
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _        -> "Site output directory (default: ./cache-site/)."
            | Port _          -> "HTTP port (default: 5173)."
            | Open            -> "Open the browser automatically when the server starts."
            | Sync_Interval _ -> "Minutes between background cache syncs (default: 15)."

[<CliPrefix(CliPrefix.None)>]
type SiteArgs =
    | [<SubCommand>] Generate of ParseResults<SiteGenerateArgs>
    | [<SubCommand>] Serve    of ParseResults<SiteServeArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Generate _ -> "Generate a static HTML site from the local cache index."
            | Serve _    -> "Serve the site locally with live reload and search API."

type OkfValidateArgs =
    | [<MainCommand; ExactlyOnce>] Path of path: string
    | [<Unique>] Strict_Links
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _        -> "Directory to validate against OKF §11."
            | Strict_Links  -> "Fail (exit 1) on broken links, images, wikilinks and anchors instead of warning."
            | Output _      -> "Output format: table (default), text, json."

type OkfInitArgs =
    | [<MainCommand; ExactlyOnce>] Path of path: string
    | [<Unique>]                   Dry_Run
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _   -> "Directory to scaffold index.md files in."
            | Dry_Run  -> "Report what would be created without writing anything."
            | Output _ -> "Output format: table (default), text, json."

type OkfFixArgs =
    | [<MainCommand; ExactlyOnce>] Path of path: string
    | [<Unique>]                   Dry_Run
    | [<Unique>]                   Default_Type of typ: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _         -> "Directory to repair so it passes `eru okf validate`."
            | Dry_Run        -> "Report what would change without writing anything."
            | Default_Type _ -> "`type` to give concept files that have none (default: reference)."
            | Output _       -> "Output format: table (default), text, json."

type OkfVerifyArgs =
    | [<MainCommand; ExactlyOnce>] File of file: string
    | [<Unique>]                   By of actor: string
    | [<Unique>]                   At of timestamp: string
    | [<Unique>]                   Dry_Run
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | File _   -> "Concept .md file to record a verification on."
            | By _     -> "Verifying actor: human:<id> or a machine actor name (default: human:<git user.email>)."
            | At _     -> "ISO 8601 timestamp with a UTC offset (default: now)."
            | Dry_Run  -> "Report what would change without writing anything."
            | Output _ -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type OkfArgs =
    | [<SubCommand>] Validate of ParseResults<OkfValidateArgs>
    | [<SubCommand>] Init     of ParseResults<OkfInitArgs>
    | [<SubCommand>] Fix      of ParseResults<OkfFixArgs>
    | [<SubCommand>] Verify   of ParseResults<OkfVerifyArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Validate _ -> "Validate a directory tree for OKF conformance (§11)."
            | Init _     -> "Create missing OKF index.md files (never overwrites)."
            | Fix _      -> "Repair a directory tree so it passes OKF conformance."
            | Verify _   -> "Record a verification (by, at) on a concept file."

type GraphArgs =
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    | [<Unique; AltCommandLine("-s")>] Source of sourceName: string
    | [<Unique>]                       Dot
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _ -> "Output format: table (default), text, json."
            | Source _ -> "Restrict the graph to a single source."
            | Dot      -> "Emit Graphviz DOT format instead (takes precedence over --output)."

[<CliPrefix(CliPrefix.None)>]
type EruArgs =
    | [<Unique; CliPrefix(CliPrefix.DoubleDash)>] Debug
    | [<SubCommand>] Init       of ParseResults<InitArgs>
    | [<SubCommand>] Add        of ParseResults<AddArgs>
    | [<SubCommand>] Search     of ParseResults<SearchArgs>
    | [<SubCommand>] Sync       of ParseResults<SyncArgs>
    | [<SubCommand>] Source     of ParseResults<SourceArgs>
    | [<SubCommand>] Inbox      of ParseResults<InboxArgs>
    | [<SubCommand>] Collection of ParseResults<CollectionArgs>
    | [<SubCommand>] Manifest   of ParseResults<ManifestArgs>
    | [<SubCommand>] Remove     of ParseResults<RemoveArgs>
    | [<SubCommand>] Disconnect of ParseResults<DisconnectArgs>
    | [<SubCommand>] Cache      of ParseResults<CacheArgs>
    | [<SubCommand>] Mcp        of ParseResults<McpArgs>
    | [<SubCommand>] Browse     of ParseResults<BrowseArgs>
    | [<SubCommand>] Site       of ParseResults<SiteArgs>
    | [<SubCommand>] Okf        of ParseResults<OkfArgs>
    | [<SubCommand>] Graph      of ParseResults<GraphArgs>
    | Version
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Debug        -> "Enable verbose/debug output (show git clone progress etc.)."
            | Init _       -> "Initialise a new eru configuration in the current repo."
            | Add _        -> "Pull a file from a knowledge source into this repo."
            | Search _     -> "Search across configured knowledge sources."
            | Sync _       -> "Synchronise local files with knowledge sources."
            | Source _     -> "Manage knowledge sources."
            | Inbox _      -> "Manage inboxes and send messages, files, or URLs into them."
            | Collection _ -> "Manage collections of knowledge file references."
            | Manifest _   -> "Manage the .eru/manifest.json for this knowledge source."
            | Remove _     -> "Remove a tracked artifact from disk and the lock file."
            | Disconnect _ -> "Remove a tracked artifact from the lock file without deleting the local file."
            | Cache _      -> "Manage the local knowledge cache."
            | Mcp _        -> "Start an MCP stdio server for AI agent use."
            | Browse _     -> "Interactively browse sources and tracked files."
            | Site _       -> "Generate a static HTML site for browsing the knowledge cache."
            | Okf _        -> "Validate, scaffold and repair a knowledge bundle for OKF conformance."
            | Graph _      -> "Show the link graph between cached documents and external URLs."
            | Version      -> "Print the eru version and commit."
