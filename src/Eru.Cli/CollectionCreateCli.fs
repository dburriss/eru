module Eru.Cli.CollectionCreateCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: CollectionCreate.Command; Format: OutputFormat }

let (|CollectionCreateCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Collection args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CollectionArgs.Create createArgs ->
                    Some {
                        Command = {
                            CollectionCreate.Command.Name        = createArgs.GetResult    CollectionCreateArgs.Name
                            CollectionCreate.Command.Tags        = createArgs.GetResults   CollectionCreateArgs.Tag
                            CollectionCreate.Command.Description = createArgs.TryGetResult CollectionCreateArgs.Description
                            CollectionCreate.Command.IsGlobal    = createArgs.Contains     CollectionCreateArgs.Global
                            CollectionCreate.Command.DryRun      = createArgs.Contains     CollectionCreateArgs.Dryrun
                        }
                        Format = parseFormat (createArgs.TryGetResult CollectionCreateArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match CollectionCreate.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
