namespace Eru

// Declared by a source repo at .eru/manifest.json
// Path supports glob patterns (same gitignore-style semantics as CollectionFileRef.RemotePath):
//   "docs/*.md"      — all .md files in docs/
//   "dotnet/**/*.md" — recursive match under dotnet/
//   "README.md"      — exact path
type ManifestFileRef = {
    Path        : string
    Tags        : string list
    Description : string option
}

type SourceManifest = {
    Version : int      // kept for future format evolution
    Files   : ManifestFileRef list
}

type SourceConfig = {
    Name: string
    Url: string option
    Branch: string option
    BasePath: string option
}

type CollectionFileRef = {
    Source: string
    RemotePath: string
    Tags: string list
    Description: string option
}

type CollectionConfig = {
    Name: string
    Tags: string list
    Files: CollectionFileRef list
    Description: string option
}

type GlobalDefaults = {
    Branch: string option
    CommitOnPull: bool option
    McpRefreshIntervalMinutes: int option
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
}

type GlobalConfig = {
    Version: int
    DefaultSources: SourceConfig list
    Collections: CollectionConfig list
    Defaults: GlobalDefaults option
}

type LocalSettings = {
    CommitOnPull: bool option
    StateFile: string option
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
}

type LocalConfig = {
    Version: int
    Sources: SourceConfig list
    Collections: CollectionConfig list
    Settings: LocalSettings option
}

type EffectiveConfig = {
    Sources                   : SourceConfig list
    CommitOnPull              : bool
    StateFile                 : string
    Collections               : CollectionFileRef list   // merged from user config + cached manifests
    McpRefreshIntervalMinutes : int
    BlockPatterns             : string list
    AllowPatterns             : string list
    AllowBinaries             : bool
}

// Per-file metadata stored in sources/<name>/index.json, keyed by remotePath
type IndexEntry = {
    Tags         : string list
    Description  : string option
    LocalPath    : string option    // set if the file is in .eru/eru.lock
    CacheRelPath : string option    // relative path under sources/<name>/files/
    ContentHash  : string option    // sha256:<hash> of cached content
}

