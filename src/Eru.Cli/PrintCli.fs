module Eru.Cli.PrintCli

open System.Text.Json
open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Target: string; Format: OutputFormat }

let (|PrintCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Print args ->
            Some {
                Target = args.GetResult PrintArgs.Target
                // Default is raw content, unlike the table default of other commands.
                Format =
                    match args.TryGetResult PrintArgs.Output |> Option.map (fun f -> f.ToLowerInvariant()) with
                    | Some "json" -> Json
                    | _           -> Text
            }
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Read.execute deps cmd.Target with
    | Error e -> renderError e; 1
    | Ok doc ->
        match cmd.Format with
        | Json ->
            let json =
                JsonSerializer.Serialize(
                    {| source = doc.Source; path = doc.RemotePath; hash = doc.Hash; content = doc.Content |})
            printfn "%s" json
        | Text | Table ->
            // Write verbatim so piped output matches the document byte for byte.
            stdout.Write doc.Content
        0
