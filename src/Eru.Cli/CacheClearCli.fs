module Eru.Cli.CacheClearCli

open Argu
open System.IO
open System.Text.Json
open Spectre.Console
open Eru.Adapters
open Eru.Cli.OutputFormat

let (|CacheClearCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Cache args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CacheArgs.Clear clearArgs -> Some clearArgs
                | _ -> None)
        | _ -> None)

let private renderText (targets: string list) (dryRun: bool) =
    let verb = if dryRun then "Would delete" else "Deleted"
    for t in targets do
        printfn "%s: %s" verb t

let private renderTable (targets: string list) (dryRun: bool) =
    let status = if dryRun then "Would delete" else "Deleted"
    let table = makeTable [ "Path"; "Status" ]
    for t in targets do
        table.AddRow(t, status) |> ignore
    AnsiConsole.Write(table)

let private renderJson (targets: string list) (deleted: bool) =
    let entries =
        targets
        |> List.map (fun p -> {| path = p; deleted = deleted |})
    printfn "%s" (JsonSerializer.Serialize(entries))

let runClear (clearArgs: ParseResults<CacheClearArgs>) : int =
    let dryRun      = clearArgs.Contains CacheClearArgs.Dryrun
    let autoConfirm = clearArgs.Contains CacheClearArgs.Yes
    let format      = parseFormat (clearArgs.TryGetResult CacheClearArgs.Output)

    let sourcesBase =
        Path.GetDirectoryName(Paths.sourceCacheManifestPath "dummy")
        |> Path.GetDirectoryName
    let targets =
        [ sourcesBase
          Paths.searchIndexDir()
          Paths.collectionCachePath() ]
        |> List.filter Directory.Exists

    if targets.IsEmpty then
        renderMessage "Cache is already empty." format
        0
    elif dryRun then
        match format with
        | Text  -> renderText targets true
        | Json  -> renderJson targets false
        | Table -> renderTable targets true
        0
    else
        match format with
        | Text  -> renderText targets false
        | Json  -> ()
        | Table -> renderTable targets false

        let confirmed =
            if autoConfirm then true
            else
                printf "\nDelete these directories? [y/N] "
                let answer = System.Console.ReadLine()
                answer <> null &&
                (answer.Trim().ToLowerInvariant() = "y" ||
                 answer.Trim().ToLowerInvariant() = "yes")

        if confirmed then
            for t in targets do
                try Directory.Delete(t, true)
                with ex -> eprintfn "Warning: failed to delete %s: %s" t ex.Message
            if format = Json then renderJson targets true
            0
        else
            renderMessage "Aborted." format
            0
