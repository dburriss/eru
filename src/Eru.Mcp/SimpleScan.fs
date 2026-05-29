module Eru.Mcp.SimpleScan

open System.IO

let search : SearchFn =
    fun termList candidates ->
        if termList = [] then
            candidates |> List.map (fun f -> f, [])
        else
            candidates |> List.choose (fun f ->
                try
                    let lines     = File.ReadAllLines f.AbsPath
                    let pathLower = f.RelPath.ToLowerInvariant()
                    let pathHits  = termList |> List.exists pathLower.Contains
                    let matchingLines =
                        lines
                        |> Array.filter (fun l ->
                            let ll = l.ToLowerInvariant()
                            termList |> List.exists ll.Contains)
                        |> Array.map (fun l -> l.Trim())
                        |> Array.toList
                    if pathHits || not matchingLines.IsEmpty then Some (f, matchingLines)
                    else None
                with _ -> None)
