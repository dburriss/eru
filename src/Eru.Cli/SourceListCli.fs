module Eru.Cli.SourceListCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Format: OutputFormat }

let (|SourceListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.List listArgs ->
                    Some { Format = parseFormat (listArgs.TryGetResult SourceListArgs.Output) }
                | _ -> None)
        | _ -> None)

let private renderText (rows: SourceList.SourceRow list) =
    if rows.IsEmpty then
        printfn "No sources configured."
    else
        for row in rows do
            let url      = row.Url      |> Option.defaultValue "(inherits from global)"
            let branch   = row.Branch   |> Option.map (fun b -> $" [branch: {b}]")   |> Option.defaultValue ""
            let basePath = row.BasePath |> Option.map (fun p -> $" [basepath: {p}]") |> Option.defaultValue ""
            let tags     =
                if row.Tags.IsEmpty then ""
                else
                    let t = row.Tags |> String.concat ", "
                    $" [tags: {t}]"
            printfn $"  {row.Name}  {url}{branch}{basePath}  [{row.Scope}]{tags}"

let private renderJson (rows: SourceList.SourceRow list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(rows, opts))

let private renderTable (rows: SourceList.SourceRow list) =
    if rows.IsEmpty then
        printfn "No sources configured."
    else
        let t = makeTable ["Name"; "URL"; "Branch"; "BasePath"; "Scope"; "Tags"]
        for row in rows do
            let url      = row.Url      |> Option.defaultValue ""
            let branch   = row.Branch   |> Option.defaultValue ""
            let basePath = row.BasePath |> Option.defaultValue ""
            let tags     = row.Tags |> String.concat ", "
            t.AddRow(row.Name, url, branch, basePath, row.Scope, tags) |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceList.execute deps with
    | Error e -> renderError e; 1
    | Ok rows ->
        match cmd.Format with
        | Text  -> renderText rows
        | Json  -> renderJson rows
        | Table -> renderTable rows
        0
