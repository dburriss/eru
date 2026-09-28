module Eru.Cli.InboxChannelListCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { InboxName: string; Format: OutputFormat }

let (|InboxChannelListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Channel channelArgs ->
                    channelArgs.TryGetSubCommand() |> Option.bind (function
                        | InboxChannelArgs.List listArgs ->
                            Some {
                                InboxName = listArgs.GetResult InboxChannelListArgs.Inbox
                                Format    = parseFormat (listArgs.TryGetResult InboxChannelListArgs.Output)
                            }
                        | _ -> None)
                | _ -> None)
        | _ -> None)

let private agentSummary (agent: AgentConfig) : string =
    let argsStr = agent.Args |> String.concat " "
    if argsStr = "" then $"{agent.Protocol}:{agent.Command}" else $"{agent.Protocol}:{agent.Command} {argsStr}"

let private renderText (rows: InboxChannelList.ChannelRow list) =
    if rows.IsEmpty then
        printfn "No channels registered."
    else
        for row in rows do
            let agent = row.Agent |> Option.map (fun a -> $" [agent: {agentSummary a}]") |> Option.defaultValue ""
            printfn $"  {row.Name}{agent}"

let private renderJson (rows: InboxChannelList.ChannelRow list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(rows, opts))

let private renderTable (rows: InboxChannelList.ChannelRow list) =
    if rows.IsEmpty then
        printfn "No channels registered."
    else
        let t = makeTable ["Name"; "Agent"; "Description"]
        for row in rows do
            t.AddRow(row.Name, row.Agent |> Option.map agentSummary |> Option.defaultValue "", row.Description |> Option.defaultValue "") |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxChannelList.execute deps cmd.InboxName with
    | Error e -> renderError e; 1
    | Ok rows ->
        match cmd.Format with
        | Text  -> renderText rows
        | Json  -> renderJson rows
        | Table -> renderTable rows
        0
