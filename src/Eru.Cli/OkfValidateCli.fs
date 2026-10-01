module Eru.Cli.OkfValidateCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Path: string; Format: OutputFormat }

let (|OkfValidateCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Okf args ->
            args.TryGetSubCommand() |> Option.bind (function
                | OkfArgs.Validate validateArgs ->
                    Some {
                        Path   = validateArgs.GetResult OkfValidateArgs.Path
                        Format = parseFormat (validateArgs.TryGetResult OkfValidateArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let private renderText (result: OkfValidate.ValidateResult) =
    for v in result.Violations do
        eprintfn $"✗ {v.Path} — {v.Message}"
    if result.Violations.IsEmpty then
        printfn $"✓ {result.TotalConcepts} concepts conformant"
    else
        eprintfn $"{result.Violations.Length} violation(s) found; {result.TotalConcepts} concept(s) conformant."

let private renderJson (result: OkfValidate.ValidateResult) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(result, opts))

let private renderTable (result: OkfValidate.ValidateResult) =
    if result.Violations.IsEmpty then
        printfn $"✓ {result.TotalConcepts} concepts conformant"
    else
        let t = makeTable ["Status"; "Path"; "Rule"; "Message"]
        for v in result.Violations do
            t.AddRow("✗", v.Path, v.Rule, v.Message) |> ignore
        AnsiConsole.Write(t)
        eprintfn $"{result.Violations.Length} violation(s) found; {result.TotalConcepts} concept(s) conformant."

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    let ignorePatterns =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Ok g, Ok l -> Config.resolveOkfIgnorePatterns g l
        | _          -> Config.defaultOkfIgnorePatterns
    match OkfValidate.execute deps { Path = cmd.Path; IgnorePatterns = ignorePatterns } with
    | Error e -> renderError e; 1
    | Ok result ->
        match cmd.Format with
        | Text  -> renderText result
        | Json  -> renderJson result
        | Table -> renderTable result
        if result.Violations.IsEmpty then 0 else 1
