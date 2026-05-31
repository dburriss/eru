namespace Eru

type Deps = {
    ReadGlobalConfig   : unit   -> Result<GlobalConfig option, string>
    ReadLocalConfig    : unit   -> Result<LocalConfig option, string>
    WriteLocalConfig   : LocalConfig  -> Result<unit, string>
    WriteGlobalConfig  : GlobalConfig -> Result<unit, string>
    ReadLockEntries    : string -> Result<LockEntry list, string>
    WriteLockEntries   : string -> LockEntry list -> Result<unit, string>
    FetchRemoteContent : string -> string -> string -> Result<(string * string) list, string>
    ListRemoteTopLevel : string -> string option -> Result<string list, string>
    ListRemoteFiles    : string -> string option -> string option -> Result<string list, string>
    WriteLocalFile     : string -> string -> Result<unit, string>
    DeleteLocalFile    : string -> Result<unit, string>
    HashContent        : string -> string
    GetCwd             : unit   -> string
    ReadCachedManifest  : string -> Result<SourceManifest option, string>
    CacheSourceManifest : string -> string -> Result<unit, string>
    ReadLocalManifest   : unit   -> Result<SourceManifest option, string>
    WriteLocalManifest  : SourceManifest -> Result<unit, string>
    ResolveLocalGlob    : string -> string list
}
