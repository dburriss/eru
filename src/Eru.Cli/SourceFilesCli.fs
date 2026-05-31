module Eru.Cli.SourceFilesCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { SourceName: string option; Format: OutputFormat }

let (|SourceFilesCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Files filesArgs ->
                    Some {
                        SourceName = filesArgs.TryGetResult SourceFilesArgs.Name
                        Format     = parseFormat (filesArgs.TryGetResult SourceFilesArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let private renderSourceText (sourceName: string) (rows: SourceFiles.SourceFileRow list) =
    printfn $"Files for source: {sourceName}\n"
    if rows.IsEmpty then
        printfn "  (no files matched manifest patterns)"
    else
        for row in rows do
            let tagStr  = if row.Tags.IsEmpty then "" else $"""  [{row.Tags |> String.concat ", "}]"""
            let descStr = row.Description |> Option.map (fun d -> $"  — {d}") |> Option.defaultValue ""
            printfn $"  {row.Hash}  {row.Path}{tagStr}{descStr}"

let private renderText (results: (string * SourceFiles.SourceFileRow list) list) =
    results |> List.iteri (fun i (name, rows) ->
        if i > 0 then printfn ""
        renderSourceText name rows)

let private renderJson (results: (string * SourceFiles.SourceFileRow list) list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let payload = results |> List.map (fun (name, rows) -> {| source = name; files = rows |})
    printfn "%s" (JsonSerializer.Serialize(payload, opts))

let private renderTable (results: (string * SourceFiles.SourceFileRow list) list) =
    let t = makeTable ["Source"; "Hash"; "Path"; "Tags"; "Description"]
    for (sourceName, rows) in results do
        if rows.IsEmpty then
            t.AddRow(sourceName, "", "(no files matched manifest patterns)", "", "") |> ignore
        else
            for row in rows do
                let tags = row.Tags |> String.concat ", "
                let desc = row.Description |> Option.defaultValue ""
                t.AddRow(sourceName, row.Hash, row.Path, tags, desc) |> ignore
    AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceFiles.execute deps cmd.SourceName with
    | Error e    -> renderError e; 1
    | Ok results ->
        match cmd.Format with
        | Text  -> renderText results
        | Json  -> renderJson results
        | Table -> renderTable results
        0
