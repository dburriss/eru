module Eru.Cli.CollectionRemoveFileCli

open Argu
open Eru
open Eru.Cli.OutputFormat

let private parseSourcePath (raw: string) : Result<string * string, string> =
    let idx = raw.IndexOf(':')
    if idx <= 0 then Error $"Invalid source:path format '{raw}' — expected <source>:<remotePath>"
    else Ok (raw.[..idx-1], raw.[idx+1..])

type Cmd = { Command: CollectionRemoveFile.Command; Format: OutputFormat }

let (|CollectionRemoveFileCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Collection args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CollectionArgs.Remove removeArgs ->
                    let raw = removeArgs.GetResult CollectionRemoveFileArgs.File
                    match parseSourcePath raw with
                    | Error e -> eprintfn $"Error: {e}"; None
                    | Ok (source, remotePath) ->
                        Some {
                            Command = {
                                CollectionRemoveFile.Command.CollectionName = removeArgs.GetResult CollectionRemoveFileArgs.Collection
                                CollectionRemoveFile.Command.Source         = source
                                CollectionRemoveFile.Command.RemotePath     = remotePath
                                CollectionRemoveFile.Command.IsGlobal       = removeArgs.Contains  CollectionRemoveFileArgs.Global
                                CollectionRemoveFile.Command.DryRun         = removeArgs.Contains  CollectionRemoveFileArgs.Dryrun
                            }
                            Format = parseFormat (removeArgs.TryGetResult CollectionRemoveFileArgs.Output)
                        }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match CollectionRemoveFile.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
