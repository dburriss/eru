module Eru.Cli.SourceBundleListCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { SourceName: string; Format: OutputFormat }

let (|SourceBundleListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Bundle bundleArgs ->
                    bundleArgs.TryGetSubCommand() |> Option.bind (function
                        | SourceBundleArgs.List listArgs ->
                            Some {
                                SourceName = listArgs.GetResult SourceBundleListArgs.Source
                                Format     = parseFormat (listArgs.TryGetResult SourceBundleListArgs.Output)
                            }
                        | _ -> None)
                | _ -> None)
        | _ -> None)

let private kindLabel = function Manifest -> "manifest" | Okf -> "okf"
let private pathLabel (p: string) = if p = "" then "(root)" else p

let private renderText (bundles: Bundle list) =
    if bundles.IsEmpty then
        printfn "No bundles registered."
    else
        for b in bundles do
            printfn $"  {pathLabel b.Path}  [{kindLabel b.Kind}]"

let private renderJson (bundles: Bundle list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(bundles, opts))

let private renderTable (bundles: Bundle list) =
    if bundles.IsEmpty then
        printfn "No bundles registered."
    else
        let t = makeTable ["Path"; "Kind"]
        for b in bundles do
            t.AddRow(pathLabel b.Path, kindLabel b.Kind) |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceBundleList.execute deps cmd.SourceName with
    | Error e -> renderError e; 1
    | Ok bundles ->
        match cmd.Format with
        | Text  -> renderText bundles
        | Json  -> renderJson bundles
        | Table -> renderTable bundles
        0
