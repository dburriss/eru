module Eru.Cli.SearchCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Query: LocalSearch.Query; Format: OutputFormat }

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args ->
            Some {
                Query = {
                    LocalSearch.Query.Terms = args.GetResult(SearchArgs.Terms, defaultValue = [])
                    LocalSearch.Query.Tags  = args.GetResults SearchArgs.Tag
                }
                Format = parseFormat (args.TryGetResult SearchArgs.Output)
            }
        | _ -> None)

let private renderText (results: LocalSearch.SearchResult list) =
    if results.IsEmpty then
        printfn "No results found."
    else
        for r in results do
            let hash      = Patterns.pathShortHash r.RemotePath
            let localPart = r.LocalPath |> Option.map (fun lp -> $"  [local: {lp}]") |> Option.defaultValue ""
            let tagPart =
                if r.Tags.IsEmpty then ""
                else
                    let tags = r.Tags |> String.concat ", "
                    $"  [tags: {tags}]"
            printfn "%s:%s  [hash: %s]%s%s" r.SourceName r.RemotePath hash tagPart localPart
            r.Description |> Option.iter (fun d -> printfn "  %s" d)

let private renderJson (results: LocalSearch.SearchResult list) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(results, opts))

let private renderTable (results: LocalSearch.SearchResult list) =
    if results.IsEmpty then
        printfn "No results found."
    else
        let t = makeTable ["Hash"; "Source"; "Path"; "Tags"; "Local Path"; "Description"]
        for r in results do
            let hash  = Patterns.pathShortHash r.RemotePath
            let tags  = r.Tags |> String.concat ", "
            let local = r.LocalPath   |> Option.defaultValue ""
            let desc  = r.Description |> Option.defaultValue ""
            t.AddRow(hash, r.SourceName, r.RemotePath, tags, local, desc) |> ignore
        AnsiConsole.Write(t)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match LocalSearch.execute deps cmd.Query with
    | Error e -> renderError e; 1
    | Ok results ->
        match cmd.Format with
        | Text  -> renderText results
        | Json  -> renderJson results
        | Table -> renderTable results
        0
