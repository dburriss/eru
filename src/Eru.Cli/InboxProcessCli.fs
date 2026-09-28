module Eru.Cli.InboxProcessCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: InboxProcess.Options; Format: OutputFormat }

let (|InboxProcessCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Process processArgs ->
                    Some {
                        Command = {
                            InboxProcess.Options.InboxName = processArgs.TryGetResult InboxProcessArgs.Inbox
                            InboxProcess.Options.Channel   = processArgs.TryGetResult InboxProcessArgs.Channel
                            InboxProcess.Options.ItemName  = processArgs.TryGetResult InboxProcessArgs.Item
                            InboxProcess.Options.All       = processArgs.Contains InboxProcessArgs.All
                            InboxProcess.Options.DryRun    = processArgs.Contains InboxProcessArgs.Dryrun
                        }
                        Format = parseFormat (processArgs.TryGetResult InboxProcessArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let private agentSummary (agent: AgentConfig) : string =
    let argsStr = agent.Args |> String.concat " "
    if argsStr = "" then $"{agent.Protocol}:{agent.Command}" else $"{agent.Protocol}:{agent.Command} {argsStr}"

// Only computed when `execute` came back empty — explains *why* (raw items sitting in a
// channel with no agent configured) instead of implying the inbox is genuinely empty.
let private pendingElsewhereNote (deps: Eru.Deps) (opts: InboxProcess.Options) : string option =
    match InboxProcess.pendingWithoutAgent deps opts with
    | Error _ | Ok [] -> None
    | Ok pairs ->
        let total = pairs |> List.sumBy snd
        let detail = pairs |> List.map (fun (name, n) -> $"{name} ({n})") |> String.concat ", "
        Some $"{total} item(s) pending in channel(s) with no agent configured: {detail}. Run 'eru inbox channel add <inbox> <channel> --agent-command <cmd>' to enable one."

let private renderText (isDryRun: bool) (note: string option) (items: InboxProcess.ProcessedItem list) =
    if items.IsEmpty then
        printfn "Nothing to process."
        note |> Option.iter (printfn "%s")
    else
        let verb = if isDryRun then "Would process" else "Processed"
        for item in items do
            printfn $"{verb} [{item.Channel}] {item.ItemPath} -> {item.ArchivePath} (agent: {agentSummary item.Agent})"

let private renderJson (items: InboxProcess.ProcessedItem list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(items, opts))

let private renderTable (isDryRun: bool) (note: string option) (items: InboxProcess.ProcessedItem list) =
    if items.IsEmpty then
        printfn "Nothing to process."
        note |> Option.iter (printfn "%s")
    else
        let t = makeTable ["Channel"; "Item"; "Archive Path"; "Agent"]
        for item in items do
            t.AddRow(item.Channel, item.ItemPath, (if isDryRun then "(would move here) " + item.ArchivePath else item.ArchivePath), agentSummary item.Agent) |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    let result =
        match cmd.Format with
        | (Table | Text) when not cmd.Command.DryRun ->
            let status = AnsiConsole.Status()
            status.Spinner <- Spinner.Known.Dots
            status.Start<Result<InboxProcess.ProcessedItem list, string>>("Processing inbox...", fun ctx ->
                let onItemStart idx total fileName = ctx.Status <- $"Processing {idx}/{total}: {fileName}"
                InboxProcess.executeWithProgress deps cmd.Command onItemStart)
        | _ -> InboxProcess.execute deps cmd.Command
    match result with
    | Error e -> renderError e; 1
    | Ok items ->
        let note = if items.IsEmpty then pendingElsewhereNote deps cmd.Command else None
        match cmd.Format with
        | Text  -> renderText cmd.Command.DryRun note items
        | Json  -> renderJson items
        | Table -> renderTable cmd.Command.DryRun note items
        0
