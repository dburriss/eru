module Eru.Mcp.CkSearch

open Eru.Adapters

let isAvailable () = CkAdapter.isAvailable ()

let search : SearchFn =
    fun termList candidates ->
        if termList = [] then
            candidates |> List.map (fun f -> f, [])
        else
            candidates |> List.choose (fun f ->
                let pathLower = f.RelPath.ToLowerInvariant()
                let pathHits  = termList |> List.exists pathLower.Contains
                let excerpts  = CkAdapter.searchFile termList f.AbsPath
                if pathHits || not excerpts.IsEmpty then Some (f, excerpts)
                else None)
