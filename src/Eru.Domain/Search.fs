namespace Eru

module Search =

    type Query = {
        Terms : string list
        Tags  : string list
    }

    let run (_deps: Deps) (_query: Query) : int =
        printfn "search: not yet implemented"
        0
