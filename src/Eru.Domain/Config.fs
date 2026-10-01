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

// How `eru inbox process` curates a channel's raw items: launched as
// `Command Args` and driven over the Agent Client Protocol (agentclientprotocol.com).
// "acp" is the only supported Protocol value in v1.
type AgentConfig = {
    Protocol         : string
    Command          : string
    Args             : string list
    // Path (absolute, or relative to the inbox's Path) to a file whose content is
    // prepended to every prompt `eru inbox process` sends this agent — typically an
    // agent/subagent definition (e.g. Claude Code's `ingestor.md`) that tells an
    // otherwise-generic ACP agent how to curate a raw item, since the bare captured
    // text alone carries no such instructions. When absent, `InboxProcess` resolves a
    // fallback chain instead: `.agents/agents/ingestor.md` under the inbox if it exists,
    // else a convention keyed by `Command`'s executable name (e.g. `.claude/agents/ingestor.md`
    // for "claude"), else eru's own built-in curation instructions — this last tier
    // always succeeds, so an implicit `InstructionsPath` never leaves an agent with no
    // instructions at all.
    InstructionsPath : string option
    // Per-agent override for how long `eru inbox process` waits for this agent to
    // finish a turn (init + session + full prompt round-trip), in seconds. When absent,
    // the adapter falls back to its own default (120s) — see AcpAgentAdapter.
    Timeout          : int option
}

// An inbox is a local filesystem write-target (e.g. a knowledge repo checkout) that
// `eru inbox send` drops captured messages/files/URLs into. Entirely unrelated to
// SourceConfig/Sources — a source is somewhere eru *pulls from*, an inbox is somewhere
// eru *writes to*. Channels are a map (not a list) and left mostly empty by design: an
// entry is only needed for a channel that wants extra config (e.g. `Agent`, which
// `eru inbox process` uses to curate that channel's raw items).
type InboxChannelConfig = {
    Description : string option
    Agent       : AgentConfig option
}

// For a local inbox `Path` is a directory; for a remote git inbox it is the repo URL
// (see `InboxConfig.isRemote`) and `Branch` optionally names the branch `inbox send`
// pushes to (default: the repo's default branch).
type InboxConfig = {
    Path           : string
    RawPath        : string option
    DefaultChannel : string option
    Channels       : Map<string, InboxChannelConfig>
    Branch         : string option
}

module InboxConfig =
    let isRemotePath (path: string) : bool =
        let p = path.Trim()
        p.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
        || p.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
        || p.StartsWith("git@", System.StringComparison.OrdinalIgnoreCase)
        || p.StartsWith("ssh://", System.StringComparison.OrdinalIgnoreCase)

    let isRemote (inbox: InboxConfig) : bool = isRemotePath inbox.Path

type GlobalDefaults = {
    Branch: string option
    CommitOnPull: bool option
    McpRefreshIntervalMinutes: int option
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
    SiteIgnorePatterns: string list option
    OkfIgnorePatterns: string list option
    DefaultInbox: string option
    InboxWatchIntervalSeconds: int option
}

type GlobalConfig = {
    Version: int
    DefaultSources: SourceConfig list
    Collections: CollectionConfig list
    DefaultInboxes: Map<string, InboxConfig>
    Defaults: GlobalDefaults option
}

type LocalSettings = {
    CommitOnPull: bool option
    StateFile: string option
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
    SiteIgnorePatterns: string list option
    OkfIgnorePatterns: string list option
    DefaultInbox: string option
    InboxWatchIntervalSeconds: int option
}

