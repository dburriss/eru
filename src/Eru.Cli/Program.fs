module Eru.Cli.Program

open Argu
open Eru
open Eru.Adapters
open Eru.Cli.SearchCli
open Eru.Cli.SyncCli
open Eru.Cli.AddCli
open Eru.Cli.InitCli
open Eru.Cli.SourceListCli
open Eru.Cli.SourceViewCli
open Eru.Cli.SourceFilesCli
open Eru.Cli.SourceAddCli
open Eru.Cli.SourceRemoveCli
open Eru.Cli.CollectionCreateCli
open Eru.Cli.CollectionAddFileCli
open Eru.Cli.CollectionRemoveFileCli
open Eru.Cli.ManifestInitCli
open Eru.Cli.ManifestAddCli
open Eru.Cli.ManifestRemoveCli
open Eru.Cli.ManifestVerifyCli
open Eru.Cli.RemoveCli

let private (|McpCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Mcp _ -> Some ()
        | _             -> None)

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<EruArgs>(programName = "eru")
    try
        let parsed  = parser.ParseCommandLine argv
        let isDebug = parsed.Contains EruArgs.Debug
        let deps    = AdapterDeps.create isDebug

        match parsed with
        | McpCmd ()                   -> Eru.Mcp.Server.run deps |> Async.AwaitTask |> Async.RunSynchronously; 0
        | InitCmd cmd                 -> InitCli.run deps cmd
        | AddCmd cmd                  -> AddCli.run deps cmd
        | SearchCmd cmd               -> SearchCli.run deps cmd
        | SyncCmd cmd                 -> SyncCli.run deps cmd
        | SourceListCmd cmd           -> SourceListCli.run deps cmd
        | SourceViewCmd cmd           -> SourceViewCli.run deps cmd
        | SourceFilesCmd cmd          -> SourceFilesCli.run deps cmd
        | SourceAddCmd cmd            -> SourceAddCli.run deps cmd
        | SourceRemoveCmd cmd         -> SourceRemoveCli.run deps cmd
        | CollectionCreateCmd cmd     -> CollectionCreateCli.run deps cmd
        | CollectionAddFileCmd cmd    -> CollectionAddFileCli.run deps cmd
        | CollectionRemoveFileCmd cmd -> CollectionRemoveFileCli.run deps cmd
        | ManifestInitCmd cmd         -> ManifestInitCli.run deps cmd
        | ManifestAddCmd cmd          -> ManifestAddCli.run deps cmd
        | ManifestRemoveCmd cmd       -> ManifestRemoveCli.run deps cmd
        | ManifestVerifyCmd cmd       -> ManifestVerifyCli.run deps cmd
        | RemoveCmd cmd               -> RemoveCli.run deps cmd
        | _ ->
            printfn "%s" (parser.PrintUsage())
            0

    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
