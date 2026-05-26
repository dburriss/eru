namespace Eru

module Sync =

    type Options = { DryRun: bool }

    let run (_deps: Deps) (_opts: Options) : int =
        printfn "sync: not yet implemented"
        0
