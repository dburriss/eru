module Eru.Cli.OkfInitCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Path: string; DryRun: bool; Format: OutputFormat }

let (|OkfInitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Okf args ->
            args.TryGetSubCommand() |> Option.bind (function
                | OkfArgs.Init initArgs ->
                    Some {
                        Path   = initArgs.GetResult OkfInitArgs.Path
                        DryRun = initArgs.Contains OkfInitArgs.Dry_Run
                        Format = parseFormat (initArgs.TryGetResult OkfInitArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    OkfFixCli.runMode deps OkfFix.CreateOnly cmd.Path cmd.DryRun "reference" cmd.Format
