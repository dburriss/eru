module Eru.Cli.Program

open Argu
open Eru
open Eru.Adapters
open Eru.Cli.CommandMapper

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<EruArgs>(programName = "eru")
    try
        let parsed  = parser.ParseCommandLine argv
        let isDebug = parsed.Contains EruArgs.Debug
        let deps    = AdapterDeps.create isDebug

        match parsed with
        | McpCmd           -> Eru.Mcp.Server.run deps |> Async.AwaitTask |> Async.RunSynchronously; 0
        | InitCmd cmd      -> Init.run   deps cmd
        | AddCmd cmd       -> Add.run    deps cmd
        | SearchCmd query  -> Search.run deps query
        | SyncCmd opts     -> Sync.run   deps opts
        | SourceListCmd              -> Source.list deps
        | SourceViewCmd (name, full) -> Source.view deps name full
        | SourceAddCmd cmd           -> Source.add  deps cmd
        | CollectionCreateCmd cmd    -> Collection.create  deps cmd
        | CollectionAddFileCmd cmd   -> Collection.addFile deps cmd
        | ManifestInitCmd cmd        -> Manifest.init       deps cmd
        | ManifestAddCmd cmd         -> Manifest.addFile    deps cmd
        | ManifestRemoveCmd cmd      -> Manifest.removeFile deps cmd
        | ManifestVerifyCmd ()       -> Manifest.verify     deps
        | _ ->
            printfn "%s" (parser.PrintUsage())
            0

    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
