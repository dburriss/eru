module Eru.Cli.InboxRemoveCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxRemove.Command; Format: OutputFormat }

let (|InboxRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Remove removeArgs ->
                    Some {
                        Command = {
                            InboxRemove.Command.Name     = removeArgs.GetResult InboxRemoveArgs.Name
                            InboxRemove.Command.IsGlobal = removeArgs.Contains  InboxRemoveArgs.Global
                            InboxRemove.Command.DryRun   = removeArgs.Contains  InboxRemoveArgs.Dryrun
                        }
                        Format = parseFormat (removeArgs.TryGetResult InboxRemoveArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxRemove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
