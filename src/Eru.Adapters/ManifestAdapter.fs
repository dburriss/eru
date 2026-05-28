namespace Eru.Adapters

open Eru
open System.IO

module ManifestAdapter =

    let readLocalManifest (cwd: string) : Result<SourceManifest option, string> =
        let path = Paths.localManifestPath cwd
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<SourceManifest>
                |> Result.map Some
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
