namespace Eru.Cli

open Argu

type InitArgs =
    | [<Unique>] Force
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Force -> "Overwrite existing eru.json."

type AddArgs =
    | [<MainCommand>]              Remote_Path of remotePath: string
    | [<AltCommandLine("-t")>]     Tag        of tag: string
    | [<AltCommandLine("-s")>]     Source     of sourceName: string
    | [<AltCommandLine("-c")>]     Collection of collectionName: string
    | [<AltCommandLine("-d")>]     Target     of targetPath: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Remote_Path _ -> "Remote path to pull (e.g. shared/templates/adr.md or source:path)."
            | Tag _         -> "Filter by tag; repeat for multiple tags (AND semantics)."
            | Source _      -> "Source name fallback when no source: prefix is used."
            | Collection _  -> "Pull all files in a named collection (e.g. name or source:name)."
            | Target _      -> "Local directory to write files into."

type SearchArgs =
    | [<MainCommand>]          Terms of term: string list
    | [<AltCommandLine("-t")>] Tag   of tag: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Terms _ -> "Search terms."
            | Tag _   -> "Filter results by tag; repeat for multiple tags."

type SyncArgs =
    | Dry_Run
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Dry_Run -> "Show what would change without writing anything."

type SourceAddArgs =
    | [<MainCommand; ExactlyOnce>] Url      of url: string
    | [<AltCommandLine("-n")>]     Name     of name: string
    | [<AltCommandLine("-b")>]     Branch   of branch: string
    | [<AltCommandLine("-p")>]     Basepath of path: string
    | [<AltCommandLine("-g")>]     Global
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Url _      -> "Git remote URL of the knowledge source."
            | Name _     -> "Override the derived source name."
            | Branch _   -> "Branch to track."
            | Basepath _ -> "Explicitly set the base path, skipping auto-detection."
            | Global     -> "Write to global config (~/.config/eru/config.json)."

[<CliPrefix(CliPrefix.None)>]
type SourceArgs =
    | [<SubCommand>] Add of ParseResults<SourceAddArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add _ -> "Add a new knowledge source."

[<CliPrefix(CliPrefix.None)>]
type EruArgs =
    | [<SubCommand>] Init   of ParseResults<InitArgs>
    | [<SubCommand>] Add    of ParseResults<AddArgs>
    | [<SubCommand>] Search of ParseResults<SearchArgs>
    | [<SubCommand>] Sync   of ParseResults<SyncArgs>
    | [<SubCommand>] Source of ParseResults<SourceArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Init _   -> "Initialise a new eru configuration in the current repo."
            | Add _    -> "Pull a file from a knowledge source into this repo."
            | Search _ -> "Search across configured knowledge sources."
            | Sync _   -> "Synchronise local files with knowledge sources."
            | Source _ -> "Manage knowledge sources."
