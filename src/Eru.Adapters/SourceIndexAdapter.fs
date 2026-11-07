namespace Eru.Adapters

open System
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

    /// Writes `index` stamped with `version` (the running eru), atomically: a temp file in the same
    /// directory is renamed over the target, so a reader never sees a half-written file. Refuses, leaving
    /// the file untouched, when the index on disk was written by a newer eru (see StaleWriter). The
    /// check-then-write is not atomic across processes; the sync lock narrows that window.
    // The WrittenBy stamp of the index.json on disk, if there is a readable one.
    let private readStamp (path: string) : string option =
        try
            if not (File.Exists path) then None
            else
                match File.ReadAllText path |> Serialization.deserialize<SourceIndex> with
                | Ok idx -> idx.WrittenBy
                | Error _ -> None
        with _ -> None

    let writeIndexToPath (version: string option) (path: string) (index: SourceIndex) : Result<unit, string> =
        let stale =
            match version, readStamp path with
            | Some self, Some onDisk when SemVer.isNewer onDisk self -> Some (StaleWriter.message onDisk self)
            | _ -> None
        match stale with
        | Some msg -> Error msg
        | None ->
            let tmp = $"{path}.{Environment.ProcessId}.tmp"
            try
                try
                    let dir = Path.GetDirectoryName path
                    if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
                    File.WriteAllText(tmp, Serialization.serialize { index with Version = currentVersion; WrittenBy = version })
                    File.Move(tmp, path, true)
                    Ok ()
                with ex -> Error ex.Message
            finally
                try if File.Exists tmp then File.Delete tmp with _ -> ()

    let writeIndex (sourceName: string) (index: SourceIndex) : Result<unit, string> =
        writeIndexToPath EruVersion.current (Paths.sourceIndexPath sourceName) index
