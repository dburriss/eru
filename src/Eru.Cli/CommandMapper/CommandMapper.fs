module Eru.Cli.CommandMapper

open Argu
open Eru

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
