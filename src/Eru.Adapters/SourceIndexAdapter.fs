namespace Eru.Adapters

open Eru
open System.IO

module SourceIndexAdapter =

    let private normalize (index: Map<string, IndexEntry>) : Map<string, IndexEntry> =
        index |> Map.map (fun _ e ->
            { e with Tags = if isNull (box e.Tags) then [] else e.Tags })

    let readIndex (sourceName: string) : Result<Map<string, IndexEntry> option, string> =
        let path = Paths.sourceIndexPath sourceName
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<Map<string, IndexEntry>>
                |> Result.map (normalize >> Some)
            with ex -> Error ex.Message

    let writeIndex (sourceName: string) (index: Map<string, IndexEntry>) : Result<unit, string> =
        let path = Paths.sourceIndexPath sourceName
        try
            let dir = Path.GetDirectoryName path
            if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, Serialization.serialize index)
            Ok ()
        with ex -> Error ex.Message
