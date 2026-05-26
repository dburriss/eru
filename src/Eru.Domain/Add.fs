namespace Eru

module Add =

    type Command = {
        RemotePath : string option
        Tags       : string list
        SourceName : string option
    }

    let run (_deps: Deps) (_cmd: Command) : int =
        printfn "add: not yet implemented"
        0
