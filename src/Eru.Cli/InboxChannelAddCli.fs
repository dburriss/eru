module Eru.Cli.InboxChannelAddCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxChannelAdd.Command; Format: OutputFormat }

let (|InboxChannelAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Channel channelArgs ->
                    channelArgs.TryGetSubCommand() |> Option.bind (function
                        | InboxChannelArgs.Add addArgs ->
                            let inboxName, channelName = addArgs.GetResult InboxChannelAddArgs.Inbox_And_Channel
                            Some {
                                Command = {
                                    InboxChannelAdd.Command.InboxName         = inboxName
                                    InboxChannelAdd.Command.ChannelName       = channelName
                                    InboxChannelAdd.Command.AgentProtocol     = addArgs.TryGetResult InboxChannelAddArgs.Agent_Protocol
                                    InboxChannelAdd.Command.AgentCommand      = addArgs.TryGetResult InboxChannelAddArgs.Agent_Command
                                    InboxChannelAdd.Command.AgentArgs         = addArgs.GetResults InboxChannelAddArgs.Agent_Args
                                    InboxChannelAdd.Command.AgentInstructions = addArgs.TryGetResult InboxChannelAddArgs.Agent_Instructions
                                    InboxChannelAdd.Command.AgentTimeout      = addArgs.TryGetResult InboxChannelAddArgs.Agent_Timeout
                                    InboxChannelAdd.Command.Description       = addArgs.TryGetResult InboxChannelAddArgs.Description
                                    InboxChannelAdd.Command.DryRun            = addArgs.Contains InboxChannelAddArgs.Dryrun
                                }
                                Format = parseFormat (addArgs.TryGetResult InboxChannelAddArgs.Output)
                            }
                        | _ -> None)
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxChannelAdd.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
