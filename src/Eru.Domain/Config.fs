namespace Eru

// Identifies an entry within a source: the pair (Source, RemotePath).
type EntryId = { Source: string; RemotePath: string }

module EntryId =
    let toString (id: EntryId) = $"{id.Source}:{id.RemotePath}"

    // Splits on the first ':' only, matching the on-disk eru.lock origin format.
    let tryParse (s: string) : EntryId option =
        match s.IndexOf(':') with
        | -1 -> None
        | i -> Some { Source = s.Substring(0, i); RemotePath = s.Substring(i + 1) }

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
    Version     : int      // kept for future format evolution
    Description : string option
    Files       : ManifestFileRef list
}

// A directory a source publishes from. Path = "" means the repo root.
// Manifest: membership declared by .eru/manifest.json. Okf: membership discovered
// by walking the tree and reading frontmatter (see BundleDiscovery).
type BundleKind = Manifest | Okf

type Bundle = { Path: string; Kind: BundleKind }

type SourceConfig = {
    Name: string
    Url: string option
    Branch: string option
    Bundles: Bundle list
}

type CollectionFileRef = {
    Source: string
    RemotePath: string
    Tags: string list
    Description: string option
}

module CollectionFileRef =
    let id (f: CollectionFileRef) : EntryId = { Source = f.Source; RemotePath = f.RemotePath }

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
    SiteIgnorePatterns: string list option
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
    SiteIgnorePatterns: string list option
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
    SiteIgnorePatterns        : string list
}

// A single bundle's contribution of metadata for one file: either a manifest entry
// (keyed by ContributionKey.manifest bundlePath) or discovered frontmatter
// (keyed by ContributionKey.frontmatter). Stored per-key so a re-sync can fully
// replace one contribution without ever needing to "subtract" stale data from
// an accumulated total.
type Contribution = {
    Tags        : string list
    Description : string option
}

// Per-file metadata stored in sources/<name>/index.json, keyed by remotePath.
// Tags/Description are blended from Contributions via IndexBlend.blend and
// stored here so existing consumers (Search, SourceFiles, McpTools, LinkGraph,
// SourceView) keep reading them unchanged.
type IndexEntry = {
    Contributions : Map<string, Contribution>
    Tags         : string list
    Description  : string option
    LocalPath    : string option    // set if the file is in .eru/eru.lock
    CacheRelPath : string option    // relative path under sources/<name>/files/
    ContentHash  : string option    // sha256:<hash> of cached content
    Type         : string option              // OKF: type
    Title        : string option              // OKF: title
    OkfStatus    : string option              // OKF: status (draft|stable|deprecated)
    Generated    : Frontmatter.ActorAt option // OKF: generated
    Verified     : Frontmatter.ActorAt list   // OKF: verified
    StaleAfter   : System.DateTimeOffset option // OKF: stale_after
    Resource     : string option              // OKF: resource
}

// sources/<name>/index.json's top-level shape. Version mismatch or malformed
// JSON forces a full rebuild — the index is fully disposable/reconstructible.
// SourceHeadSha/ConsecutiveShaCheckFailures cache OKF-bundle discovery's
// git-ls-remote check so a re-sync can skip re-walking unchanged sources.
type SourceIndex = {
    Version                     : int
    SourceHeadSha               : string option
    ConsecutiveShaCheckFailures : int
    Entries                     : Map<string, IndexEntry>
}

// Version-1 shapes, kept only to migrate old on-disk config.json files (BasePath -> Bundles).
type SourceConfigV1 = {
    Name: string
    Url: string option
    Branch: string option
    BasePath: string option
}

type GlobalConfigV1 = {
    Version: int
    DefaultSources: SourceConfigV1 list
    Collections: CollectionConfig list
    Defaults: GlobalDefaults option
}

type LocalConfigV1 = {
    Version: int
    Sources: SourceConfigV1 list
    Collections: CollectionConfig list
    Settings: LocalSettings option
}

module IndexEntry =
    let empty : IndexEntry = {
        Contributions = Map.empty
        Tags          = []
        Description   = None
        LocalPath     = None
        CacheRelPath  = None
        ContentHash   = None
        Type          = None
        Title         = None
        OkfStatus     = None
        Generated     = None
        Verified      = []
        StaleAfter    = None
        Resource      = None
    }

module Bundle =
    // Does `bundle` cover `path`? Root bundles (Path = "") cover everything.
    let covers (bundle: Bundle) (path: string) : bool =
        if bundle.Path = "" then true
        else
            let prefix = if bundle.Path.EndsWith('/') then bundle.Path else bundle.Path + "/"
            path = bundle.Path || path.StartsWith(prefix)

    let coveringBundles (bundles: Bundle list) (path: string) : Bundle list =
        bundles |> List.filter (fun b -> covers b path)

    // The most specific (longest Path) Manifest-kind bundle covering `path`, if any.
    let mostSpecificManifestBundle (bundles: Bundle list) (path: string) : Bundle option =
        bundles
        |> List.filter (fun b -> b.Kind = Manifest && covers b path)
        |> List.sortByDescending (fun b -> b.Path.Length)
        |> List.tryHead

    // The bundle that "owns" `path` for display/grouping purposes: prefer the most
    // specific covering Manifest bundle, else fall back to the most specific
    // covering bundle of any kind.
    let owningBundleForDisplay (bundles: Bundle list) (path: string) : Bundle option =
        match mostSpecificManifestBundle bundles path with
        | Some b -> Some b
        | None ->
            bundles
            |> List.filter (fun b -> covers b path)
            |> List.sortByDescending (fun b -> b.Path.Length)
            |> List.tryHead

module ContributionKey =
    let manifest (bundlePath: string) = $"manifest:{bundlePath}"
    let frontmatter = "frontmatter"

