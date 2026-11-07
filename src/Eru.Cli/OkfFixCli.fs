module Eru.Cli.OkfFixCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Path: string; DryRun: bool; DefaultType: string option; Format: OutputFormat }

let (|OkfFixCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Okf args ->
            args.TryGetSubCommand() |> Option.bind (function
                | OkfArgs.Fix fixArgs ->
                    Some {
                        Path        = fixArgs.GetResult OkfFixArgs.Path
                        DryRun      = fixArgs.Contains OkfFixArgs.Dry_Run
                        DefaultType = fixArgs.TryGetResult OkfFixArgs.Default_Type
                        Format      = parseFormat (fixArgs.TryGetResult OkfFixArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let private verb (r: OkfFix.FixResult) (c: OkfFix.Change) =
    match r.DryRun, c.Created with
    | true, true   -> "would create"
    | true, false  -> "would fix"
    | false, true  -> "created"
    | false, false -> "fixed"

let private summary (r: OkfFix.FixResult) =
    let created = r.Changes |> List.filter (fun c -> c.Created) |> List.length
    let modified = r.Changes.Length - created
    let tense = if r.DryRun then "would be " else ""
    let skipped = if r.Skipped.IsEmpty then "" else $", {r.Skipped.Length} skipped"
    $"{created} file(s) {tense}created, {modified} {tense}modified, {r.Manual.Length} need manual attention{skipped}."

let private renderText (r: OkfFix.FixResult) =
    for c in r.Changes do
        printfn $"✓ {verb r c} {c.Path} — {c.Description}"
    for (path, reason) in r.Skipped do
        printfn $"- skipped {path} — {reason}"
    for v in r.Manual do
        eprintfn $"✗ {v.Path} — {v.Message}"
    printfn "%s" (summary r)

let private renderJson (r: OkfFix.FixResult) =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    printfn "%s" (JsonSerializer.Serialize(r, opts))

let private renderTable (r: OkfFix.FixResult) =
    if not r.Changes.IsEmpty || not r.Manual.IsEmpty || not r.Skipped.IsEmpty then
        let t = makeTable ["Status"; "Path"; "Rule"; "Detail"]
        for c in r.Changes do
            t.AddRowEscaped("✓", c.Path, c.Rule, $"{verb r c}: {c.Description}") |> ignore
        for (path, reason) in r.Skipped do
            t.AddRowEscaped("-", path, "skipped", reason) |> ignore
        for v in r.Manual do
            t.AddRowEscaped("✗", v.Path, v.Rule, v.Message) |> ignore
        AnsiConsole.Write(t)
    printfn "%s" (summary r)

/// Shared by `eru okf init` and `eru okf fix`; exit code 1 if anything needs manual attention.
let runMode (deps: Eru.Deps) (mode: OkfFix.Mode) (path: string) (dryRun: bool) (update: bool) (force: bool) (typeOverride: string option) (format: OutputFormat) : int =
    let ignorePatterns, fixTypes, defaultType =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig (), deps.ReadEnvConfig () with
        | Ok g, Ok l, Ok e -> Config.resolveOkfIgnorePatternsWith e g l, Config.resolveOkfFixTypesWith e g l, Config.resolveOkfDefaultTypeWith e g l
        | _                -> Config.defaultOkfIgnorePatterns, [], Config.defaultOkfType
    let cmd : OkfFix.Command =
        { Path = path; IgnorePatterns = ignorePatterns; Mode = mode; DefaultType = defaultType
          TypeOverride = typeOverride; FixTypes = fixTypes; DryRun = dryRun; Update = update; Force = force }
    match OkfFix.execute deps cmd with
    | Error e -> renderError e; 1
    | Ok result ->
        match format with
        | Text  -> renderText result
        | Json  -> renderJson result
        | Table -> renderTable result
        if result.Manual.IsEmpty then 0 else 1

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    runMode deps OkfFix.Fix cmd.Path cmd.DryRun false false cmd.DefaultType cmd.Format
