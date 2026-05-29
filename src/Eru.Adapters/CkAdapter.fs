namespace Eru.Adapters

open System
open SimpleExec

module CkAdapter =

    let isAvailable () =
        try
            Command.ReadAsync("ck", "--version").Result |> ignore
            true
        with _ -> false

    let indexDir (dir: string) =
        try Command.ReadAsync("ck", $"--index \"{dir}\"").Result |> ignore
        with _ -> ()

    let searchFile (termList: string list) (absPath: string) : string list =
        try
            let query = (termList |> String.concat " ").Replace("\"", "")
            let struct (stdout, _) =
                Command.ReadAsync("ck", $"--hybrid -n --no-filename \"{query}\" \"{absPath}\"").Result
            stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            |> Array.choose (fun line ->
                let parts = line.Split(':', 2)
                if parts.Length = 2 then Some (parts.[1].Trim())
                else None)
            |> Array.toList
        with _ -> []
