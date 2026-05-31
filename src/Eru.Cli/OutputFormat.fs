module Eru.Cli.OutputFormat

open Spectre.Console

type OutputFormat = Text | Json | Table

let renderError (msg: string) = eprintfn "Error: %s" msg

let renderMessage (msg: string) (format: OutputFormat) =
    match format with
    | Text | Table -> printfn "%s" msg
    | Json ->
        let escaped = msg.Replace("\\", "\\\\").Replace("\"", "\\\"")
        printfn """{"message":"%s"}""" escaped

let parseFormat (s: string option) : OutputFormat =
    match s with
    | None -> Table
    | Some f ->
        match f.ToLowerInvariant() with
        | "json" -> Json
        | "text" -> Text
        | _      -> Table

let makeTable (headers: string list) : Spectre.Console.Table =
    let t = Spectre.Console.Table()
    t.Border <- TableBorder.None
    t.ShowHeaders <- true
    headers |> List.iter (fun h -> t.AddColumn(TableColumn(h)) |> ignore)
    t
