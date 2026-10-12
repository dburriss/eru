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

// Config v3: settings are grouped into top-level sections, shared by the global and
// local files (local overrides global per key). Section names are singular and the
// entity collection inside a section is always called `items`, so a setting can later
// be added to any section without breaking the file shape. All fields are optional so
// an absent key falls through to the other file and then to the built-in default.
type OkfSection = {
    IgnorePatterns: string list option
}

type McpSection = {
    RefreshIntervalMinutes: int option
}

type SyncSection = {
    Branch: string option
    CommitOnPull: bool option
    StateFile: string option
}

type FilesSection = {
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
}

// Items are keyed by inbox name; merged by name, local wins.
type InboxSection = {
    Default: string option
    WatchIntervalSeconds: int option
    Items: Map<string, InboxConfig> option
}

// Merged by name, local wins.
type SourceSection = {
    Items: SourceConfig list option
}

// Merged by concatenation (global then local), not by name.
type CollectionSection = {
    Items: CollectionConfig list option
}

type GlobalConfig = {
    Version: int
    Site: SiteSection option
    Okf: OkfSection option
    Mcp: McpSection option
    Sync: SyncSection option
    Files: FilesSection option
    Inbox: InboxSection option
    Source: SourceSection option
    Collection: CollectionSection option
}

type LocalConfig = {
    Version: int
    Site: SiteSection option
    Okf: OkfSection option
    Mcp: McpSection option
    Sync: SyncSection option
    Files: FilesSection option
    Inbox: InboxSection option
    Source: SourceSection option
    Collection: CollectionSection option
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
    SiteHideEmptyBundles      : bool
    Site                      : SiteSettings
    OkfIgnorePatterns         : string list
    Inboxes                   : Map<string, InboxConfig>
    DefaultInbox              : string option
    InboxWatchIntervalSeconds : int
}

module SourceConfig =
    let empty : SourceConfig = { Name = ""; Url = None; Branch = None; Bundles = [] }

module OkfSection =
    let empty : OkfSection = { IgnorePatterns = None }

module McpSection =
    let empty : McpSection = { RefreshIntervalMinutes = None }

module SyncSection =
    let empty : SyncSection = { Branch = None; CommitOnPull = None; StateFile = None }

module FilesSection =
    let empty : FilesSection = { BlockPatterns = None; AllowPatterns = None; AllowBinaries = None }

module InboxSection =
    let empty : InboxSection = { Default = None; WatchIntervalSeconds = None; Items = None }

module SourceSection =
    let empty : SourceSection = { Items = None }

module CollectionSection =
    let empty : CollectionSection = { Items = None }

// Accessors keep entity commands (source/inbox/collection add, remove, ...) independent
// of how deeply the entity lists are nested in the sections.
module GlobalConfig =
    let empty : GlobalConfig = {
        Version = 3
        Site = None; Okf = None; Mcp = None; Sync = None; Files = None
        Inbox = None; Source = None; Collection = None
    }

    let sources (g: GlobalConfig) : SourceConfig list =
        g.Source |> Option.bind (fun s -> s.Items) |> Option.defaultValue []

    let withSources (items: SourceConfig list) (g: GlobalConfig) : GlobalConfig =
        { g with Source = Some { (g.Source |> Option.defaultValue SourceSection.empty) with Items = Some items } }

    let collections (g: GlobalConfig) : CollectionConfig list =
        g.Collection |> Option.bind (fun s -> s.Items) |> Option.defaultValue []

    let withCollections (items: CollectionConfig list) (g: GlobalConfig) : GlobalConfig =
        { g with Collection = Some { (g.Collection |> Option.defaultValue CollectionSection.empty) with Items = Some items } }

    let inboxes (g: GlobalConfig) : Map<string, InboxConfig> =
        g.Inbox |> Option.bind (fun s -> s.Items) |> Option.defaultValue Map.empty

    let withInboxes (items: Map<string, InboxConfig>) (g: GlobalConfig) : GlobalConfig =
        { g with Inbox = Some { (g.Inbox |> Option.defaultValue InboxSection.empty) with Items = Some items } }

    let withDefaultInbox (name: string) (g: GlobalConfig) : GlobalConfig =
        { g with Inbox = Some { (g.Inbox |> Option.defaultValue InboxSection.empty) with Default = Some name } }