module Config =
    let defaultBlockPatterns = ["*.exe"; "*.dll"; "*.so"; "*.dylib"; "*.bin"; "*.out"; "*.app"]
    let defaultAllowPatterns : string list = []
    let defaultAllowBinaries = false

    let private supportedVersion = 1

    let private checkVersion label version =
        if version > supportedVersion then
            Error $"Unsupported {label} version {version} — please upgrade eru"
        else Ok ()

    let private checkDuplicateNames label (sources: SourceConfig list) =
        let dups =
            sources
            |> List.map (fun s -> s.Name)
            |> List.groupBy id
            |> List.choose (fun (n, vs) -> if vs.Length > 1 then Some n else None)
        match dups with
        | [] -> Ok ()
        | n :: _ -> Error $"Duplicate source name '{n}' in {label}"

    let private checkGlobalSourceUrls (sources: SourceConfig list) =
        sources
        |> List.tryFind (fun s -> s.Url.IsNone)
        |> Option.map (fun s -> Error $"Source '{s.Name}' in global config has no URL")
        |> Option.defaultValue (Ok ())

    let private checkCollectionSources (collections: CollectionConfig list) (mergedSources: SourceConfig list) =
        let names = mergedSources |> List.map (fun s -> s.Name) |> Set.ofList
        collections
        |> List.tryPick (fun col ->
            col.Files
            |> List.tryPick (fun f ->
                if not (Set.contains f.Source names) then
                    Some (Error $"Collection '{col.Name}' references unknown source '{f.Source}'")
                else None))
        |> Option.defaultValue (Ok ())

    let private resolveLocalSources (localSources: SourceConfig list) (globalSources: SourceConfig list) =
        localSources
        |> List.fold (fun acc ls ->
            match acc with
            | Error e -> Error e
            | Ok resolved ->
                match ls.Url with
                | Some _ -> Ok (resolved @ [ls])
                | None ->
                    globalSources
                    |> List.tryFind (fun gs -> gs.Name = ls.Name)
                    |> Option.map (fun gs -> Ok (resolved @ [gs]))
                    |> Option.defaultWith (fun () ->
                        Error $"Local source '{ls.Name}' has no URL and was not found in global config"))
            (Ok [])

    let merge (globalCfg: GlobalConfig option) (localCfg: LocalConfig option) : Result<EffectiveConfig, string> =
        let validateGlobal =
            match globalCfg with
            | None -> Ok ()
            | Some g ->
                checkVersion "global config" g.Version
                |> Result.bind (fun () -> checkDuplicateNames "global config" g.DefaultSources)
                |> Result.bind (fun () -> checkGlobalSourceUrls g.DefaultSources)

        let validateLocal =
            match localCfg with
            | None -> Ok ()
            | Some l ->
                checkVersion "local config" l.Version
                |> Result.bind (fun () -> checkDuplicateNames "local config" l.Sources)

        validateGlobal
        |> Result.bind (fun () -> validateLocal)
        |> Result.bind (fun () ->
            let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []
            let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []
            resolveLocalSources localSources globalSources
            |> Result.map (fun resolvedLocal ->
                let globalOnly =
                    globalSources
                    |> List.filter (fun gs ->
                        localSources |> List.forall (fun ls -> ls.Name <> gs.Name))
                resolvedLocal @ globalOnly))
        |> Result.bind (fun mergedSources ->
            let validateCollections =
                let globalCols = globalCfg |> Option.map (fun g -> g.Collections) |> Option.defaultValue []
                let localCols  = localCfg  |> Option.map (fun l -> l.Collections) |> Option.defaultValue []
                checkCollectionSources (globalCols @ localCols) mergedSources
            validateCollections |> Result.map (fun () -> mergedSources))
        |> Result.map (fun mergedSources ->
            let globalCommitOnPull =
                globalCfg
                |> Option.bind (fun g -> g.Defaults)
                |> Option.bind (fun d -> d.CommitOnPull)
                |> Option.defaultValue false

            let localCommitOnPull =
                localCfg
                |> Option.bind (fun l -> l.Settings)
                |> Option.bind (fun s -> s.CommitOnPull)

            let stateFile =
                localCfg
                |> Option.bind (fun l -> l.Settings)
                |> Option.bind (fun s -> s.StateFile)
                |> Option.defaultValue "eru.lock"

            let blockPatterns =
                match localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.BlockPatterns) with
                | Some ps -> ps
                | None    ->
                    globalCfg
                    |> Option.bind (fun g -> g.Defaults)
                    |> Option.bind (fun d -> d.BlockPatterns)
                    |> Option.defaultValue defaultBlockPatterns

            let allowPatterns =
                match localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.AllowPatterns) with
                | Some ps -> ps
                | None    ->
                    globalCfg
                    |> Option.bind (fun g -> g.Defaults)
                    |> Option.bind (fun d -> d.AllowPatterns)
                    |> Option.defaultValue defaultAllowPatterns

            let allowBinaries =
                match localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.AllowBinaries) with
                | Some b -> b
                | None   ->
                    globalCfg
                    |> Option.bind (fun g -> g.Defaults)
                    |> Option.bind (fun d -> d.AllowBinaries)
                    |> Option.defaultValue defaultAllowBinaries

            {
                Sources      = mergedSources
                CommitOnPull = localCommitOnPull |> Option.defaultValue globalCommitOnPull
                StateFile    = stateFile
                Collections  =
                    let globalCols = globalCfg |> Option.map (fun g -> g.Collections |> List.collect (fun col -> col.Files)) |> Option.defaultValue []
                    let localCols  = localCfg  |> Option.map (fun l -> l.Collections |> List.collect (fun col -> col.Files)) |> Option.defaultValue []
                    globalCols @ localCols
                McpRefreshIntervalMinutes =
                    globalCfg
                    |> Option.bind (fun g -> g.Defaults)
                    |> Option.bind (fun d -> d.McpRefreshIntervalMinutes)
                    |> Option.defaultValue 60
                BlockPatterns = blockPatterns
                AllowPatterns = allowPatterns
                AllowBinaries = allowBinaries
            })

    let withManifests
        (readCachedManifest: string -> Result<SourceManifest option, string>)
        (cfg: EffectiveConfig) : EffectiveConfig =
        let existingKeys =
            cfg.Collections
            |> List.map (fun f -> f.Source, f.RemotePath)
            |> Set.ofList
        let manifestFiles =
            cfg.Sources
            |> List.collect (fun src ->
                match readCachedManifest src.Name with
                | Ok (Some manifest) ->
                    manifest.Files
                    |> List.map (fun f ->
                        { Source      = src.Name
                          RemotePath  = f.Path
                          Tags        = f.Tags
                          Description = f.Description })
                    |> List.filter (fun f ->
                        not (Set.contains (f.Source, f.RemotePath) existingKeys))
                | _ -> [])
        { cfg with Collections = cfg.Collections @ manifestFiles }

    let resolveByTags (tags: string list) (globalCfg: GlobalConfig) : (string * string) list =
        let normalised = tags |> List.map (fun t -> t.ToLowerInvariant())
        let hasAllTags (itemTags: string list) =
            normalised |> List.forall (fun t -> itemTags |> List.exists (fun it -> it.ToLowerInvariant() = t))

        globalCfg.Collections
        |> List.collect (fun col ->
            let colMatches = hasAllTags col.Tags
            col.Files
            |> List.choose (fun f ->
                if colMatches || hasAllTags f.Tags then Some (f.Source, f.RemotePath)
                else None))
        |> List.distinct
