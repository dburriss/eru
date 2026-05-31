namespace Eru.Adapters

open Eru
open System.IO

module ManifestAdapter =

    // System.Text.Json sets F# list fields to null when the JSON field is absent or null.
    // Normalize after deserialization so consumers never see null Tags/Files.
    let private normalize (manifest: SourceManifest) : SourceManifest =
        { manifest with
            Files =
                if isNull (box manifest.Files) then []
                else
                    manifest.Files |> List.map (fun f ->
                        { f with Tags =
                                    if isNull (box f.Tags) then []
                                    else f.Tags |> List.filter (fun t -> not (isNull t)) }) }

    let readLocalManifest (cwd: string) : Result<SourceManifest option, string> =
        let path = Paths.localManifestPath cwd
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<SourceManifest>
                |> Result.map (normalize >> Some)
            with ex -> Error ex.Message

    let writeLocalManifest (cwd: string) (manifest: SourceManifest) : Result<unit, string> =
        let path = Paths.localManifestPath cwd
        try
            let dir = Path.GetDirectoryName path
            if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, Serialization.serialize manifest)
            Ok ()
        with ex -> Error ex.Message

    let resolveLocalGlob (cwd: string) (pattern: string) : string list =
        try
            Directory.EnumerateFiles(cwd, "*", SearchOption.AllDirectories)
            |> Seq.map (fun f -> Path.GetRelativePath(cwd, f).Replace('\\', '/'))
            |> Seq.filter (Patterns.matchesGlob pattern)
            |> Seq.toList
        with _ -> []


    let readCachedManifest (sourceName: string) : Result<SourceManifest option, string> =
        let path = Paths.sourceCacheManifestPath sourceName
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<SourceManifest>
                |> Result.map (normalize >> Some)
            with ex -> Error ex.Message

    let cacheSourceManifest (sourceName: string) (rawJson: string) : Result<unit, string> =
        let path = Paths.sourceCacheManifestPath sourceName
        try
            // Validate that the JSON is a well-formed SourceManifest before writing
            match Serialization.deserialize<SourceManifest> rawJson with
            | Error e -> Error $"invalid manifest JSON: {e}"
            | Ok _ ->
                let dir = Path.GetDirectoryName path
                if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
                File.WriteAllText(path, rawJson)
                Ok ()
        with ex -> Error ex.Message
