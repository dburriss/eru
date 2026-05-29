module Eru.Mcp.IndexedSearch

open Eru.Adapters

let search : SearchFn =
    fun termList candidates ->
        if termList = [] then
            candidates |> List.map (fun f -> f, [])
        else
            candidates |> List.choose (fun f ->
                let pathLower   = f.RelPath.ToLowerInvariant()
                let pathHits    = termList |> List.exists pathLower.Contains
                let queryTokens = termList |> List.collect SearchIndexAdapter.tokenize |> List.distinct
                match SearchIndexAdapter.getOrBuild f.AbsPath with
                | None when not pathHits -> None
                | idxOpt ->
                    let excerpts =
                        match idxOpt with
                        | None -> []
                        | Some idx ->
                            queryTokens
                            |> List.collect (fun t ->
                                idx.Words
                                |> List.tryFind (fun w -> w.Word = t)
                                |> Option.map (fun w -> w.Lines)
                                |> Option.defaultValue [])
                            |> List.distinct
                            |> List.sort
                            |> List.choose (fun n ->
                                idx.Lines |> List.tryFind (fun l -> l.Num = n)
                                |> Option.map (fun l -> l.Text))
                    if pathHits || not excerpts.IsEmpty then Some (f, excerpts)
                    else None)
