namespace Eru.Adapters

open Eru
open System.IO

module SourceIndexAdapter =

    let private currentVersion = 1

    let private normalizeEntry (e: IndexEntry) : IndexEntry =
        { e with
            Tags          = if isNull (box e.Tags) then [] else e.Tags
            Contributions = if isNull (box e.Contributions) then Map.empty else e.Contributions }

    let private normalize (index: SourceIndex) : SourceIndex =
        { index with Entries = index.Entries |> Map.map (fun _ e -> normalizeEntry e) }

    let readIndex (sourceName: string) : Result<SourceIndex option, string> =
        let path = Paths.sourceIndexPath sourceName
        if not (File.Exists path) then Ok None
        else
            try
                let result =
                    File.ReadAllText path
                    |> Serialization.deserialize<SourceIndex>
                match result with
                | Ok idx when idx.Version = currentVersion -> Ok (Some (normalize idx))
                | Ok _ -> Ok None   // version mismatch — force a full rebuild
                | Error _ -> Ok None // malformed JSON — index.json is fully disposable
            with ex -> Error ex.Message

    let writeIndex (sourceName: string) (index: SourceIndex) : Result<unit, string> =
        let path = Paths.sourceIndexPath sourceName
        try
            let dir = Path.GetDirectoryName path
            if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, Serialization.serialize { index with Version = currentVersion })
            Ok ()
        with ex -> Error ex.Message
