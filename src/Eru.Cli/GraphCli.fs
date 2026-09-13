module Eru.Cli.GraphCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.LinkGraph
open Eru.Cli.OutputFormat

type Cmd = { SourceFilter: string option; Format: OutputFormat; Dot: bool }

let (|GraphCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Graph args ->
            Some {
                SourceFilter = args.TryGetResult GraphArgs.Source
                Format       = parseFormat (args.TryGetResult GraphArgs.Output)
                Dot          = args.Contains GraphArgs.Dot
            }
        | _ -> None)

let private renderText (result: BuildResult) =
    for e in result.Edges do
        printfn "%s -> %s" (nodeKey e.From) (nodeKey e.To)

let private renderJson (result: BuildResult) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let payload =
        {| nodes = result.Nodes |> List.map nodeKey
           edges = result.Edges |> List.map (fun e -> {| from = nodeKey e.From; ``to`` = nodeKey e.To |}) |}
    printfn "%s" (JsonSerializer.Serialize(payload, opts))

let private renderTable (result: BuildResult) =
    if result.Edges.IsEmpty then
        printfn "No edges found."
    else
        let t = makeTable ["From"; "To"]
        for e in result.Edges do
            t.AddRow(nodeKey e.From, nodeKey e.To) |> ignore
        AnsiConsole.Write(t)

let private quoteDot (s: string) = "\"" + s.Replace("\"", "\\\"") + "\""

let private renderDot (result: BuildResult) =
    printfn "digraph {"
    for n in result.Nodes do
        match n with
        | ExternalNode _ -> printfn "  %s [shape=box];" (quoteDot (nodeKey n))
        | InternalNode _ -> printfn "  %s;" (quoteDot (nodeKey n))
    for e in result.Edges do
        printfn "  %s -> %s;" (quoteDot (nodeKey e.From)) (quoteDot (nodeKey e.To))
    printfn "}"

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match LinkGraph.execute deps { SourceFilter = cmd.SourceFilter } with
    | Error e -> renderError e; 1
    | Ok result ->
        if cmd.Dot then
            renderDot result
        else
            match cmd.Format with
            | Text  -> renderText result
            | Json  -> renderJson result
            | Table -> renderTable result
        0
