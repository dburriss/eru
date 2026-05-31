module Eru.Cli.SyncCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Options: Sync.Options; Format: OutputFormat }

let (|SyncCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Sync args ->
            Some {
                Options = { Sync.Options.DryRun = args.Contains SyncArgs.Dryrun }
                Format  = parseFormat (args.TryGetResult SyncArgs.Output)
            }
        | _ -> None)

let private statusLabel (isDryRun: bool) (s: Sync.SyncStatus) =
    match s with
    | Sync.Current       -> "current"
    | Sync.Drifted       -> if isDryRun then "drifted" else "updated"
    | Sync.Missing       -> "missing"
    | Sync.Skipped _     -> "skipped"
    | Sync.Blocked       -> "blocked"

let private reason (s: Sync.SyncStatus) =
    match s with
    | Sync.Skipped r -> r
    | _              -> ""

let private counts (entries: Sync.SyncEntry list) =
    let n s = entries |> List.sumBy (fun e -> if e.Status = s then 1 else 0)
    let nSkipped = entries |> List.sumBy (function { Status = Sync.Skipped _ } -> 1 | _ -> 0)
    n Sync.Current, n Sync.Drifted, n Sync.Missing, nSkipped, n Sync.Blocked

let private renderText (result: Sync.SyncResult) =
    for e in result.Entries do
        let label = statusLabel result.DryRun e.Status
        match e.Status with
        | Sync.Skipped r -> printfn "[%s]  %s  (%s)" label e.LocalPath r
        | _              -> printfn "[%s]  %s" label e.LocalPath
    let nCurrent, nDrifted, nMissing, nSkipped, nBlocked = counts result.Entries
    if result.DryRun then
        printfn "Sync dry-run: %d drifted, %d current, %d missing, %d skipped, %d blocked."
            nDrifted nCurrent nMissing nSkipped nBlocked
    else
        printfn "Sync complete: %d updated, %d current, %d missing, %d skipped, %d blocked."
            nDrifted nCurrent nMissing nSkipped nBlocked

let private renderJson (result: Sync.SyncResult) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(result, opts))

let private renderTable (result: Sync.SyncResult) =
    let t = makeTable ["Status"; "Path"; "Reason"]
    for e in result.Entries do
        t.AddRow(statusLabel result.DryRun e.Status, e.LocalPath, reason e.Status) |> ignore
    AnsiConsole.Write(t)
    let nCurrent, nDrifted, nMissing, nSkipped, nBlocked = counts result.Entries
    if result.DryRun then
        printfn "\nSync dry-run: %d drifted, %d current, %d missing, %d skipped, %d blocked."
            nDrifted nCurrent nMissing nSkipped nBlocked
    else
        printfn "\nSync complete: %d updated, %d current, %d missing, %d skipped, %d blocked."
            nDrifted nCurrent nMissing nSkipped nBlocked

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Sync.execute deps cmd.Options with
    | Error e -> renderError e; 1
    | Ok result ->
        match cmd.Format with
        | Text  -> renderText result
        | Json  -> renderJson result
        | Table -> renderTable result
        0
