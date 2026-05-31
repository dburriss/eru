module Eru.Cli.DisconnectCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: Disconnect.Command; Format: OutputFormat }

let (|DisconnectCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Disconnect args ->
            Some {
                Command = {
                    Disconnect.Command.Target = args.GetResult DisconnectArgs.Target
                    Disconnect.Command.DryRun = args.Contains  DisconnectArgs.Dryrun
                }
                Format = parseFormat (args.TryGetResult DisconnectArgs.Output)
            }
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Disconnect.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
