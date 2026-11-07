module Eru.Cli.SourceAddCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: SourceAdd.Command; Format: OutputFormat }

// A local directory is stored as an absolute path (expanding `~`), so the source keeps working from any cwd.
// Anything else (a remote URL, an scp-style address, a missing path) is left exactly as typed.
let normalizeLocation (raw: string) : string =
    let expanded =
        if raw = "~" || raw.StartsWith "~/" || raw.StartsWith "~\\" then
            System.IO.Path.Combine(System.Environment.GetFolderPath System.Environment.SpecialFolder.UserProfile, raw.TrimStart('~').TrimStart('/', '\\'))
        else raw
    try
        if System.IO.Directory.Exists expanded then System.IO.Path.GetFullPath(expanded).TrimEnd('/', '\\') else raw
    with _ -> raw

let (|SourceAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Add addArgs ->
                    Some {
                        Command = {
                            SourceAdd.Command.Url      = normalizeLocation (addArgs.GetResult SourceAddArgs.Url)
                            SourceAdd.Command.Name     = addArgs.TryGetResult SourceAddArgs.Name
                            SourceAdd.Command.Branch   = addArgs.TryGetResult SourceAddArgs.Branch
                            SourceAdd.Command.BasePath = addArgs.TryGetResult SourceAddArgs.Basepath
                            SourceAdd.Command.Scan     = addArgs.Contains     SourceAddArgs.Scan
                            SourceAdd.Command.Nested   = addArgs.Contains     SourceAddArgs.Nested
                            SourceAdd.Command.Exclude  = addArgs.GetResults   SourceAddArgs.Exclude
                            SourceAdd.Command.IsGlobal = addArgs.Contains     SourceAddArgs.Global
                            SourceAdd.Command.DryRun   = addArgs.Contains     SourceAddArgs.Dryrun
                        }
                        Format = parseFormat (addArgs.TryGetResult SourceAddArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceAdd.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
