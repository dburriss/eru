namespace Eru.Adapters

open Eru
open System.IO

module ManifestAdapter =

    let readCachedManifest (sourceName: string) : Result<SourceManifest option, string> =
        let path = Paths.sourceCacheManifestPath sourceName
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<SourceManifest>
                |> Result.map Some
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
