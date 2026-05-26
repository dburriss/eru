module Eru.Cli.Program

open Argu
open Eru
open Eru.Adapters
open Eru.Cli.CommandMapper

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<EruArgs>(programName = "eru")
    try
        let parsed = parser.ParseCommandLine argv
        let deps   = AdapterDeps.create ()

        match parsed with
        | InitCmd cmd      -> Init.run   deps cmd
        | AddCmd cmd       -> Add.run    deps cmd
        | SearchCmd query  -> Search.run deps query
        | SyncCmd opts     -> Sync.run   deps opts
        | SourceAddCmd cmd -> Source.add deps cmd
        | _ ->
            printfn "%s" (parser.PrintUsage())
            0

    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
