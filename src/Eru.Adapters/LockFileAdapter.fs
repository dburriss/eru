namespace Eru.Adapters

open Eru
open System.IO

module LockFileAdapter =

    let read (path: string) : Result<LockEntry list, string> =
        if not (File.Exists path) then Ok []
        else
            try File.ReadAllText path |> LockFile.parse
            with ex -> Error ex.Message

    let write (path: string) (entries: LockEntry list) : Result<unit, string> =
        try
            let dir = Path.GetDirectoryName path
            if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, LockFile.write entries)
            Ok ()
        with ex -> Error ex.Message
