module Eru.Cli.ManifestRemoveCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: ManifestRemove.Command; Format: OutputFormat }

let (|ManifestRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Remove removeArgs ->
                    Some {
                        Command = {
                            ManifestRemove.Command.Path   = removeArgs.GetResult ManifestRemoveArgs.Path
                            ManifestRemove.Command.DryRun = removeArgs.Contains  ManifestRemoveArgs.Dryrun
                        }
                        Format = parseFormat (removeArgs.TryGetResult ManifestRemoveArgs.Output)
                    }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match ManifestRemove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
