module Eru.Cli.SearchCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Query: Search.Query; Format: OutputFormat }

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args ->
            Some {
                Query = {
                    Search.Query.Terms = args.GetResult(SearchArgs.Terms, defaultValue = [])
                    Search.Query.Tags  = args.GetResults SearchArgs.Tag
                }
                Format = parseFormat (args.TryGetResult SearchArgs.Output)
            }
        | _ -> None)

let private renderText (results: Search.SearchResult list) =
    if results.IsEmpty then
        printfn "No results found."
    else
        for r in results do
            let localPart = r.LocalPath |> Option.map (fun lp -> $"  [local: {lp}]") |> Option.defaultValue ""
            let tagPart =
                if r.Tags.IsEmpty then ""
                else
                    let tags = r.Tags |> String.concat ", "
                    $"  [tags: {tags}]"
            printfn "%s:%s%s%s" r.SourceName r.RemotePath tagPart localPart
            r.Description |> Option.iter (fun d -> printfn "  %s" d)

let private renderJson (results: Search.SearchResult list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(results, opts))

let private renderTable (results: Search.SearchResult list) =
    if results.IsEmpty then
        printfn "No results found."
    else
        let t = makeTable ["Source"; "Path"; "Tags"; "Local Path"; "Description"]
        for r in results do
            let tags  = r.Tags |> String.concat ", "
            let local = r.LocalPath   |> Option.defaultValue ""
            let desc  = r.Description |> Option.defaultValue ""
            t.AddRow(r.SourceName, r.RemotePath, tags, local, desc) |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Search.execute deps cmd.Query with
    | Error e -> renderError e; 1
    | Ok results ->
        match cmd.Format with
        | Text  -> renderText results
        | Json  -> renderJson results
        | Table -> renderTable results
        0