module BundleDetect =
    // Existing KNOWLEDGE/knowledge top-level convention match, unchanged.
    let candidatePath (topLevel: string list) : string option =
        topLevel |> List.tryFind (fun e -> e = "KNOWLEDGE" || e = "knowledge")

    let detectKind (indexMdHasOkfVersion: bool) : BundleKind =
        if indexMdHasOkfVersion then Okf else Manifest

// Result of blending a file's per-bundle Contributions into the flat Tags/Description
// shape every other module reads.
type BlendResult = { Tags: string list; Description: string option }

module IndexBlend =
    // Tags: union of every contribution's tags (always additive, never lossy).
    // Description: the most-specific covering Manifest bundle's own contribution if
    // present, else the "frontmatter" contribution.
    let blend (bundles: Bundle list) (remotePath: string) (contributions: Map<string, Contribution>) : BlendResult =
        let tags =
            contributions
            |> Map.toList
            |> List.collect (fun (_, c) -> c.Tags)
            |> List.distinct

        let frontmatterDescription =
            contributions |> Map.tryFind ContributionKey.frontmatter |> Option.bind (fun c -> c.Description)

        let description =
            match Bundle.mostSpecificManifestBundle bundles remotePath with
            | Some b ->
                match contributions |> Map.tryFind (ContributionKey.manifest b.Path) with
                | Some c when c.Description.IsSome -> c.Description
                | _ -> frontmatterDescription
            | None -> frontmatterDescription

        { Tags = tags; Description = description }

module Config =
    let defaultBlockPatterns = ["*.exe"; "*.dll"; "*.so"; "*.dylib"; "*.bin"; "*.out"; "*.app"]
    let defaultAllowPatterns : string list = []
    let defaultAllowBinaries = false
    let defaultSiteIgnorePatterns = ["index.md"; "log.md"]

    let private supportedVersion = 2

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

            let siteIgnorePatterns =
                match localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.SiteIgnorePatterns) with
                | Some ps -> ps
                | None    ->
                    globalCfg
                    |> Option.bind (fun g -> g.Defaults)
                    |> Option.bind (fun d -> d.SiteIgnorePatterns)
                    |> Option.defaultValue defaultSiteIgnorePatterns

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
                SiteIgnorePatterns = siteIgnorePatterns
            })

    let withManifests
        (readCachedManifest: string -> Result<SourceManifest option, string>)
        (cfg: EffectiveConfig) : EffectiveConfig =
        let existingKeys =
            cfg.Collections
            |> List.map CollectionFileRef.id
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
                        not (Set.contains (CollectionFileRef.id f) existingKeys))
                | _ -> [])
        { cfg with Collections = cfg.Collections @ manifestFiles }

    let resolveByTags (tags: string list) (globalCfg: GlobalConfig) : EntryId list =
        let normalised = tags |> List.map (fun t -> t.ToLowerInvariant())
        let hasAllTags (itemTags: string list) =
            normalised |> List.forall (fun t -> itemTags |> List.exists (fun it -> it.ToLowerInvariant() = t))

        globalCfg.Collections
        |> List.collect (fun col ->
            let colMatches = hasAllTags col.Tags
            col.Files
            |> List.choose (fun f ->
                if colMatches || hasAllTags f.Tags then Some (CollectionFileRef.id f)
                else None))
        |> List.distinct

    // --- Config v1 -> v2 migration (BasePath -> Bundles) ---
    // Migration happens at the adapter boundary (ConfigAdapter.readGlobalConfig /
    // readLocalConfig), not here in merge — merge stays pure and version-agnostic
    // beyond checkVersion's upper-bound guard. The migrated result is not written
    // back to disk until the next explicit save ("refresh in place").

    let private migrateSourceV1 (v1: SourceConfigV1) : SourceConfig =
        { Name    = v1.Name
          Url     = v1.Url
          Branch  = v1.Branch
          Bundles =
            match v1.BasePath with
            | None    -> []
            | Some "" -> []
            | Some p  -> [ { Path = p; Kind = Manifest } ] }

    // Defends against System.Text.Json silently leaving a missing list field null
    // rather than throwing, regardless of exactly which failure mode a given
    // deserializer surfaces for old-format JSON.
    let private normalizeBundles (s: SourceConfig) : SourceConfig =
        if isNull (box s.Bundles) then { s with Bundles = [] } else s

    let migrateGlobalV1 (v1: GlobalConfigV1) : GlobalConfig =
        { Version        = 2
          DefaultSources = v1.DefaultSources |> List.map migrateSourceV1
          Collections    = v1.Collections
          Defaults       = v1.Defaults }

    let migrateLocalV1 (v1: LocalConfigV1) : LocalConfig =
        { Version     = 2
          Sources     = v1.Sources |> List.map migrateSourceV1
          Collections = v1.Collections
          Settings    = v1.Settings }

    let readAndMigrateGlobalJson
        (deserializeCurrent: string -> Result<GlobalConfig, string>)
        (deserializeV1: string -> Result<GlobalConfigV1, string>)
        (json: string) : Result<GlobalConfig, string> =
        match deserializeCurrent json with
        | Ok cfg -> Ok { cfg with DefaultSources = cfg.DefaultSources |> List.map normalizeBundles }
        | Error _ -> deserializeV1 json |> Result.map migrateGlobalV1

    let readAndMigrateLocalJson
        (deserializeCurrent: string -> Result<LocalConfig, string>)
        (deserializeV1: string -> Result<LocalConfigV1, string>)
        (json: string) : Result<LocalConfig, string> =
        match deserializeCurrent json with
        | Ok cfg -> Ok { cfg with Sources = cfg.Sources |> List.map normalizeBundles }
        | Error _ -> deserializeV1 json |> Result.map migrateLocalV1
