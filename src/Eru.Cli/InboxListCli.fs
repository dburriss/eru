module Eru.Cli.InboxListCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Format: OutputFormat }

let (|InboxListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.List listArgs ->
                    Some { Format = parseFormat (listArgs.TryGetResult InboxListArgs.Output) }
                | _ -> None)
        | _ -> None)

let private renderText (rows: InboxList.InboxRow list) =
    if rows.IsEmpty then
        printfn "No inboxes configured."
    else
        for row in rows do
            let channelList = row.Channels |> String.concat ", "
            let channels = if row.Channels.IsEmpty then "" else $" [channels: {channelList}]"
            printfn $"  {row.Name}  {row.Path} ({row.RawPath}/{row.DefaultChannel})  [{row.Scope}]{channels}"

let private renderJson (rows: InboxList.InboxRow list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(rows, opts))

let private renderTable (rows: InboxList.InboxRow list) =
    if rows.IsEmpty then
        printfn "No inboxes configured."
    else
        let t = makeTable ["Name"; "Path"; "Raw Path"; "Default Channel"; "Channels"; "Scope"]
        for row in rows do
            t.AddRow(row.Name, row.Path, row.RawPath, row.DefaultChannel, row.Channels |> String.concat ", ", row.Scope) |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match InboxList.execute deps with
    | Error e -> renderError e; 1
    | Ok rows ->
        match cmd.Format with
        | Text  -> renderText rows
        | Json  -> renderJson rows
        | Table -> renderTable rows
        0
