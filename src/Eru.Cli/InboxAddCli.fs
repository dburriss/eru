module Eru.Cli.InboxAddCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxAdd.Command; Format: OutputFormat }

let (|InboxAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Add addArgs ->
                    let name, path = addArgs.GetResult InboxAddArgs.Name_And_Path
                    Some {
                        Command = {
                            InboxAdd.Command.Name           = name
                            InboxAdd.Command.Path           = path
                            InboxAdd.Command.RawPath        = addArgs.TryGetResult InboxAddArgs.Raw_Path
                            InboxAdd.Command.DefaultChannel = addArgs.TryGetResult InboxAddArgs.Default_Channel
                            InboxAdd.Command.IsGlobal       = addArgs.Contains     InboxAddArgs.Global
                            InboxAdd.Command.DryRun         = addArgs.Contains     InboxAddArgs.Dryrun
                        }
                        Format = parseFormat (addArgs.TryGetResult InboxAddArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxAdd.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
