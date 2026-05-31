module Eru.Cli.SourceRemoveCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: SourceRemove.Command; Format: OutputFormat }

let (|SourceRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Remove removeArgs ->
                    Some {
                        Command = {
                            SourceRemove.Command.Name     = removeArgs.GetResult SourceRemoveArgs.Name
                            SourceRemove.Command.IsGlobal = removeArgs.Contains  SourceRemoveArgs.Global
                            SourceRemove.Command.DryRun   = removeArgs.Contains  SourceRemoveArgs.Dryrun
                        }
                        Format = parseFormat (removeArgs.TryGetResult SourceRemoveArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceRemove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
