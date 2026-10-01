module Eru.Cli.InboxSendCli

open Argu
open System
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxSend.Command; Format: OutputFormat }

let (|InboxSendCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Send sendArgs ->
                    let content =
                        match sendArgs.TryGetResult InboxSendArgs.Content with
                        | Some c -> Some c
                        | None when Console.IsInputRedirected -> Some (Console.In.ReadToEnd().Trim())
                        | None -> None
                    Some {
                        Command = {
                            InboxSend.Command.Content   = content
                            InboxSend.Command.InboxName = sendArgs.TryGetResult InboxSendArgs.Inbox
                            InboxSend.Command.Channel   = sendArgs.TryGetResult InboxSendArgs.Channel
                            InboxSend.Command.Title     = sendArgs.TryGetResult InboxSendArgs.Title
                            InboxSend.Command.Note      = sendArgs.TryGetResult InboxSendArgs.Note
                            InboxSend.Command.As        = sendArgs.TryGetResult InboxSendArgs.As
                            InboxSend.Command.DryRun    = sendArgs.Contains InboxSendArgs.Dryrun
                        }
                        Format = parseFormat (sendArgs.TryGetResult InboxSendArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let private messageFor (isDryRun: bool) (result: InboxSend.SendResult) : string =
    let verb = if isDryRun then "Would send" else "Sent"
    let sidecar = result.SidecarPath |> Option.map (fun p -> $" (+ {p})") |> Option.defaultValue ""
    let remote = result.Remote |> Option.map (fun r -> $" [remote {r}]") |> Option.defaultValue ""
    $"{verb} {result.Kind} to inbox '{result.InboxName}' channel '{result.Channel}' -> {result.TargetPath}{sidecar}{remote}"

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxSend.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok result -> renderMessage (messageFor cmd.Command.DryRun result) cmd.Format; 0
