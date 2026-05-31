module Eru.Cli.SourceViewCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Name: string; ShowFull: bool; Format: OutputFormat }

let (|SourceViewCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.View viewArgs ->
                    Some {
                        Name     = viewArgs.GetResult SourceViewArgs.Name
                        ShowFull = viewArgs.Contains SourceViewArgs.Full
                        Format   = parseFormat (viewArgs.TryGetResult SourceViewArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let private printKv label (value: string) = printfn "%-9s %s" (label + ":") value

let private renderText (detail: SourceView.SourceDetail) =
    printKv "Name"  detail.Name
    printKv "Scope" detail.Scope
    detail.Url      |> Option.iter (printKv "URL")
    detail.Branch   |> Option.iter (printKv "Branch")
    detail.BasePath |> Option.iter (printKv "BasePath")
    match detail.Manifest with
    | SourceView.NotCached   ->
        printfn "\nNo manifest cached. Run 'eru sync' to fetch source metadata."
    | SourceView.LoadError e ->
        eprintfn "Warning: could not read manifest: %s" e
    | SourceView.Files (entries, total, capped) ->
        let capNote = if capped then $", showing {entries.Length}" else ""
        printfn $"\nFiles ({total} total{capNote}):"
        for f in entries do
            let tags = if f.Tags.IsEmpty then "" else $"""  [{f.Tags |> String.concat ", "}]"""
            let desc = f.Description |> Option.map (fun d -> $"  — {d}") |> Option.defaultValue ""
            printfn $"  {f.Path}{tags}{desc}"
        if capped then
            printfn $"  ... and {total - entries.Length} more (pass --full to see all)"

let private renderJson (detail: SourceView.SourceDetail) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(detail, opts))

let private renderTable (detail: SourceView.SourceDetail) =
    let meta = makeTable ["Field"; "Value"]
    meta.AddRow("Name",  detail.Name)  |> ignore
    meta.AddRow("Scope", detail.Scope) |> ignore
    detail.Url      |> Option.iter (fun v -> meta.AddRow("URL",      v) |> ignore)
    detail.Branch   |> Option.iter (fun v -> meta.AddRow("Branch",   v) |> ignore)
    detail.BasePath |> Option.iter (fun v -> meta.AddRow("BasePath", v) |> ignore)
    AnsiConsole.Write(meta)
    match detail.Manifest with
    | SourceView.NotCached ->
        printfn "\nNo manifest cached. Run 'eru sync' to fetch source metadata."
    | SourceView.LoadError e ->
        eprintfn "\nWarning: could not read manifest: %s" e
    | SourceView.Files (entries, total, capped) ->
        let capNote = if capped then $", showing {entries.Length}" else ""
        printfn $"\nFiles ({total} total{capNote}):"
        let ft = makeTable ["Path"; "Tags"; "Description"]
        for f in entries do
            let tags = f.Tags |> String.concat ", "
            let desc = f.Description |> Option.defaultValue ""
            ft.AddRow(f.Path, tags, desc) |> ignore
        AnsiConsole.Write(ft)
        if capped then
            printfn $"  ... and {total - entries.Length} more (pass --full to see all)"

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceView.execute deps cmd.Name cmd.ShowFull with
    | Error e    -> renderError e; 1
    | Ok detail ->
        match cmd.Format with
        | Text  -> renderText detail
        | Json  -> renderJson detail
        | Table -> renderTable detail
        0
