module Eru.Cli.Program

open Argu
open Eru
open Eru.Adapters

let (|InitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Init args -> Some args
        | _                 -> None)

let (|AddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Add args -> Some args
        | _                -> None)

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args -> Some args
        | _                   -> None)

let (|SyncCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Sync args -> Some args
        | _                 -> None)

let (|SourceCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args -> Some args
        | _                   -> None)

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<EruArgs>(programName = "eru")
    try
        let parsed = parser.ParseCommandLine argv
        let deps   = AdapterDeps.create ()

        match parsed with
        | InitCmd args ->
            let cmd : Init.Command = { Force = args.Contains InitArgs.Force }
            Init.run deps cmd

        | AddCmd args ->
            let cmd : Add.Command = {
                RemotePath = args.TryGetResult AddArgs.Remote_Path
                Tags       = args.GetResults AddArgs.Tag
                SourceName = args.TryGetResult AddArgs.Source
            }
            Add.run deps cmd

        | SearchCmd args ->
            let query : Search.Query = {
                Terms = args.GetResult(SearchArgs.Terms, defaultValue = [])
                Tags  = args.GetResults SearchArgs.Tag
            }
            Search.run deps query

        | SyncCmd args ->
            let opts : Sync.Options = { DryRun = args.Contains SyncArgs.Dry_Run }
            Sync.run deps opts

        | SourceCmd args ->
            match args.TryGetSubCommand() with
            | Some (SourceArgs.Add addArgs) ->
                let cmd : Source.AddCommand = {
                    Url      = addArgs.GetResult SourceAddArgs.Url
                    Name     = addArgs.TryGetResult SourceAddArgs.Name
                    Branch   = addArgs.TryGetResult SourceAddArgs.Branch
                    BasePath = addArgs.TryGetResult SourceAddArgs.Basepath
                    IsGlobal = addArgs.Contains SourceAddArgs.Global
                }
                Source.add deps cmd
            | _ ->
                printfn "%s" (parser.PrintUsage())
                0

        | _ ->
            printfn "%s" (parser.PrintUsage())
            0

    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
