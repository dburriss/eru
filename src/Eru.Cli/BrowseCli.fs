module Eru.Cli.BrowseCli

open Argu
open Eru

type Cmd = unit

let (|BrowseCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Browse _ -> Some ()
        | _                -> None)

let run (deps: Deps) (_cmd: Cmd) : int =
    Eru.Tui.BrowseTui.run deps
