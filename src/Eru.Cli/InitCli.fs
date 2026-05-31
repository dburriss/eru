module Eru.Cli.InitCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: Init.Command; Format: OutputFormat }

let (|InitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Init args ->
            Some {
                Command = {
                    Init.Command.Force    = args.Contains InitArgs.Force
                    Init.Command.IsGlobal = args.Contains InitArgs.Global
                    Init.Command.Path     = args.TryGetResult InitArgs.Path
                }
                Format = parseFormat (args.TryGetResult InitArgs.Output)
            }
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Init.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
