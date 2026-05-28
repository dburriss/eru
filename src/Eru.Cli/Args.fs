namespace Eru.Cli

open Argu

type InitArgs =
    | [<Unique>]              Force
    | [<Unique>]              Global
    | [<Unique; MainCommand>] Path of dir: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Force  -> "Overwrite existing .eru/config.json."
            | Global -> "Create the global config (~/.config/eru/config.json)."
            | Path _ -> "Directory in which to create the config (default: current directory)."

type AddArgs =
    | [<MainCommand>]              Remote_Path of remotePath: string
    | [<AltCommandLine("-t")>]     Tag        of tag: string
    | [<AltCommandLine("-s")>]     Source     of sourceName: string
    | [<AltCommandLine("-c")>]     Collection of collectionName: string
    | [<AltCommandLine("-d")>]     Target     of targetPath: string
    | [<Unique>]                   Dryrun
    | [<Unique>]                   Global
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Remote_Path _ -> "Remote path to pull (e.g. shared/templates/adr.md or source:path)."
            | Tag _         -> "Filter by tag; repeat for multiple tags (AND semantics)."
            | Source _      -> "Source name fallback when no source: prefix is used."
            | Collection _  -> "Pull all files in a named collection (e.g. name or source:name)."
            | Target _      -> "Local directory to write files into."
            | Dryrun        -> "Show what would be pulled without writing anything."
            | Global        -> "Write auto-created source to global config (~/.config/eru/config.json)."

type SearchArgs =
    | [<MainCommand>]          Terms of term: string list
    | [<AltCommandLine("-t")>] Tag   of tag: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Terms _ -> "Search terms."
            | Tag _   -> "Filter results by tag; repeat for multiple tags."

type SyncArgs =
    | Dryrun
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Dryrun -> "Show what would change without writing anything."

type SourceAddArgs =
    | [<MainCommand; ExactlyOnce>] Url      of url: string
    | [<AltCommandLine("-n")>]     Name     of name: string
    | [<AltCommandLine("-b")>]     Branch   of branch: string
    | [<AltCommandLine("-p")>]     Basepath of path: string
    | [<AltCommandLine("-g")>]     Global
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Url _      -> "Git URL or local path of the knowledge source."
            | Name _     -> "Override the derived source name."
            | Branch _   -> "Branch to track."
            | Basepath _ -> "Explicitly set the base path, skipping auto-detection."
            | Global     -> "Write to global config (~/.config/eru/config.json)."

type SourceListArgs =
    | [<Hidden>] Placeholder
    interface IArgParserTemplate with
        member a.Usage = match a with Placeholder -> ""

type SourceViewArgs =
    | [<MainCommand; ExactlyOnce>] Name of sourceName: string
    | [<Unique>]                   Full
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _ -> "Name of the source to view."
            | Full   -> "Show all files without the 20-entry cap."

[<CliPrefix(CliPrefix.None)>]
type SourceArgs =
    | [<SubCommand>] Add  of ParseResults<SourceAddArgs>
    | [<SubCommand>] List of ParseResults<SourceListArgs>
    | [<SubCommand>] View of ParseResults<SourceViewArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add  _ -> "Add a new knowledge source."
            | List _ -> "List configured knowledge sources."
            | View _ -> "Show details and available files for a source."

type McpArgs =
    | [<Hidden>] Placeholder
    interface IArgParserTemplate with
        member a.Usage = match a with Placeholder -> ""

[<CliPrefix(CliPrefix.None)>]
type EruArgs =
    | [<Unique; CliPrefix(CliPrefix.DoubleDash)>] Debug
    | [<SubCommand>] Init   of ParseResults<InitArgs>
    | [<SubCommand>] Add    of ParseResults<AddArgs>
    | [<SubCommand>] Search of ParseResults<SearchArgs>
    | [<SubCommand>] Sync   of ParseResults<SyncArgs>
    | [<SubCommand>] Source of ParseResults<SourceArgs>
    | [<SubCommand>] Mcp    of ParseResults<McpArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Debug    -> "Enable verbose/debug output (show git clone progress etc.)."
            | Init _   -> "Initialise a new eru configuration in the current repo."
            | Add _    -> "Pull a file from a knowledge source into this repo."
            | Search _ -> "Search across configured knowledge sources."
            | Sync _   -> "Synchronise local files with knowledge sources."
            | Source _ -> "Manage knowledge sources."
            | Mcp _    -> "Start an MCP stdio server for AI agent use."
