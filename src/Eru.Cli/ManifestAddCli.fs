module Eru.Cli.ManifestAddCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: ManifestAdd.Command; Format: OutputFormat }

let (|ManifestAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Add addArgs ->
                    Some {
                        Command = {
                            ManifestAdd.Command.Path        = addArgs.GetResult    ManifestAddArgs.Path
                            ManifestAdd.Command.Tags        = addArgs.GetResults   ManifestAddArgs.Tag
                            ManifestAdd.Command.Description = addArgs.TryGetResult ManifestAddArgs.Description
                            ManifestAdd.Command.DryRun      = addArgs.Contains     ManifestAddArgs.Dryrun
                        }
                        Format = parseFormat (addArgs.TryGetResult ManifestAddArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match ManifestAdd.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
