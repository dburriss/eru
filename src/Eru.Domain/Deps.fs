namespace Eru

type LinkKind =
    | MarkdownLink
    | Wikilink

type ExtractedLink = {
    Target      : string
    Description : string option
    Kind        : LinkKind
}

// Per-phase duration (ms) of one RunAgent call's ACP handshake — surfaced via
// `eru inbox process --debug` to show where time actually goes (mostly the fixed
// cost of spawning a fresh agent process + session per item, not the prompt itself).
type AgentTimings = {
    InitializeMs : float
    SessionNewMs : float
    PromptMs     : float
}

type AgentRunResult = {
    Response : string
    Timings  : AgentTimings
}

type Deps = {
    ReadGlobalConfig         : unit   -> Result<GlobalConfig option, string>
    ReadLocalConfig          : unit   -> Result<LocalConfig option, string>
    WriteLocalConfig         : LocalConfig  -> Result<unit, string>
    WriteGlobalConfig        : GlobalConfig -> Result<unit, string>
    ReadLockEntries          : string -> Result<LockEntry list, string>
    WriteLockEntries         : string -> LockEntry list -> Result<unit, string>
    FetchRemoteContent       : string -> string -> string list -> Result<(string * string) list, string>
    ListRemoteTopLevel       : string -> string option -> Result<string list, string>
    ListRemoteFiles          : string -> string option -> string option -> Result<string list, string>
    WriteLocalFile           : string -> string -> Result<unit, string>
    ReadLocalFile            : string -> Result<string option, string>
    DeleteLocalFile          : string -> Result<unit, string>
    HashContent              : string -> string
    GetCwd                   : unit   -> string
    ReadCachedManifest       : string -> Result<SourceManifest option, string>
    CacheSourceManifest      : string -> string -> Result<unit, string>
    ReadLocalManifest        : unit   -> Result<SourceManifest option, string>
    WriteLocalManifest       : SourceManifest -> Result<unit, string>
    ResolveLocalGlob         : string -> string list
    ReadSourceIndex          : string -> Result<SourceIndex option, string>
    WriteSourceIndex         : string -> SourceIndex -> Result<unit, string>
    CacheSourceContent       : string -> string -> string -> Result<string, string>
    ReadCachedSourceContent  : string -> string -> Result<string option, string>
    BuildSearchIndex         : string -> string -> unit
    ParseYamlBlock           : Yaml.Parse
    ListMarkdownFiles        : string -> Result<string list, string>
    ExtractLinks             : string -> ExtractedLink list
    GetRemoteHeadSha         : string -> string option -> Result<string, string>
    DirectoryExists          : string -> bool
    GetUtcNow                : unit   -> System.DateTimeOffset
    ListLocalFiles           : string -> Result<string list, string>   // non-recursive; full paths, files only
    ListLocalDirectories     : string -> Result<string list, string>   // non-recursive; full paths, directories only; Ok [] if the directory doesn't exist
    MoveLocalFile            : string -> string -> Result<unit, string> // src -> dst; creates dst's parent dir
    // remoteUrl -> branch (None = the repo's default branch) -> commit message -> (repo-relative path, content) files.
    // Shallow-clones the repo, writes the files, commits and pushes; returns the branch pushed to.
    PushToRemote             : string -> string option -> string -> (string * string) list -> Result<string, string>
    RunAgent                 : AgentConfig -> string -> string -> (string -> unit) -> Result<AgentRunResult, string> // agent -> workingDir -> prompt -> onChunk (called with each streamed text fragment as the agent responds)
}
