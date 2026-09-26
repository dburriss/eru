module Eru.Cli.SourceBundleRemoveCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: SourceBundleRemove.Command; Format: OutputFormat }

let (|SourceBundleRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Bundle bundleArgs ->
                    bundleArgs.TryGetSubCommand() |> Option.bind (function
                        | SourceBundleArgs.Remove removeArgs ->
                            let sourceName, path = removeArgs.GetResult SourceBundleRemoveArgs.Source_And_Path
                            Some {
                                Command = {
                                    SourceBundleRemove.Command.SourceName = sourceName
                                    SourceBundleRemove.Command.Path       = path
                                    SourceBundleRemove.Command.DryRun     = removeArgs.Contains SourceBundleRemoveArgs.Dryrun
                                }
                                Format = parseFormat (removeArgs.TryGetResult SourceBundleRemoveArgs.Output)
                            }
                        | _ -> None)
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceBundleRemove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
