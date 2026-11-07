module Eru.Cli.OkfInitCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Path: string; DryRun: bool; Update: bool; Force: bool; Format: OutputFormat }

let (|OkfInitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Okf args ->
            args.TryGetSubCommand() |> Option.bind (function
                | OkfArgs.Init initArgs ->
                    Some {
                        Path   = initArgs.GetResult OkfInitArgs.Path
                        DryRun = initArgs.Contains OkfInitArgs.Dry_Run
                        Update = initArgs.Contains OkfInitArgs.Update
                        Force  = initArgs.Contains OkfInitArgs.Force
                        Format = parseFormat (initArgs.TryGetResult OkfInitArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    if cmd.Force && not cmd.Update then
        renderError "--force requires --update"
        1
    else
        OkfFixCli.runMode deps OkfFix.CreateOnly cmd.Path cmd.DryRun cmd.Update cmd.Force None cmd.Format
