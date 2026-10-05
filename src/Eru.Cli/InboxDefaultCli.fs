module Eru.Cli.InboxDefaultCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxDefault.Command; Format: OutputFormat }

let (|InboxDefaultCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Default defaultArgs ->
                    Some {
                        Command = {
                            InboxDefault.Command.Name     = defaultArgs.GetResult InboxDefaultArgs.Name
                            InboxDefault.Command.IsGlobal = defaultArgs.Contains  InboxDefaultArgs.Global
                            InboxDefault.Command.DryRun   = defaultArgs.Contains  InboxDefaultArgs.Dryrun
                        }
                        Format = parseFormat (defaultArgs.TryGetResult InboxDefaultArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxDefault.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
