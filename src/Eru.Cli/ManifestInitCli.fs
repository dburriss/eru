module Eru.Cli.ManifestInitCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: ManifestInit.Command; Format: OutputFormat }

let (|ManifestInitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Init initArgs ->
                    Some {
                        Command = { ManifestInit.Command.Force = initArgs.Contains ManifestInitArgs.Force }
                        Format  = parseFormat (initArgs.TryGetResult ManifestInitArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match ManifestInit.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
