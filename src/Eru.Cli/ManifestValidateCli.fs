module Eru.Cli.ManifestValidateCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Format: OutputFormat }

let (|ManifestValidateCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Validate validateArgs ->
                    Some { Format = parseFormat (validateArgs.TryGetResult ManifestValidateArgs.Output) }
                | _ -> None)
        | _ -> None)

let private renderText (result: ManifestValidate.ValidateResult) =
    if result.Total = 0 then
        printfn "Manifest is empty — nothing to validate."
    elif result.Missing.IsEmpty then
        printfn $"All {result.Total} manifest reference(s) verified."
    else
        for path in result.Missing do
            eprintfn $"  missing: {path}"
        eprintfn $"{result.Missing.Length} reference(s) resolved to no local files."

let private renderJson (result: ManifestValidate.ValidateResult) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(result, opts))

let private renderTable (result: ManifestValidate.ValidateResult) =
    if result.Total = 0 then
        printfn "Manifest is empty — nothing to validate."
    elif result.Missing.IsEmpty then
        printfn $"All {result.Total} manifest reference(s) verified."
    else
        let t = makeTable ["Status"; "Path"]
        for path in result.Missing do
            t.AddRow("missing", path) |> ignore
        AnsiConsole.Write(t)
        eprintfn $"{result.Missing.Length} reference(s) resolved to no local files."

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match ManifestValidate.execute deps with
    | Error e -> renderError e; 1
    | Ok result ->
        match cmd.Format with
        | Text  -> renderText result
        | Json  -> renderJson result
        | Table -> renderTable result
        if result.Missing.IsEmpty then 0 else 1
