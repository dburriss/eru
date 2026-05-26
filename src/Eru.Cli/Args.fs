namespace Eru.Cli

open Argu

type InitArgs =
    | [<Unique>] Force
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Force -> "Overwrite existing eru.json."

type AddArgs =
    | [<MainCommand; ExactlyOnce>] Remote_Path of remotePath: string
    | [<AltCommandLine("-t")>]     Tag of tag: string
    | [<AltCommandLine("-s")>]     Source of sourceName: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Remote_Path _ -> "Remote path to pull (e.g. shared/templates/adr.md)."
            | Tag _         -> "Filter by tag; repeat for multiple tags (AND semantics)."
            | Source _      -> "Source name to pull from."

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

[<CliPrefix(CliPrefix.None)>]
type EruArgs =
    | [<SubCommand>] Init   of ParseResults<InitArgs>
    | [<SubCommand>] Add    of ParseResults<AddArgs>
    | [<SubCommand>] Search of ParseResults<SearchArgs>
    | [<SubCommand>] Sync   of ParseResults<SyncArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Init _   -> "Initialise a new eru configuration in the current repo."
            | Add _    -> "Pull a file from a knowledge source into this repo."
            | Search _ -> "Search across configured knowledge sources."
            | Sync _   -> "Synchronise local files with knowledge sources."
