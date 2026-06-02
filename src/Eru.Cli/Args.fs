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
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Url _      -> "Git URL or local path of the knowledge source."
            | Name _     -> "Override the derived source name."
            | Branch _   -> "Branch to track."
            | Basepath _ -> "Explicitly set the base path, skipping auto-detection."
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
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _   -> "Name of the source to view."
            | Full     -> "Show all files without the 20-entry cap."
            | Output _ -> "Output format: table (default), text, json."

type SourceFilesArgs =
    | [<MainCommand>]              Name    of sourceName: string
    | [<Unique>]                   Refresh
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _    -> "Name of the source. Omit to list files for all configured sources."
            | Refresh   -> "Fetch fresh metadata from the source before displaying."
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

[<CliPrefix(CliPrefix.None)>]
type SourceArgs =
    | [<SubCommand>] Add    of ParseResults<SourceAddArgs>
    | [<SubCommand>] List   of ParseResults<SourceListArgs>
    | [<SubCommand>] View   of ParseResults<SourceViewArgs>
    | [<SubCommand>] Files  of ParseResults<SourceFilesArgs>
    | [<SubCommand>] Remove of ParseResults<SourceRemoveArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add    _ -> "Add a new knowledge source."
            | List   _ -> "List configured knowledge sources."
            | View   _ -> "Show details and available files for a source."
            | Files  _ -> "List all concrete files exposed by a source, resolving any manifest glob patterns."
            | Remove _ -> "Remove a knowledge source."

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

type ManifestVerifyArgs =
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
    | [<SubCommand>] Verify of ParseResults<ManifestVerifyArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Init   _ -> "Create a new .eru/manifest.json in the current directory."
            | Add    _ -> "Add a file reference to the manifest."
            | Remove _ -> "Remove a file reference from the manifest."
            | Verify _ -> "Verify all manifest entries resolve to local files."

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

[<CliPrefix(CliPrefix.None)>]
type EruArgs =
    | [<Unique; CliPrefix(CliPrefix.DoubleDash)>] Debug
    | [<SubCommand>] Init       of ParseResults<InitArgs>
    | [<SubCommand>] Add        of ParseResults<AddArgs>
    | [<SubCommand>] Search     of ParseResults<SearchArgs>
    | [<SubCommand>] Sync       of ParseResults<SyncArgs>
    | [<SubCommand>] Source     of ParseResults<SourceArgs>
    | [<SubCommand>] Collection of ParseResults<CollectionArgs>
    | [<SubCommand>] Manifest   of ParseResults<ManifestArgs>
    | [<SubCommand>] Remove     of ParseResults<RemoveArgs>
    | [<SubCommand>] Disconnect of ParseResults<DisconnectArgs>
    | [<SubCommand>] Cache      of ParseResults<CacheArgs>
    | [<SubCommand>] Mcp        of ParseResults<McpArgs>
    | [<SubCommand>] Browse     of ParseResults<BrowseArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Debug        -> "Enable verbose/debug output (show git clone progress etc.)."
            | Init _       -> "Initialise a new eru configuration in the current repo."
            | Add _        -> "Pull a file from a knowledge source into this repo."
            | Search _     -> "Search across configured knowledge sources."
            | Sync _       -> "Synchronise local files with knowledge sources."
            | Source _     -> "Manage knowledge sources."
            | Collection _ -> "Manage collections of knowledge file references."
            | Manifest _   -> "Manage the .eru/manifest.json for this knowledge source."
            | Remove _     -> "Remove a tracked artifact from disk and the lock file."
            | Disconnect _ -> "Remove a tracked artifact from the lock file without deleting the local file."
            | Cache _      -> "Manage the local knowledge cache."
            | Mcp _        -> "Start an MCP stdio server for AI agent use."
            | Browse _     -> "Interactively browse sources and installed files."