type LocalConfig = {
    Version: int
    Sources: SourceConfig list
    Collections: CollectionConfig list
    Inboxes: Map<string, InboxConfig>
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
    OkfIgnorePatterns         : string list
    Inboxes                   : Map<string, InboxConfig>
    DefaultInbox              : string option
    InboxWatchIntervalSeconds : int
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
    let defaultSiteIgnorePatterns = ["index.md"; "log.md"; "README.md"]
    // Repo conventions that hold non-concept markdown. Anchored at the bundle root;
    // dot-directories are always skipped separately (see Patterns.isOkfIgnored).
    let defaultOkfIgnorePatterns = ["apm_modules/**"; "inbox/**"; "node_modules/**"]

    let resolveOkfIgnorePatterns (globalCfg: GlobalConfig option) (localCfg: LocalConfig option) : string list =
        match localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.OkfIgnorePatterns) with
        | Some ps -> ps
        | None    ->
            globalCfg
            |> Option.bind (fun g -> g.Defaults)
            |> Option.bind (fun d -> d.OkfIgnorePatterns)
            |> Option.defaultValue defaultOkfIgnorePatterns

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

            let okfIgnorePatterns = resolveOkfIgnorePatterns globalCfg localCfg

            // Inboxes merge by key: a local inbox of a given name wins outright; any
            // global inbox whose name isn't used locally is appended. No cross-reference
            // validation (unlike Sources/Collections) — an inbox's Path is just a plain
            // directory, checked against the filesystem where it's used, not here.
            let globalInboxes = globalCfg |> Option.map (fun g -> g.DefaultInboxes) |> Option.defaultValue Map.empty
            let localInboxes  = localCfg  |> Option.map (fun l -> l.Inboxes)        |> Option.defaultValue Map.empty
            let mergedInboxes =
                globalInboxes
                |> Map.fold (fun acc name inbox -> if Map.containsKey name acc then acc else Map.add name inbox acc) localInboxes

            let defaultInbox =
                localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.DefaultInbox)
                |> Option.orElse (globalCfg |> Option.bind (fun g -> g.Defaults) |> Option.bind (fun d -> d.DefaultInbox))

            let inboxWatchIntervalSeconds =
                match localCfg |> Option.bind (fun l -> l.Settings) |> Option.bind (fun s -> s.InboxWatchIntervalSeconds) with
                | Some i -> i
                | None   ->
                    globalCfg
                    |> Option.bind (fun g -> g.Defaults)
                    |> Option.bind (fun d -> d.InboxWatchIntervalSeconds)
                    |> Option.defaultValue 30

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
                OkfIgnorePatterns = okfIgnorePatterns
                Inboxes = mergedInboxes
                DefaultInbox = defaultInbox
                InboxWatchIntervalSeconds = inboxWatchIntervalSeconds
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

    // Same defence as normalizeBundles, for the Inboxes/DefaultInboxes maps added
    // after Bundles — old config files (or ones that just never set them) leave
    // these fields null rather than an empty map.
    let private normalizeInboxConfig (i: InboxConfig) : InboxConfig =
        if isNull (box i.Channels) then { i with Channels = Map.empty } else i

    let private normalizeInboxMap (m: Map<string, InboxConfig>) : Map<string, InboxConfig> =
        if isNull (box m) then Map.empty
        else m |> Map.map (fun _ v -> normalizeInboxConfig v)

    let migrateGlobalV1 (v1: GlobalConfigV1) : GlobalConfig =
        { Version        = 2
          DefaultSources = v1.DefaultSources |> List.map migrateSourceV1
          Collections    = v1.Collections
          DefaultInboxes = Map.empty
          Defaults       = v1.Defaults }

    let migrateLocalV1 (v1: LocalConfigV1) : LocalConfig =
        { Version     = 2
          Sources     = v1.Sources |> List.map migrateSourceV1
          Collections = v1.Collections
          Inboxes     = Map.empty
          Settings    = v1.Settings }

    let readAndMigrateGlobalJson
        (deserializeCurrent: string -> Result<GlobalConfig, string>)
        (deserializeV1: string -> Result<GlobalConfigV1, string>)
        (json: string) : Result<GlobalConfig, string> =
        match deserializeCurrent json with
        | Ok cfg ->
            Ok { cfg with
                    DefaultSources = cfg.DefaultSources |> List.map normalizeBundles
                    DefaultInboxes = normalizeInboxMap cfg.DefaultInboxes }
        | Error _ -> deserializeV1 json |> Result.map migrateGlobalV1

    let readAndMigrateLocalJson
        (deserializeCurrent: string -> Result<LocalConfig, string>)
        (deserializeV1: string -> Result<LocalConfigV1, string>)
        (json: string) : Result<LocalConfig, string> =
        match deserializeCurrent json with
        | Ok cfg ->
            Ok { cfg with
                    Sources = cfg.Sources |> List.map normalizeBundles
                    Inboxes = normalizeInboxMap cfg.Inboxes }
        | Error _ -> deserializeV1 json |> Result.map migrateLocalV1