module LocalConfig =
    let empty : LocalConfig = {
        Version = 3
        Site = None; Okf = None; Mcp = None; Sync = None; Files = None
        Inbox = None; Source = None; Collection = None
    }

    let sources (l: LocalConfig) : SourceConfig list =
        l.Source |> Option.bind (fun s -> s.Items) |> Option.defaultValue []

    let withSources (items: SourceConfig list) (l: LocalConfig) : LocalConfig =
        { l with Source = Some { (l.Source |> Option.defaultValue SourceSection.empty) with Items = Some items } }

    let collections (l: LocalConfig) : CollectionConfig list =
        l.Collection |> Option.bind (fun s -> s.Items) |> Option.defaultValue []

    let withCollections (items: CollectionConfig list) (l: LocalConfig) : LocalConfig =
        { l with Collection = Some { (l.Collection |> Option.defaultValue CollectionSection.empty) with Items = Some items } }

    let inboxes (l: LocalConfig) : Map<string, InboxConfig> =
        l.Inbox |> Option.bind (fun s -> s.Items) |> Option.defaultValue Map.empty

    let withInboxes (items: Map<string, InboxConfig>) (l: LocalConfig) : LocalConfig =
        { l with Inbox = Some { (l.Inbox |> Option.defaultValue InboxSection.empty) with Items = Some items } }

    let withDefaultInbox (name: string) (l: LocalConfig) : LocalConfig =
        { l with Inbox = Some { (l.Inbox |> Option.defaultValue InboxSection.empty) with Default = Some name } }

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

// Version-2 shapes: flat `defaults` (global) / `settings` (local) bags, kept only to
// migrate old on-disk config files to the v3 sections.
type GlobalDefaultsV2 = {
    Branch: string option
    CommitOnPull: bool option
    McpRefreshIntervalMinutes: int option
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
    SiteIgnorePatterns: string list option
    SiteHideEmptyBundles: bool option
    OkfIgnorePatterns: string list option
    DefaultInbox: string option
    InboxWatchIntervalSeconds: int option
}

type LocalSettingsV2 = {
    CommitOnPull: bool option
    StateFile: string option
    BlockPatterns: string list option
    AllowPatterns: string list option
    AllowBinaries: bool option
    SiteIgnorePatterns: string list option
    SiteHideEmptyBundles: bool option
    OkfIgnorePatterns: string list option
    DefaultInbox: string option
    InboxWatchIntervalSeconds: int option
}

type GlobalConfigV2 = {
    Version: int
    DefaultSources: SourceConfig list
    Collections: CollectionConfig list
    DefaultInboxes: Map<string, InboxConfig>
    Defaults: GlobalDefaultsV2 option
}

type LocalConfigV2 = {
    Version: int
    Sources: SourceConfig list
    Collections: CollectionConfig list
    Inboxes: Map<string, InboxConfig>
    Settings: LocalSettingsV2 option
}

type GlobalConfigV1 = {
    Version: int
    DefaultSources: SourceConfigV1 list
    Collections: CollectionConfig list
    Defaults: GlobalDefaultsV2 option
}

