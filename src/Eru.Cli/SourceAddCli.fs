module Eru.Cli.SourceAddCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: SourceAdd.Command; Format: OutputFormat }

let (|SourceAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Add addArgs ->
                    Some {
                        Command = {
                            SourceAdd.Command.Url      = addArgs.GetResult    SourceAddArgs.Url
                            SourceAdd.Command.Name     = addArgs.TryGetResult SourceAddArgs.Name
                            SourceAdd.Command.Branch   = addArgs.TryGetResult SourceAddArgs.Branch
                            SourceAdd.Command.BasePath = addArgs.TryGetResult SourceAddArgs.Basepath
                            SourceAdd.Command.IsGlobal = addArgs.Contains     SourceAddArgs.Global
                            SourceAdd.Command.DryRun   = addArgs.Contains     SourceAddArgs.Dryrun
                        }
                        Format = parseFormat (addArgs.TryGetResult SourceAddArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceAdd.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
