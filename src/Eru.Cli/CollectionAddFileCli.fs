module Eru.Cli.CollectionAddFileCli

open Argu
open Eru
open Eru.Cli.OutputFormat

let private parseSourcePath (raw: string) : Result<string * string, string> =
    let idx = raw.IndexOf(':')
    if idx <= 0 then Error $"Invalid source:path format '{raw}' — expected <source>:<remotePath>"
    else Ok (raw.[..idx-1], raw.[idx+1..])

type Cmd = { Command: CollectionAddFile.Command; Format: OutputFormat }

let (|CollectionAddFileCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Collection args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CollectionArgs.Add addArgs ->
                    let raw = addArgs.GetResult CollectionAddArgs.File
                    match parseSourcePath raw with
                    | Error e -> eprintfn $"Error: {e}"; None
                    | Ok (source, remotePath) ->
                        Some {
                            Command = {
                                CollectionAddFile.Command.CollectionName = addArgs.GetResult    CollectionAddArgs.Collection
                                CollectionAddFile.Command.Source         = source
                                CollectionAddFile.Command.RemotePath     = remotePath
                                CollectionAddFile.Command.Tags           = addArgs.GetResults   CollectionAddArgs.Tag
                                CollectionAddFile.Command.Description    = addArgs.TryGetResult CollectionAddArgs.Description
                                CollectionAddFile.Command.IsGlobal       = addArgs.Contains     CollectionAddArgs.Global
                                CollectionAddFile.Command.DryRun         = addArgs.Contains     CollectionAddArgs.Dryrun
                            }
                            Format = parseFormat (addArgs.TryGetResult CollectionAddArgs.Output)
                        }
                | _ -> None)
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match CollectionAddFile.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
