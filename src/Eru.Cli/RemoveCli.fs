module Eru.Cli.RemoveCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: Remove.Command; Format: OutputFormat }

let (|RemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Remove args ->
            Some {
                Command = {
                    Remove.Command.Target  = args.GetResult RemoveArgs.Target
                    Remove.Command.DryRun  = args.Contains  RemoveArgs.Dryrun
                }
                Format = parseFormat (args.TryGetResult RemoveArgs.Output)
            }
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Remove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
