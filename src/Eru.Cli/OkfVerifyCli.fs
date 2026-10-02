module Eru.Cli.OkfVerifyCli

open Argu
open System
open System.Diagnostics
open System.Text.Json
open Eru
open Eru.Cli.OutputFormat

type Cmd = { File: string; By: string option; At: string option; DryRun: bool; Format: OutputFormat }

let (|OkfVerifyCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Okf args ->
            args.TryGetSubCommand() |> Option.bind (function
                | OkfArgs.Verify v ->
                    Some {
                        File   = v.GetResult OkfVerifyArgs.File
                        By     = v.TryGetResult OkfVerifyArgs.By
                        At     = v.TryGetResult OkfVerifyArgs.At
                        DryRun = v.Contains OkfVerifyArgs.Dry_Run
                        Format = parseFormat (v.TryGetResult OkfVerifyArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

/// `git config user.email`, or None when git is unavailable or no email is set.
let private gitEmail () : string option =
    try
        let psi = ProcessStartInfo("git", "config user.email", RedirectStandardOutput = true, RedirectStandardError = true)
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd().Trim()
        p.WaitForExit()
        if p.ExitCode = 0 && out <> "" then Some out else None
    with _ -> None

let private resolveBy (by: string option) : Result<string, string> =
    match by with
    | Some b -> Ok b
    | None ->
        match gitEmail () with
        | Some email -> Ok $"human:{email}"
        | None -> Error "no --by given and `git config user.email` is not set"

let private resolveAt (deps: Eru.Deps) (at: string option) : Result<DateTimeOffset, string> =
    match at with
    | None -> Ok (deps.GetUtcNow ())
    | Some s ->
        match DateTimeOffset.TryParse s with
        | true, d when Frontmatter.hasUtcOffset s -> Ok d
        | _ -> Error $"--at \"{s}\" must be an ISO 8601 datetime with an explicit UTC offset, e.g. 2026-07-01T16:00:00Z"

let private message (r: OkfVerify.VerifyResult) =
    let verb = if r.DryRun then "would record" else "recorded"
    let note = if r.ReplacedInvalid then " (discarded a malformed existing `verified` value)" else ""
    $"✓ {verb} verification on {r.Path}: by {r.By} at {r.At}{note}"

let private render (format: OutputFormat) (r: OkfVerify.VerifyResult) =
    match format with
    | Json ->
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        printfn "%s" (JsonSerializer.Serialize(r, opts))
    | Text | Table -> printfn "%s" (message r)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    let cmdResult =
        resolveBy cmd.By
        |> Result.bind (fun by ->
            resolveAt deps cmd.At
            |> Result.map (fun at ->
                let c : OkfVerify.Command = { Path = cmd.File; By = by; At = at; DryRun = cmd.DryRun }
                c))
    match cmdResult |> Result.bind (OkfVerify.execute deps) with
    | Error e -> renderError e; 1
    | Ok r -> render cmd.Format r; 0
