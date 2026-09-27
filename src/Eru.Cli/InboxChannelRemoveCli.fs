module Eru.Cli.InboxChannelRemoveCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxChannelRemove.Command; Format: OutputFormat }

let (|InboxChannelRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Channel channelArgs ->
                    channelArgs.TryGetSubCommand() |> Option.bind (function
                        | InboxChannelArgs.Remove removeArgs ->
                            let inboxName, channelName = removeArgs.GetResult InboxChannelRemoveArgs.Inbox_And_Channel
                            Some {
                                Command = {
                                    InboxChannelRemove.Command.InboxName   = inboxName
                                    InboxChannelRemove.Command.ChannelName = channelName
                                    InboxChannelRemove.Command.DryRun      = removeArgs.Contains InboxChannelRemoveArgs.Dryrun
                                }
                                Format = parseFormat (removeArgs.TryGetResult InboxChannelRemoveArgs.Output)
                            }
                        | _ -> None)
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxChannelRemove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
