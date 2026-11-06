module Eru.Cli.SourceToggleCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: SourceToggle.Command; Format: OutputFormat }

let (|SourceToggleCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Enable a ->
                    Some { Command = { SourceToggle.Command.Name = a.GetResult SourceToggleArgs.Name
                                       SourceToggle.Command.Enable = true
                                       SourceToggle.Command.IsGlobal = a.Contains SourceToggleArgs.Global
                                       SourceToggle.Command.DryRun = a.Contains SourceToggleArgs.Dryrun }
                           Format = parseFormat (a.TryGetResult SourceToggleArgs.Output) }
                | SourceArgs.Disable a ->
                    Some { Command = { SourceToggle.Command.Name = a.GetResult SourceToggleArgs.Name
                                       SourceToggle.Command.Enable = false
                                       SourceToggle.Command.IsGlobal = a.Contains SourceToggleArgs.Global
                                       SourceToggle.Command.DryRun = a.Contains SourceToggleArgs.Dryrun }
                           Format = parseFormat (a.TryGetResult SourceToggleArgs.Output) }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceToggle.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
