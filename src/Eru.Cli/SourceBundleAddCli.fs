module Eru.Cli.SourceBundleAddCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: SourceBundleAdd.Command; Format: OutputFormat }

let (|SourceBundleAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Bundle bundleArgs ->
                    bundleArgs.TryGetSubCommand() |> Option.bind (function
                        | SourceBundleArgs.Add addArgs ->
                            let sourceName, path = addArgs.GetResult SourceBundleAddArgs.Source_And_Path
                            Some {
                                Command = {
                                    SourceBundleAdd.Command.SourceName = sourceName
                                    SourceBundleAdd.Command.Path       = path
                                    SourceBundleAdd.Command.Kind       = addArgs.TryGetResult SourceBundleAddArgs.Kind
                                    SourceBundleAdd.Command.DryRun     = addArgs.Contains SourceBundleAddArgs.Dryrun
                                }
                                Format = parseFormat (addArgs.TryGetResult SourceBundleAddArgs.Output)
                            }
                        | _ -> None)
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match SourceBundleAdd.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
