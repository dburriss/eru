module Eru.Cli.AddCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: Add.Command; Format: OutputFormat }

let (|AddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Add args ->
            Some {
                Command = {
                    Add.Command.RemotePath     = args.TryGetResult AddArgs.Remote_Path
                    Add.Command.Tags           = args.GetResults  AddArgs.Tag
                    Add.Command.SourceName     = args.TryGetResult AddArgs.Source
                    Add.Command.CollectionName = args.TryGetResult AddArgs.Collection
                    Add.Command.Target         = args.TryGetResult AddArgs.Target
                    Add.Command.DryRun         = args.Contains    AddArgs.Dryrun
                    Add.Command.IsGlobal       = args.Contains    AddArgs.Global
                }
                Format = parseFormat (args.TryGetResult AddArgs.Output)
            }
        | _ -> None)

let private renderText (entries: Add.PullEntry list) (isDryRun: bool) =
    let pulled  = entries |> List.choose (function Add.Pulled e -> Some e | Add.Blocked _ -> None)
    let blocked = entries |> List.choose (function Add.Blocked p -> Some p | Add.Pulled _ -> None)
    for path in blocked do
        printfn "[blocked]  %s" path
    if isDryRun then
        match pulled with
        | [e] -> printfn "Would pull %s → %s" e.RemotePath e.LocalPath
        | _   -> printfn "Would pull %d file(s)" pulled.Length
    else
        match pulled with
        | [e] -> printfn "Pulled %s → %s" e.RemotePath e.LocalPath
        | _   -> printfn "Pulled %d file(s)" pulled.Length

let private renderJson (entries: Add.PullEntry list) (isDryRun: bool) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let payload = {| dryRun = isDryRun; entries = entries |}
    printfn "%s" (JsonSerializer.Serialize(payload, opts))

let private renderTable (entries: Add.PullEntry list) (isDryRun: bool) =
    let t = makeTable ["Action"; "Remote Path"; "Local Path"]
    for e in entries do
        match e with
        | Add.Pulled lock ->
            let action = if isDryRun then "would pull" else "pulled"
            t.AddRow(action, lock.RemotePath, lock.LocalPath) |> ignore
        | Add.Blocked path ->
            t.AddRow("blocked", path, "") |> ignore
    AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Add.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok entries ->
        match cmd.Format with
        | Text  -> renderText entries cmd.Command.DryRun
        | Json  -> renderJson entries cmd.Command.DryRun
        | Table -> renderTable entries cmd.Command.DryRun
        0
