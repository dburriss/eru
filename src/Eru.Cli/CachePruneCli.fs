module Eru.Cli.CachePruneCli

open Argu
open System.IO
open Eru.Adapters

let (|CachePruneCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Cache args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CacheArgs.Prune pruneArgs -> Some pruneArgs
                | _ -> None)
        | _ -> None)

let runPrune (pruneArgs: ParseResults<CachePruneArgs>) : int =
    let autoConfirm = pruneArgs.Contains CachePruneArgs.Yes

    let sourcesBase = Path.GetDirectoryName(Paths.sourceCacheManifestPath "dummy") |> Path.GetDirectoryName

    if not (Directory.Exists sourcesBase) then
        printfn "No source cache directory found."
        0
    else
        let orphans = System.Collections.Generic.List<string>()

        for sourceDir in Directory.EnumerateDirectories sourcesBase do
            let sourceName = Path.GetFileName sourceDir
            let filesDir   = Path.Combine(sourceDir, "files")
            if Directory.Exists filesDir then
                let referencedHexes =
                    match SourceIndexAdapter.readIndex sourceName with
                    | Ok (Some idx) ->
                        idx |> Map.toSeq
                        |> Seq.choose (fun (_, entry) ->
                            entry.CacheRelPath |> Option.map (fun rp ->
                                // CacheRelPath = "files/<hex>"; extract the hex filename
                                Path.GetFileName rp))
                        |> Set.ofSeq
                    | _ -> Set.empty

                for file in Directory.EnumerateFiles filesDir do
                    let hex = Path.GetFileName file
                    if not (Set.contains hex referencedHexes) then
                        orphans.Add(file)

        if orphans.Count = 0 then
            printfn "Cache is clean — no orphaned files found."
            0
        else
            printfn $"Found {orphans.Count} orphaned cache file(s):"
            for f in orphans do
                printfn $"  {f}"

            let confirmed =
                if autoConfirm then true
                else
                    printf "\nDelete these files? [y/N] "
                    let answer = System.Console.ReadLine()
                    answer <> null && (answer.Trim().ToLowerInvariant() = "y" ||
                                       answer.Trim().ToLowerInvariant() = "yes")

            if confirmed then
                for f in orphans do
                    try File.Delete f
                    with ex -> eprintfn $"Warning: failed to delete {f}: {ex.Message}"
                printfn $"Deleted {orphans.Count} file(s)."
                0
            else
                printfn "Aborted."
                0
