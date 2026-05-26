module Eru.Cli.Program

open Argu
open Eru
open Eru.Adapters

let (|InitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Init args -> Some { Init.Command.Force = args.Contains InitArgs.Force }
        | _                 -> None)

let (|AddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Add args ->
            let cmd : Add.Command = {
                RemotePath     = args.TryGetResult AddArgs.Remote_Path
                Tags           = args.GetResults  AddArgs.Tag
                SourceName     = args.TryGetResult AddArgs.Source
                CollectionName = args.TryGetResult AddArgs.Collection
                Target         = args.TryGetResult AddArgs.Target
            }
            Some cmd
        | _ -> None)

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args ->
            let query : Search.Query = {
                Terms = args.GetResult(SearchArgs.Terms, defaultValue = [])
                Tags  = args.GetResults SearchArgs.Tag
            }
            Some query
        | _ -> None)

let (|SyncCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Sync args -> Some { Sync.Options.DryRun = args.Contains SyncArgs.Dry_Run }
        | _                 -> None)

let (|SourceAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.map (fun (SourceArgs.Add addArgs) ->
                {
                    Url      = addArgs.GetResult SourceAddArgs.Url
                    Name     = addArgs.TryGetResult SourceAddArgs.Name
                    Branch   = addArgs.TryGetResult SourceAddArgs.Branch
                    BasePath = addArgs.TryGetResult SourceAddArgs.Basepath
                    IsGlobal = addArgs.Contains SourceAddArgs.Global
                } : Source.AddCommand)
        | _ -> None)

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
