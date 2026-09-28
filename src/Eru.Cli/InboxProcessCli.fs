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
                            // Reuses the top-level `eru --debug` flag rather than a second,
                            // confusingly-same-named subcommand flag — `--debug` already means
                            // "show more than usual"; here that includes each item's agent
                            // handshake timings.
                            InboxProcess.Options.Timing    = r.Contains EruArgs.Debug
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

let private timingsSummary (timings: AgentTimings) : string =
    $"initialize: %.0f{timings.InitializeMs}ms, session/new: %.0f{timings.SessionNewMs}ms, prompt: %.0f{timings.PromptMs}ms"

let private renderText (isDryRun: bool) (note: string option) (items: InboxProcess.ProcessedItem list) =
    if items.IsEmpty then
        printfn "Nothing to process."
        note |> Option.iter (printfn "%s")
    else
        let verb = if isDryRun then "Would process" else "Processed"
        for item in items do
            printfn $"{verb} [{item.Channel}] {item.ItemPath} -> {item.ArchivePath} (agent: {agentSummary item.Agent})"
            item.Timings |> Option.iter (fun t -> printfn $"  {timingsSummary t}")

let private renderJson (items: InboxProcess.ProcessedItem list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(items, opts))

let private renderTable (isDryRun: bool) (note: string option) (items: InboxProcess.ProcessedItem list) =
    if items.IsEmpty then
        printfn "Nothing to process."
        note |> Option.iter (printfn "%s")
    else
        let showTimings = items |> List.exists (fun i -> i.Timings.IsSome)
        let headers = if showTimings then ["Channel"; "Item"; "Archive Path"; "Agent"; "Timings"] else ["Channel"; "Item"; "Archive Path"; "Agent"]
        let t = makeTable headers
        for item in items do
            let cells = [
                item.Channel
                item.ItemPath
                (if isDryRun then "(would move here) " + item.ArchivePath else item.ArchivePath)
                agentSummary item.Agent
            ]
            let cells = if showTimings then cells @ [ item.Timings |> Option.map timingsSummary |> Option.defaultValue "" ] else cells
            t.AddRow(cells |> Array.ofList) |> ignore
        AnsiConsole.Write(t)

// How much of the agent's most-recently-streamed text to show next to the spinner —
// just enough to look alive, not a transcript.
let private snippetLength = 60

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    let result =
        match cmd.Format with
        | (Table | Text) when not cmd.Command.DryRun ->
            let status = AnsiConsole.Status()
            status.Spinner <- Spinner.Known.Dots
            status.Start<Result<InboxProcess.ProcessedItem list, string>>("Processing inbox...", fun ctx ->
                let mutable itemLabel = "Processing inbox..."
                // Only ever touched from one thread at a time: onItemStart runs on this
                // (the blocking) thread between items, onChunk on the ACP transport's
                // reader thread while an item's RunAgent call is in flight — and that call
                // has fully returned, with no further onChunk possible, before the next
                // onItemStart fires.
                let buffer = System.Text.StringBuilder()
                let render () =
                    let flat = buffer.ToString().Replace("\r", "").Replace("\n", " ").Trim()
                    let tail =
                        if flat.Length <= snippetLength then flat
                        else
                            // Cut to the tail, then drop any partial word at the front (up to
                            // the first space) so it doesn't look like text is missing —
                            // a leading "…" marks the cut instead.
                            let cut = flat.Substring(flat.Length - snippetLength)
                            let atWordStart = cut.IndexOf ' '
                            let wholeWords = if atWordStart >= 0 then cut.Substring(atWordStart + 1) else cut
                            "…" + wholeWords
                    ctx.Status <- if tail = "" then itemLabel else $"{itemLabel} — {Markup.Escape tail}"
                let onItemStart idx total fileName =
                    itemLabel <- $"Processing {idx}/{total}: {fileName}"
                    buffer.Clear() |> ignore
                    render ()
                let onChunk (text: string) =
                    buffer.Append(text) |> ignore
                    render ()
                InboxProcess.executeWithProgress deps cmd.Command onItemStart onChunk)
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