type LocalConfigV1 = {
    Version: int
    Sources: SourceConfigV1 list
    Collections: CollectionConfig list
    Settings: LocalSettingsV2 option
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

    // The most specific (longest Path) bundle of any kind covering `path`, if any.
    let mostSpecificBundle (bundles: Bundle list) (path: string) : Bundle option =
        bundles
        |> List.filter (fun b -> covers b path)
        |> List.sortByDescending (fun b -> b.Path.Length)
        |> List.tryHead

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
    let defaultSiteHideEmptyBundles = false
    // Repo conventions that hold non-concept markdown. Anchored at the bundle root;
    // dot-directories are always skipped separately (see Patterns.isOkfIgnored).
    let defaultOkfIgnorePatterns = ["apm_modules/**"; "inbox/**"; "node_modules/**"]

    // First Some wins: the local file's section value, then the global file's.
    let private first (local: 'S option) (fromGlobal: 'S option) (get: 'S -> 'a option) : 'a option =
        local |> Option.bind get |> Option.orElse (fromGlobal |> Option.bind get)

    let resolveOkfIgnorePatterns (globalCfg: GlobalConfig option) (localCfg: LocalConfig option) : string list =
        first
            (localCfg  |> Option.bind (fun l -> l.Okf))
            (globalCfg |> Option.bind (fun g -> g.Okf))
            (fun (s: OkfSection) -> s.IgnorePatterns)
        |> Option.defaultValue defaultOkfIgnorePatterns

    let supportedVersion = 3

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
        let globalSources = globalCfg |> Option.map GlobalConfig.sources |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map LocalConfig.sources  |> Option.defaultValue []

        let validateGlobal =
            match globalCfg with
            | None -> Ok ()
            | Some g ->
                checkVersion "global config" g.Version
                |> Result.bind (fun () -> checkDuplicateNames "global config" globalSources)
                |> Result.bind (fun () -> checkGlobalSourceUrls globalSources)

        let validateLocal =
            match localCfg with
            | None -> Ok ()
            | Some l ->
                checkVersion "local config" l.Version
                |> Result.bind (fun () -> checkDuplicateNames "local config" localSources)

        validateGlobal
        |> Result.bind (fun () -> validateLocal)
        |> Result.bind (fun () ->
            resolveLocalSources localSources globalSources
            |> Result.map (fun resolvedLocal ->
                let globalOnly =
                    globalSources
                    |> List.filter (fun gs ->
                        localSources |> List.forall (fun ls -> ls.Name <> gs.Name))
                resolvedLocal @ globalOnly))
        |> Result.bind (fun mergedSources ->
            let validateCollections =
                let globalCols = globalCfg |> Option.map GlobalConfig.collections |> Option.defaultValue []
                let localCols  = localCfg  |> Option.map LocalConfig.collections  |> Option.defaultValue []
                checkCollectionSources (globalCols @ localCols) mergedSources
            validateCollections |> Result.map (fun () -> mergedSources))
        |> Result.bind (fun mergedSources ->
            let lSite  = localCfg  |> Option.bind (fun l -> l.Site)
            let gSite  = globalCfg |> Option.bind (fun g -> g.Site)
            let parseSite (who: string) (sec: SiteSection option) =
                match sec with
                | None -> Ok SiteSettings.empty
                | Some s -> SiteSettings.parse s |> Result.mapError (fun e -> $"{who}: {e}")
            parseSite "global config" gSite
            |> Result.bind (fun gs ->
                parseSite "local config" lSite
                |> Result.map (fun ls -> SiteSettings.merge gs ls))
            |> Result.bind (fun site -> SiteSettings.validate site |> Result.map (fun () -> site))
            |> Result.map (fun site ->
            let lMcp   = localCfg  |> Option.bind (fun l -> l.Mcp)
            let gMcp   = globalCfg |> Option.bind (fun g -> g.Mcp)
            let lSync  = localCfg  |> Option.bind (fun l -> l.Sync)
            let gSync  = globalCfg |> Option.bind (fun g -> g.Sync)
            let lFiles = localCfg  |> Option.bind (fun l -> l.Files)
            let gFiles = globalCfg |> Option.bind (fun g -> g.Files)
            let lInbox = localCfg  |> Option.bind (fun l -> l.Inbox)
            let gInbox = globalCfg |> Option.bind (fun g -> g.Inbox)

            // Inboxes merge by key: a local inbox of a given name wins outright; any
            // global inbox whose name isn't used locally is appended. No cross-reference
            // validation (unlike Sources/Collections) — an inbox's Path is just a plain
            // directory, checked against the filesystem where it's used, not here.
            let globalInboxes = globalCfg |> Option.map GlobalConfig.inboxes |> Option.defaultValue Map.empty
            let localInboxes  = localCfg  |> Option.map LocalConfig.inboxes  |> Option.defaultValue Map.empty
            let mergedInboxes =
                globalInboxes
                |> Map.fold (fun acc name inbox -> if Map.containsKey name acc then acc else Map.add name inbox acc) localInboxes

            let collectionFiles (cols: CollectionConfig list) = cols |> List.collect (fun col -> col.Files)

            {
                Sources      = mergedSources
                CommitOnPull = first lSync gSync (fun (s: SyncSection) -> s.CommitOnPull) |> Option.defaultValue false
                StateFile    = first lSync gSync (fun (s: SyncSection) -> s.StateFile) |> Option.defaultValue "eru.lock"
                Collections  =
                    let globalCols = globalCfg |> Option.map (GlobalConfig.collections >> collectionFiles) |> Option.defaultValue []
                    let localCols  = localCfg  |> Option.map (LocalConfig.collections  >> collectionFiles) |> Option.defaultValue []
                    globalCols @ localCols
                McpRefreshIntervalMinutes =
                    first lMcp gMcp (fun (s: McpSection) -> s.RefreshIntervalMinutes) |> Option.defaultValue 60
                BlockPatterns = first lFiles gFiles (fun (s: FilesSection) -> s.BlockPatterns) |> Option.defaultValue defaultBlockPatterns
                AllowPatterns = first lFiles gFiles (fun (s: FilesSection) -> s.AllowPatterns) |> Option.defaultValue defaultAllowPatterns
                AllowBinaries = first lFiles gFiles (fun (s: FilesSection) -> s.AllowBinaries) |> Option.defaultValue defaultAllowBinaries
                SiteIgnorePatterns = first lSite gSite (fun (s: SiteSection) -> s.IgnorePatterns) |> Option.defaultValue defaultSiteIgnorePatterns
                SiteHideEmptyBundles = first lSite gSite (fun (s: SiteSection) -> s.HideEmptyBundles) |> Option.defaultValue defaultSiteHideEmptyBundles
                Site = site
                OkfIgnorePatterns = resolveOkfIgnorePatterns globalCfg localCfg
                Inboxes = mergedInboxes
                DefaultInbox = first lInbox gInbox (fun (s: InboxSection) -> s.Default)
                InboxWatchIntervalSeconds = first lInbox gInbox (fun (s: InboxSection) -> s.WatchIntervalSeconds) |> Option.defaultValue 30
            }))

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

        GlobalConfig.collections globalCfg
        |> List.collect (fun col ->
            let colMatches = hasAllTags col.Tags
            col.Files
            |> List.choose (fun f ->
                if colMatches || hasAllTags f.Tags then Some (CollectionFileRef.id f)
                else None))
        |> List.distinct

    // --- Config migration (v1 -> v2 -> v3) ---
    // Migration happens at the adapter boundary (ConfigAdapter.readGlobalConfig /
    // readLocalConfig), not here in merge — merge stays pure and version-agnostic
    // beyond checkVersion's upper-bound guard. The migrated result is not written
    // back to disk until the next explicit save ("refresh in place").
    //   v1 -> v2: SourceConfig.BasePath became Bundles.
    //   v2 -> v3: the flat `defaults` / `settings` bags and the entity lists
    //             (sources, inboxes, collections) became top-level sections.

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

    // Same defence as normalizeBundles, for inbox channels — configs that never set
    // them leave the field null rather than an empty map.
    let private normalizeInboxConfig (i: InboxConfig) : InboxConfig =
        if isNull (box i.Channels) then { i with Channels = Map.empty } else i

    let private normalizeInboxMap (m: Map<string, InboxConfig>) : Map<string, InboxConfig> =
        if isNull (box m) then Map.empty
        else m |> Map.map (fun _ v -> normalizeInboxConfig v)

    let private normalizeSourceSection (s: SourceSection option) : SourceSection option =
        let fix (items: SourceConfig list) = if isNull (box items) then [] else items |> List.map normalizeBundles
        s |> Option.map (fun sec -> { sec with Items = sec.Items |> Option.map fix })

    let private normalizeInboxSection (s: InboxSection option) : InboxSection option =
        s |> Option.map (fun sec -> { sec with Items = sec.Items |> Option.map normalizeInboxMap })

    let private normalizeCollectionSection (s: CollectionSection option) : CollectionSection option =
        let fix (items: CollectionConfig list) = if isNull (box items) then [] else items
        s |> Option.map (fun sec -> { sec with Items = sec.Items |> Option.map fix })

    let private normalizeGlobal (cfg: GlobalConfig) : GlobalConfig =
        { cfg with
            Source     = normalizeSourceSection cfg.Source
            Inbox      = normalizeInboxSection cfg.Inbox
            Collection = normalizeCollectionSection cfg.Collection }

    let private normalizeLocal (cfg: LocalConfig) : LocalConfig =
        { cfg with
            Source     = normalizeSourceSection cfg.Source
            Inbox      = normalizeInboxSection cfg.Inbox
            Collection = normalizeCollectionSection cfg.Collection }

    let migrateGlobalV1 (v1: GlobalConfigV1) : GlobalConfigV2 =
        { Version        = 2
          DefaultSources = v1.DefaultSources |> List.map migrateSourceV1
          Collections    = v1.Collections
          DefaultInboxes = Map.empty
          Defaults       = v1.Defaults }

    let migrateLocalV1 (v1: LocalConfigV1) : LocalConfigV2 =
        { Version     = 2
          Sources     = v1.Sources |> List.map migrateSourceV1
          Collections = v1.Collections
          Inboxes     = Map.empty
          Settings    = v1.Settings }

    // A section with no populated field is dropped (None) so migrated output stays tidy.
    let private section (isEmpty: 'S -> bool) (s: 'S) : 'S option =
        if isEmpty s then None else Some s

    // List/map entities may be null when the old file never set them.
    let private listOrEmpty (xs: 'a list) : 'a list = if isNull (box xs) then [] else xs

    let private itemsSection<'a> (xs: 'a list) : 'a list option =
        match listOrEmpty xs with
        | [] -> None
        | l  -> Some l

    let private inboxItems (m: Map<string, InboxConfig>) : Map<string, InboxConfig> option =
        let m = normalizeInboxMap m
        if Map.isEmpty m then None else Some m

    let private sourceSection (xs: SourceConfig list) : SourceSection option =
        itemsSection (listOrEmpty xs |> List.map normalizeBundles) |> Option.map (fun items -> ({ Items = Some items } : SourceSection))

    let private collectionSection (xs: CollectionConfig list) : CollectionSection option =
        itemsSection xs |> Option.map (fun items -> ({ Items = Some items } : CollectionSection))

    let migrateGlobalV2 (v2: GlobalConfigV2) : GlobalConfig =
        let d = v2.Defaults
        let get (f: GlobalDefaultsV2 -> 'a option) = d |> Option.bind f
        { Version = 3
          Site =
            section (fun (s: SiteSection) -> s = SiteSection.empty)
                { SiteSection.empty with
                    IgnorePatterns   = get (fun d -> d.SiteIgnorePatterns)
                    HideEmptyBundles = get (fun d -> d.SiteHideEmptyBundles) }
          Okf =
            section (fun (s: OkfSection) -> s = OkfSection.empty)
                { IgnorePatterns = get (fun d -> d.OkfIgnorePatterns) }
          Mcp =
            section (fun (s: McpSection) -> s = McpSection.empty)
                { RefreshIntervalMinutes = get (fun d -> d.McpRefreshIntervalMinutes) }
          Sync =
            section (fun (s: SyncSection) -> s = SyncSection.empty)
                { Branch       = get (fun d -> d.Branch)
                  CommitOnPull = get (fun d -> d.CommitOnPull)
                  StateFile    = None }
          Files =
            section (fun (s: FilesSection) -> s = FilesSection.empty)
                { BlockPatterns = get (fun d -> d.BlockPatterns)
                  AllowPatterns = get (fun d -> d.AllowPatterns)
                  AllowBinaries = get (fun d -> d.AllowBinaries) }
          Inbox =
            section (fun (s: InboxSection) -> s = InboxSection.empty)
                { Default              = get (fun d -> d.DefaultInbox)
                  WatchIntervalSeconds = get (fun d -> d.InboxWatchIntervalSeconds)
                  Items                = inboxItems v2.DefaultInboxes }
          Source     = sourceSection v2.DefaultSources
          Collection = collectionSection v2.Collections }

    let migrateLocalV2 (v2: LocalConfigV2) : LocalConfig =
        let st = v2.Settings
        let get (f: LocalSettingsV2 -> 'a option) = st |> Option.bind f
        { Version = 3
          Site =
            section (fun (s: SiteSection) -> s = SiteSection.empty)
                { SiteSection.empty with
                    IgnorePatterns   = get (fun s -> s.SiteIgnorePatterns)
                    HideEmptyBundles = get (fun s -> s.SiteHideEmptyBundles) }
          Okf =
            section (fun (s: OkfSection) -> s = OkfSection.empty)
                { IgnorePatterns = get (fun s -> s.OkfIgnorePatterns) }
          Mcp = None
          Sync =
            section (fun (s: SyncSection) -> s = SyncSection.empty)
                { Branch       = None
                  CommitOnPull = get (fun s -> s.CommitOnPull)
                  StateFile    = get (fun s -> s.StateFile) }
          Files =
            section (fun (s: FilesSection) -> s = FilesSection.empty)
                { BlockPatterns = get (fun s -> s.BlockPatterns)
                  AllowPatterns = get (fun s -> s.AllowPatterns)
                  AllowBinaries = get (fun s -> s.AllowBinaries) }
          Inbox =
            section (fun (s: InboxSection) -> s = InboxSection.empty)
                { Default              = get (fun s -> s.DefaultInbox)
                  WatchIntervalSeconds = get (fun s -> s.InboxWatchIntervalSeconds)
                  Items                = inboxItems v2.Inboxes }
          Source     = sourceSection v2.Sources
          Collection = collectionSection v2.Collections }

    // What a config document says about its own shape: the "version" property (if a
    // number) and whether any source still uses the v1 `basePath` instead of `bundles`.
    // The latter is needed because System.Text.Json would happily read a v1 file as the
    // v2 shape (unknown `basePath` ignored, `bundles` left null) and drop the base path.
    let probe (json: string) : Result<int option * bool, string> =
        try
            use doc = System.Text.Json.JsonDocument.Parse json
            let root = doc.RootElement
            if root.ValueKind <> System.Text.Json.JsonValueKind.Object then Ok (None, false)
            else
                let version =
                    match root.TryGetProperty "version" with
                    | true, v when v.ValueKind = System.Text.Json.JsonValueKind.Number ->
                        match v.TryGetInt32() with
                        | true, i -> Some i
                        | _       -> None
                    | _ -> None
                let usesBasePath (sources: System.Text.Json.JsonElement) =
                    sources.ValueKind = System.Text.Json.JsonValueKind.Array
                    && sources.EnumerateArray()
                       |> Seq.exists (fun src ->
                            src.ValueKind = System.Text.Json.JsonValueKind.Object
                            && (match src.TryGetProperty "basePath" with
                                | true, _ -> (match src.TryGetProperty "bundles" with | true, _ -> false | _ -> true)
                                | _ -> false))
                let isV1 =
                    [ "sources"; "defaultSources" ]
                    |> List.exists (fun key ->
                        match root.TryGetProperty key with
                        | true, v -> usesBasePath v
                        | _ -> false)
                Ok (version, isV1)
        with ex -> Error ex.Message

    // Legacy (version <= 2) files: pick the V2 or V1 reader by shape, falling back to the
    // other on failure.
    let private readLegacy
        (deserializeV2: string -> Result<'v2, string>)
        (deserializeV1: string -> Result<'v1, string>)
        (fromV2: 'v2 -> 'cfg)
        (fromV1: 'v1 -> 'cfg)
        (isV1Shape: bool)
        (json: string) : Result<'cfg, string> =
        let asV2 () = deserializeV2 json |> Result.map fromV2
        let asV1 () = deserializeV1 json |> Result.map fromV1
        if isV1Shape then (match asV1 () with Ok c -> Ok c | Error _ -> asV2 ())
        else (match asV2 () with Ok c -> Ok c | Error _ -> asV1 ())

    // Reader flow: probe the document first. A v2 -> v3 file will NOT fail to deserialize
    // as v3 (System.Text.Json ignores unknown keys), so "try current, fall back on Error"
    // cannot detect it. Version >= 3 reads the current shape; version <= 2 reads V2, or V1
    // when a source still has `basePath`. Fallback in both directions is needed because
    // init/fallbacks used to stamp `Version = 1` on files that already had the v2 `bundles`
    // shape, so the number alone can't pick V1.
    let readAndMigrateGlobalJson
        (deserializeCurrent: string -> Result<GlobalConfig, string>)
        (deserializeV2: string -> Result<GlobalConfigV2, string>)
        (deserializeV1: string -> Result<GlobalConfigV1, string>)
        (json: string) : Result<GlobalConfig, string> =
        probe json
        |> Result.bind (fun (version, isV1Shape) ->
            match version with
            | Some v when v >= 3 -> deserializeCurrent json |> Result.map normalizeGlobal
            | _ ->
                readLegacy deserializeV2 deserializeV1
                    migrateGlobalV2 (migrateGlobalV1 >> migrateGlobalV2) isV1Shape json)

    let readAndMigrateLocalJson
        (deserializeCurrent: string -> Result<LocalConfig, string>)
        (deserializeV2: string -> Result<LocalConfigV2, string>)
        (deserializeV1: string -> Result<LocalConfigV1, string>)
        (json: string) : Result<LocalConfig, string> =
        probe json
        |> Result.bind (fun (version, isV1Shape) ->
            match version with
            | Some v when v >= 3 -> deserializeCurrent json |> Result.map normalizeLocal
            | _ ->
                readLegacy deserializeV2 deserializeV1
                    migrateLocalV2 (migrateLocalV1 >> migrateLocalV2) isV1Shape json)

module EffectiveConfig =
    // Built-in defaults only: what `Config.merge` yields with no config files at all.
    let empty : EffectiveConfig = {
        Sources                   = []
        CommitOnPull              = false
        StateFile                 = "eru.lock"
        Collections               = []
        McpRefreshIntervalMinutes = 60
        BlockPatterns             = Config.defaultBlockPatterns
        AllowPatterns             = Config.defaultAllowPatterns
        AllowBinaries             = Config.defaultAllowBinaries
        SiteIgnorePatterns        = Config.defaultSiteIgnorePatterns
        SiteHideEmptyBundles      = Config.defaultSiteHideEmptyBundles
        Site                      = SiteSettings.empty
        OkfIgnorePatterns         = Config.defaultOkfIgnorePatterns
        Inboxes                   = Map.empty
        DefaultInbox              = None
        InboxWatchIntervalSeconds = 30
    }
