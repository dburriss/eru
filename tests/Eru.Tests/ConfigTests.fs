module Eru.Tests.ConfigTests

open Xunit
open Eru

let private unwrapOk label = function
    | Ok x -> x
    | Error e -> failwith $"{label}: expected Ok but got Error: {e}"

let private assertError (expected: string) = function
    | Ok _ -> failwith $"Expected Error \"{expected}\" but got Ok"
    | Error e -> Assert.Contains(expected, e)

let private makeGlobal (sources: SourceConfig list) (collections: CollectionConfig list) : GlobalConfig =
    { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some (sources) }; Collection = Some { CollectionSection.empty with Items = Some (collections) } }

let private makeInbox path : InboxConfig =
    { Path = path; RawPath = None; DefaultChannel = None; Channels = Map.empty; Branch = None }

let private makeSource name url : SourceConfig =
    { Name = name; Url = url; Branch = None; Bundles = [] }

// ── merge: basic cases ──────────────────────────────────────────────────────

[<Fact>]
let ``merge returns empty sources when both configs absent`` () =
    let result = Config.merge None None |> unwrapOk "both absent"
    Assert.Empty result.Sources

[<Fact>]
let ``merge uses global sources when no local config`` () =
    let g = makeGlobal [ makeSource "main" (Some "https://example.com/repo.git") ] []
    let result = Config.merge (Some g) None |> unwrapOk "global only"
    Assert.Equal(1, result.Sources.Length)
    Assert.Equal("main", result.Sources[0].Name)

[<Fact>]
let ``merge prefers local CommitOnPull over global default`` () =
    let g = { makeGlobal [] [] with Sync = Some { SyncSection.empty with CommitOnPull = Some false } }
    let l : LocalConfig = { LocalConfig.empty with Sync = Some { SyncSection.empty with CommitOnPull = Some true } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "commitOnPull"
    Assert.True result.CommitOnPull

[<Fact>]
let ``merge uses custom StateFile from local settings`` () =
    let l : LocalConfig = { LocalConfig.empty with Sync = Some { SyncSection.empty with StateFile = Some "custom.lock" } }
    let result = Config.merge None (Some l) |> unwrapOk "stateFile"
    Assert.Equal("custom.lock", result.StateFile)

[<Fact>]
let ``merge defaults McpRefreshIntervalMinutes to 60`` () =
    let result = Config.merge None None |> unwrapOk "mcp refresh default"
    Assert.Equal(60, result.McpRefreshIntervalMinutes)

[<Fact>]
let ``merge uses McpRefreshIntervalMinutes from global defaults`` () =
    let g = { makeGlobal [] [] with Mcp = Some { McpSection.empty with RefreshIntervalMinutes = Some 30 } }
    let result = Config.merge (Some g) None |> unwrapOk "mcp refresh custom"
    Assert.Equal(30, result.McpRefreshIntervalMinutes)

// ── merge: InboxWatchIntervalSeconds ────────────────────────────────────────

[<Fact>]
let ``merge defaults InboxWatchIntervalSeconds to 30`` () =
    let result = Config.merge None None |> unwrapOk "inbox watch interval default"
    Assert.Equal(30, result.InboxWatchIntervalSeconds)

[<Fact>]
let ``merge uses InboxWatchIntervalSeconds from global defaults`` () =
    let g = { makeGlobal [] [] with Inbox = Some { InboxSection.empty with WatchIntervalSeconds = Some 10 } }
    let result = Config.merge (Some g) None |> unwrapOk "inbox watch interval global"
    Assert.Equal(10, result.InboxWatchIntervalSeconds)

[<Fact>]
let ``merge prefers local InboxWatchIntervalSeconds over global default`` () =
    let g = { makeGlobal [] [] with Inbox = Some { InboxSection.empty with WatchIntervalSeconds = Some 10 } }
    let l : LocalConfig = { LocalConfig.empty with Inbox = Some { InboxSection.empty with WatchIntervalSeconds = Some 5 } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "inbox watch interval local override"
    Assert.Equal(5, result.InboxWatchIntervalSeconds)

// ── merge: SiteIgnorePatterns ────────────────────────────────────────────────

[<Fact>]
let ``merge defaults SiteIgnorePatterns to index.md, log.md and README.md`` () =
    let result = Config.merge None None |> unwrapOk "site ignore default"
    Assert.Equal<string list>([ "index.md"; "log.md"; "README.md" ], result.SiteIgnorePatterns)

[<Fact>]
let ``merge uses SiteIgnorePatterns from global defaults`` () =
    let g = { makeGlobal [] [] with Site = Some { SiteSection.empty with IgnorePatterns = Some [ "draft-*.md" ] } }
    let result = Config.merge (Some g) None |> unwrapOk "site ignore global"
    Assert.Equal<string list>([ "draft-*.md" ], result.SiteIgnorePatterns)

[<Fact>]
let ``merge prefers local SiteIgnorePatterns over global default`` () =
    let g = { makeGlobal [] [] with Site = Some { SiteSection.empty with IgnorePatterns = Some [ "index.md"; "log.md" ] } }
    let l : LocalConfig = { LocalConfig.empty with Site = Some { SiteSection.empty with IgnorePatterns = Some [] } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "site ignore local override"
    Assert.Empty result.SiteIgnorePatterns

[<Fact>]
let ``merge defaults SiteHideEmptyBundles to false`` () =
    let result = Config.merge (Some (makeGlobal [] [])) None |> unwrapOk "hide empty default"
    Assert.False result.SiteHideEmptyBundles

[<Fact>]
let ``merge prefers local SiteHideEmptyBundles over global default`` () =
    let g = { makeGlobal [] [] with Site = Some { SiteSection.empty with HideEmptyBundles = Some true } }
    let globalOnly = Config.merge (Some g) None |> unwrapOk "hide empty global"
    Assert.True globalOnly.SiteHideEmptyBundles
    let l : LocalConfig = { LocalConfig.empty with Site = Some { SiteSection.empty with HideEmptyBundles = Some false } }
    let overridden = Config.merge (Some g) (Some l) |> unwrapOk "hide empty local override"
    Assert.False overridden.SiteHideEmptyBundles

// ── merge: source ordering ───────────────────────────────────────────────────

[<Fact>]
let ``merge preserves local source declaration order`` () =
    let g = makeGlobal [ makeSource "b" (Some "https://b.com"); makeSource "a" (Some "https://a.com") ] []
    let l : LocalConfig = { LocalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ makeSource "b" None; makeSource "a" (Some "https://a-override.com") ]) } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "ordering"
    Assert.Equal(2, result.Sources.Length)
    Assert.Equal("b", result.Sources[0].Name)
    Assert.Equal("a", result.Sources[1].Name)

[<Fact>]
let ``merge appends global-only sources after local sources`` () =
    let g = makeGlobal [ makeSource "local-one" (Some "https://l.com"); makeSource "global-only" (Some "https://g.com") ] []
    let l : LocalConfig = { LocalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ makeSource "local-one" None ]) } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "global-only appended"
    Assert.Equal(2, result.Sources.Length)
    Assert.Equal("local-one", result.Sources[0].Name)
    Assert.Equal("global-only", result.Sources[1].Name)

// ── merge: error cases ───────────────────────────────────────────────────────

[<Fact>]
let ``merge errors when inherited local source not found in global config`` () =
    let g = makeGlobal [ makeSource "other" (Some "https://other.com") ] []
    let l : LocalConfig = { LocalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ makeSource "missing" None ]) } }
    Config.merge (Some g) (Some l)
    |> assertError "missing"

[<Fact>]
let ``merge errors when global config version too high`` () =
    let g = { makeGlobal [] [] with Version = 99 }
    Config.merge (Some g) None
    |> assertError "please upgrade eru"

[<Fact>]
let ``merge errors when local config version too high`` () =
    let l : LocalConfig = { LocalConfig.empty with Version = 99 }
    Config.merge None (Some l)
    |> assertError "please upgrade eru"

[<Fact>]
let ``merge errors on duplicate source name in global config`` () =
    let g = makeGlobal [ makeSource "dup" (Some "https://a.com"); makeSource "dup" (Some "https://b.com") ] []
    Config.merge (Some g) None
    |> assertError "Duplicate source name 'dup'"

[<Fact>]
let ``merge errors on duplicate source name in local config`` () =
    let l : LocalConfig = { LocalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ makeSource "dup" None; makeSource "dup" None ]) } }
    Config.merge None (Some l)
    |> assertError "Duplicate source name 'dup'"

[<Fact>]
let ``merge errors when global source has no URL`` () =
    let g = makeGlobal [ makeSource "no-url" None ] []
    Config.merge (Some g) None
    |> assertError "no URL"

[<Fact>]
let ``merge errors when collection references unknown source`` () =
    let file = { Source = "unknown"; RemotePath = "foo.md"; Tags = []; Description = None }
    let col  = { Name = "my-col"; Tags = []; Files = [ file ]; Description = None }
    let g    = makeGlobal [ makeSource "known" (Some "https://known.com") ] [ col ]
    Config.merge (Some g) None
    |> assertError "unknown source 'unknown'"

// ── resolveByTags ────────────────────────────────────────────────────────────

let private makeCollection name tags files =
    { Name = name; Tags = tags; Files = files; Description = None }

let private makeFileRef source path tags =
    { Source = source; RemotePath = path; Tags = tags; Description = None }

[<Fact>]
let ``resolveByTags returns files from matching collection`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [ makeCollection "col" ["backend"] files ]
    let result = Config.resolveByTags ["backend"] g
    Assert.Equal(1, result.Length)
    Assert.Equal({ Source = "src"; RemotePath = "foo.md" }, result[0])

[<Fact>]
let ``resolveByTags AND semantics - all tags must match`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [
        makeCollection "partial" ["backend"] files
        makeCollection "full"    ["backend"; "dotnet"] files
    ]
    let result = Config.resolveByTags ["backend"; "dotnet"] g
    Assert.Equal(1, result.Length)

[<Fact>]
let ``resolveByTags is case insensitive`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [ makeCollection "col" ["Backend"] files ]
    let result = Config.resolveByTags ["backend"] g
    Assert.Equal(1, result.Length)

[<Fact>]
let ``resolveByTags matches file-level tags even when collection does not`` () =
    let files = [
        makeFileRef "src" "matches.md" ["dotnet"]
        makeFileRef "src" "no-match.md" []
    ]
    let g = makeGlobal [] [ makeCollection "col" ["other"] files ]
    let result = Config.resolveByTags ["dotnet"] g
    Assert.Equal(1, result.Length)
    Assert.Equal("matches.md", result[0].RemotePath)

[<Fact>]
let ``resolveByTags returns empty when no collections match`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [ makeCollection "col" ["frontend"] files ]
    let result = Config.resolveByTags ["backend"] g
    Assert.Empty result

[<Fact>]
let ``resolveByTags deduplicates files appearing in multiple collections`` () =
    let file = makeFileRef "src" "shared.md" ["common"]
    let g = makeGlobal [] [
        makeCollection "col1" ["common"] [ file ]
        makeCollection "col2" ["common"] [ file ]
    ]
    let result = Config.resolveByTags ["common"] g
    Assert.Equal(1, result.Length)

// ── LockFile ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``LockFile parse roundtrips entries`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "main"; RemotePath = "templates/adr.md"; ContentHash = "sha256:abc123"; Tags = []; Description = None }
        { LocalPath = "src/Utils.fs"; SourceName = "platform"; RemotePath = "dotnet/Utils.fs"; ContentHash = "sha256:def456"; Tags = []; Description = None }
    ]
    let written = LockFile.write entries
    match LockFile.parse written with
    | Error e   -> Assert.Fail e
    | Ok parsed ->
        Assert.Equal(2, parsed.Length)
        Assert.Equal("docs/adr.md",      parsed[0].LocalPath)
        Assert.Equal("main",             parsed[0].SourceName)
        Assert.Equal("templates/adr.md", parsed[0].RemotePath)
        Assert.Equal("src/Utils.fs",     parsed[1].LocalPath)

[<Fact>]
let ``LockFile write sorts entries by LocalPath`` () =
    let entries = [
        { LocalPath = "z-last.md";  SourceName = "s"; RemotePath = "z.md"; ContentHash = "sha256:aaa"; Tags = []; Description = None }
        { LocalPath = "a-first.md"; SourceName = "s"; RemotePath = "a.md"; ContentHash = "sha256:bbb"; Tags = []; Description = None }
    ]
    let written = LockFile.write entries
    match LockFile.parse written with
    | Error e   -> Assert.Fail e
    | Ok parsed ->
        Assert.Equal("a-first.md", parsed[0].LocalPath)
        Assert.Equal("z-last.md",  parsed[1].LocalPath)

[<Fact>]
let ``LockFile findByLocalPath returns entry when found`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "src"; RemotePath = "adr.md"; ContentHash = "sha256:aaa"; Tags = []; Description = None }
        { LocalPath = "src/Utils.fs"; SourceName = "src"; RemotePath = "Utils.fs"; ContentHash = "sha256:bbb"; Tags = []; Description = None }
    ]
    match LockFile.findByLocalPath "docs/adr.md" entries with
    | None   -> Assert.Fail "Expected Some but got None"
    | Some e -> Assert.Equal("sha256:aaa", e.ContentHash)

[<Fact>]
let ``LockFile findByLocalPath returns None when not found`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "src"; RemotePath = "adr.md"; ContentHash = "sha256:aaa"; Tags = []; Description = None }
    ]
    Assert.Equal(None, LockFile.findByLocalPath "does-not-exist.md" entries)

// ── merge: Collections ───────────────────────────────────────────────────────

[<Fact>]
let ``merge populates Collections from GlobalConfig`` () =
    let file = makeFileRef "src" "docs/guide.md" ["docs"]
    let col  = { Name = "guides"; Tags = []; Files = [ file ]; Description = None }
    let g    = makeGlobal [ makeSource "src" (Some "https://src.com") ] [ col ]
    let result = Config.merge (Some g) None |> unwrapOk "collections"
    Assert.Equal(1, result.Collections.Length)
    Assert.Equal("src",          result.Collections[0].Source)
    Assert.Equal("docs/guide.md", result.Collections[0].RemotePath)

[<Fact>]
let ``merge returns empty Collections when no global config`` () =
    let result = Config.merge None None |> unwrapOk "no global"
    Assert.Empty result.Collections

// ── withManifests ────────────────────────────────────────────────────────────

[<Fact>]
let ``withManifests appends manifest files not in user config`` () =
    let src = makeSource "kb" (Some "https://kb.com")
    let eff = Config.merge (Some (makeGlobal [ src ] [])) None |> unwrapOk "base"
    let manifest = { Version = 1; Description = None; Files = [ { Path = "guide.md"; Tags = ["docs"]; Description = None } ] }
    let result = Config.withManifests (fun _ -> Ok (Some manifest)) eff
    Assert.Equal(1, result.Collections.Length)
    Assert.Equal("kb",       result.Collections[0].Source)
    Assert.Equal("guide.md", result.Collections[0].RemotePath)
    Assert.Equal<string list>(["docs"],   result.Collections[0].Tags)

[<Fact>]
let ``withManifests user-explicit entry wins on collision`` () =
    let src      = makeSource "kb" (Some "https://kb.com")
    let explicit = { Source = "kb"; RemotePath = "guide.md"; Tags = ["explicit"]; Description = Some "user-defined" }
    let col      = { Name = "c"; Tags = []; Files = [ explicit ]; Description = None }
    let g        = makeGlobal [ src ] [ col ]
    let eff      = Config.merge (Some g) None |> unwrapOk "base"
    let manifest = { Version = 1; Description = None; Files = [ { Path = "guide.md"; Tags = ["manifest"]; Description = None } ] }
    let result   = Config.withManifests (fun _ -> Ok (Some manifest)) eff
    // Should still have exactly one entry (no duplicate), and it should be the user-explicit one
    Assert.Equal(1, result.Collections.Length)
    Assert.Equal<string list>(["explicit"], result.Collections[0].Tags)

[<Fact>]
let ``withManifests silently ignores missing manifest`` () =
    let src = makeSource "kb" (Some "https://kb.com")
    let eff = Config.merge (Some (makeGlobal [ src ] [])) None |> unwrapOk "base"
    let result = Config.withManifests (fun _ -> Ok None) eff
    Assert.Empty result.Collections

[<Fact>]
let ``withManifests silently ignores manifest read error`` () =
    let src = makeSource "kb" (Some "https://kb.com")
    let eff = Config.merge (Some (makeGlobal [ src ] [])) None |> unwrapOk "base"
    let result = Config.withManifests (fun _ -> Error "disk error") eff
    Assert.Empty result.Collections

// ── config migration (v1 -> v2 -> v3) ────────────────────────────────────────

let private v1Source name url basePath : SourceConfigV1 =
    { Name = name; Url = url; Branch = None; BasePath = basePath }

let private noV2<'a> : string -> Result<'a, string> = fun _ -> Error "should not be used"

let private migrateGlobalFromV1 (v1: GlobalConfigV1) =
    Config.readAndMigrateGlobalJson noV2 (fun _ -> Error "not v2") (fun _ -> Ok v1) """{ "version": 1 }"""
    |> unwrapOk "v1 -> v3"

[<Fact>]
let ``readAndMigrateGlobalJson passes a version 3 config through unchanged`` () =
    let cfg : GlobalConfig = makeGlobal [ makeSource "kb" (Some "https://kb.com") ] []
    let result =
        Config.readAndMigrateGlobalJson (fun _ -> Ok cfg) noV2 noV2 """{ "version": 3 }"""
        |> unwrapOk "current shape"
    Assert.Equal(3, result.Version)
    Assert.Equal(1, (GlobalConfig.sources result).Length)
    Assert.Empty (GlobalConfig.sources result).[0].Bundles

[<Fact>]
let ``readAndMigrateGlobalJson maps BasePath to a single Manifest bundle`` () =
    let v1 : GlobalConfigV1 =
        { Version = 1; DefaultSources = [ v1Source "kb" (Some "https://kb.com") (Some "KNOWLEDGE") ]; Collections = []; Defaults = None }
    let result = migrateGlobalFromV1 v1
    Assert.Equal(3, result.Version)
    Assert.Equal<Bundle list>([ { Path = "KNOWLEDGE"; Kind = Manifest } ], (GlobalConfig.sources result).[0].Bundles)

[<Fact>]
let ``readAndMigrateGlobalJson maps a None BasePath to zero bundles`` () =
    let v1 : GlobalConfigV1 =
        { Version = 1; DefaultSources = [ v1Source "kb" (Some "https://kb.com") None ]; Collections = []; Defaults = None }
    Assert.Empty (GlobalConfig.sources (migrateGlobalFromV1 v1)).[0].Bundles

[<Fact>]
let ``readAndMigrateLocalJson maps BasePath to a single Manifest bundle`` () =
    let v1 : LocalConfigV1 =
        { Version = 1; Sources = [ v1Source "kb" (Some "https://kb.com") (Some "docs") ]; Collections = []; Settings = None }
    let result =
        Config.readAndMigrateLocalJson noV2 (fun _ -> Error "not v2") (fun _ -> Ok v1) """{ "version": 1 }"""
        |> unwrapOk "v1 -> v3"
    Assert.Equal(3, result.Version)
    Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Manifest } ], (LocalConfig.sources result).[0].Bundles)

let private v2Defaults : GlobalDefaultsV2 =
    { Branch = Some "dev"; CommitOnPull = Some true; McpRefreshIntervalMinutes = Some 15
      BlockPatterns = Some [ "*.x" ]; AllowPatterns = Some [ "a.x" ]; AllowBinaries = Some true
      SiteIgnorePatterns = Some [ "s.md" ]; SiteHideEmptyBundles = Some true
      OkfIgnorePatterns = Some [ "o/**" ]; DefaultInbox = Some "kb"; InboxWatchIntervalSeconds = Some 7 }

let private v2Settings : LocalSettingsV2 =
    { CommitOnPull = Some true; StateFile = Some "x.lock"
      BlockPatterns = Some [ "*.x" ]; AllowPatterns = Some [ "a.x" ]; AllowBinaries = Some true
      SiteIgnorePatterns = Some [ "s.md" ]; SiteHideEmptyBundles = Some true
      OkfIgnorePatterns = Some [ "o/**" ]; DefaultInbox = Some "kb"; InboxWatchIntervalSeconds = Some 7 }

let private migrateGlobalV2Json (v2: GlobalConfigV2) =
    Config.readAndMigrateGlobalJson noV2 (fun _ -> Ok v2) noV2 """{ "version": 2 }"""
    |> unwrapOk "v2 -> v3"

let private migrateLocalV2Json (v2: LocalConfigV2) =
    Config.readAndMigrateLocalJson noV2 (fun _ -> Ok v2) noV2 """{ "version": 2 }"""
    |> unwrapOk "v2 -> v3"

[<Fact>]
let ``v2 global defaults map key by key into v3 sections`` () =
    let col = { Name = "c"; Tags = []; Files = []; Description = None }
    let v2 : GlobalConfigV2 =
        { Version = 2
          DefaultSources = [ makeSource "kb" (Some "https://kb.com") ]
          Collections = [ col ]
          DefaultInboxes = Map.ofList [ "kb", makeInbox "/kb" ]
          Defaults = Some v2Defaults }
    let r = migrateGlobalV2Json v2
    Assert.Equal(3, r.Version)
    Assert.Equal(Some [ "s.md" ], r.Site.Value.IgnorePatterns)
    Assert.Equal(Some true, r.Site.Value.HideEmptyBundles)
    Assert.Equal(Some [ "o/**" ], r.Okf.Value.IgnorePatterns)
    Assert.Equal(Some 15, r.Mcp.Value.RefreshIntervalMinutes)
    Assert.Equal(Some "dev", r.Sync.Value.Branch)
    Assert.Equal(Some true, r.Sync.Value.CommitOnPull)
    Assert.Equal(None, r.Sync.Value.StateFile)
    Assert.Equal(Some [ "*.x" ], r.Files.Value.BlockPatterns)
    Assert.Equal(Some [ "a.x" ], r.Files.Value.AllowPatterns)
    Assert.Equal(Some true, r.Files.Value.AllowBinaries)
    Assert.Equal(Some "kb", r.Inbox.Value.Default)
    Assert.Equal(Some 7, r.Inbox.Value.WatchIntervalSeconds)
    Assert.Equal("/kb", (GlobalConfig.inboxes r).["kb"].Path)
    Assert.Equal("kb", (GlobalConfig.sources r).[0].Name)
    Assert.Equal("c", (GlobalConfig.collections r).[0].Name)

[<Fact>]
let ``v2 local settings map key by key into v3 sections`` () =
    let v2 : LocalConfigV2 =
        { Version = 2
          Sources = [ makeSource "kb" (Some "https://kb.com") ]
          Collections = []
          Inboxes = Map.ofList [ "kb", makeInbox "/kb" ]
          Settings = Some v2Settings }
    let r = migrateLocalV2Json v2
    Assert.Equal(3, r.Version)
    Assert.Equal(Some [ "s.md" ], r.Site.Value.IgnorePatterns)
    Assert.Equal(Some true, r.Site.Value.HideEmptyBundles)
    Assert.Equal(Some [ "o/**" ], r.Okf.Value.IgnorePatterns)
    Assert.True(r.Mcp.IsNone)
    Assert.Equal(Some true, r.Sync.Value.CommitOnPull)
    Assert.Equal(Some "x.lock", r.Sync.Value.StateFile)
    Assert.Equal(Some true, r.Files.Value.AllowBinaries)
    Assert.Equal(Some "kb", r.Inbox.Value.Default)
    Assert.Equal(Some 7, r.Inbox.Value.WatchIntervalSeconds)
    Assert.Equal(1, (LocalConfig.inboxes r).Count)
    Assert.Equal(1, (LocalConfig.sources r).Length)

[<Fact>]
let ``v2 migration leaves sections without populated fields as None`` () =
    let v2 : LocalConfigV2 =
        { Version = 2; Sources = []; Collections = []; Inboxes = Map.empty
          Settings = Some { v2Settings with
                                CommitOnPull = None; StateFile = None; BlockPatterns = None; AllowPatterns = None
                                AllowBinaries = None; SiteIgnorePatterns = None; SiteHideEmptyBundles = None
                                OkfIgnorePatterns = None; DefaultInbox = Some "kb"; InboxWatchIntervalSeconds = None } }
    let r = migrateLocalV2Json v2
    Assert.True(r.Site.IsNone)
    Assert.True(r.Okf.IsNone)
    Assert.True(r.Sync.IsNone)
    Assert.True(r.Files.IsNone)
    Assert.True(r.Source.IsNone)
    Assert.True(r.Collection.IsNone)
    Assert.Equal(Some "kb", r.Inbox.Value.Default)

[<Fact>]
let ``v2 migration of an empty config yields no sections`` () =
    let v2 : GlobalConfigV2 =
        { Version = 2; DefaultSources = []; Collections = []; DefaultInboxes = Map.empty; Defaults = None }
    Assert.Equal<GlobalConfig>(GlobalConfig.empty, migrateGlobalV2Json v2)

[<Fact>]
let ``v2 migration tolerates null entity lists`` () =
    let v2 : LocalConfigV2 =
        { Version = 2; Sources = Unchecked.defaultof<_>; Collections = Unchecked.defaultof<_>
          Inboxes = Unchecked.defaultof<_>; Settings = None }
    Assert.Equal<LocalConfig>(LocalConfig.empty, migrateLocalV2Json v2)

[<Fact>]
let ``a version-1-stamped file with the v2 shape is migrated via the V2 reader`` () =
    let v2 : LocalConfigV2 =
        { Version = 1; Sources = [ makeSource "kb" (Some "https://kb.com") ]; Collections = []
          Inboxes = Map.empty; Settings = None }
    let r =
        Config.readAndMigrateLocalJson noV2 (fun _ -> Ok v2) noV2 """{ "version": 1 }"""
        |> unwrapOk "v1-stamped v2 shape"
    Assert.Equal(3, r.Version)
    Assert.Equal(1, (LocalConfig.sources r).Length)

[<Fact>]
let ``a version 1 file in the V1 shape falls through V2 to the V1 chain`` () =
    let v1 : LocalConfigV1 =
        { Version = 1; Sources = [ v1Source "kb" (Some "https://kb.com") (Some "docs") ]; Collections = []; Settings = None }
    let r =
        Config.readAndMigrateLocalJson noV2 (fun _ -> Error "not v2") (fun _ -> Ok v1) """{ "version": 1 }"""
        |> unwrapOk "v1 chain"
    Assert.Equal(3, r.Version)
    Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Manifest } ], (LocalConfig.sources r).[0].Bundles)

// ── real JSON through Serialization (the adapter's deserializers) ───────────

open Eru.Adapters

let private readGlobal json =
    Config.readAndMigrateGlobalJson
        Serialization.deserialize<GlobalConfig>
        Serialization.deserialize<GlobalConfigV2>
        Serialization.deserialize<GlobalConfigV1>
        json

let private readLocal json =
    Config.readAndMigrateLocalJson
        Serialization.deserialize<LocalConfig>
        Serialization.deserialize<LocalConfigV2>
        Serialization.deserialize<LocalConfigV1>
        json

[<Fact>]
let ``real v2 local JSON is migrated to v3 sections`` () =
    let json = """{
      "version": 2,
      "sources": [ { "name": "kb", "url": "https://kb.com", "bundles": [ { "path": "docs", "kind": "okf" } ] } ],
      "collections": [],
      "inboxes": { "kb": { "path": "/kb", "channels": {} } },
      "settings": { "commitOnPull": true, "siteIgnorePatterns": ["x.md"], "defaultInbox": "kb", "inboxWatchIntervalSeconds": 9 }
    }"""
    let r = readLocal json |> unwrapOk "real v2"
    Assert.Equal(3, r.Version)
    Assert.Equal(Some true, r.Sync.Value.CommitOnPull)
    Assert.Equal(Some [ "x.md" ], r.Site.Value.IgnorePatterns)
    Assert.Equal(Some "kb", r.Inbox.Value.Default)
    Assert.Equal(Some 9, r.Inbox.Value.WatchIntervalSeconds)
    Assert.Equal("/kb", (LocalConfig.inboxes r).["kb"].Path)
    Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Okf } ], (LocalConfig.sources r).[0].Bundles)

[<Fact>]
let ``real v2 global JSON is migrated to v3 sections`` () =
    let json = """{
      "version": 2,
      "defaultSources": [ { "name": "kb", "url": "https://kb.com", "bundles": [] } ],
      "collections": [],
      "defaultInboxes": {},
      "defaults": { "branch": "dev", "mcpRefreshIntervalMinutes": 15, "allowBinaries": true }
    }"""
    let r = readGlobal json |> unwrapOk "real v2 global"
    Assert.Equal(3, r.Version)
    Assert.Equal(Some "dev", r.Sync.Value.Branch)
    Assert.Equal(Some 15, r.Mcp.Value.RefreshIntervalMinutes)
    Assert.Equal(Some true, r.Files.Value.AllowBinaries)
    Assert.Equal("kb", (GlobalConfig.sources r).[0].Name)

[<Fact>]
let ``real v1 JSON chains through v2 to v3`` () =
    let json = """{
      "version": 1,
      "defaultSources": [ { "name": "kb", "url": "https://kb.com", "basePath": "KNOWLEDGE" } ],
      "collections": [],
      "defaults": { "commitOnPull": true }
    }"""
    let r = readGlobal json |> unwrapOk "real v1"
    Assert.Equal(3, r.Version)
    Assert.Equal(Some true, r.Sync.Value.CommitOnPull)
    Assert.Equal<Bundle list>([ { Path = "KNOWLEDGE"; Kind = Manifest } ], (GlobalConfig.sources r).[0].Bundles)

[<Fact>]
let ``real version-1-stamped JSON with the v2 bundles shape keeps its bundles`` () =
    let json = """{
      "version": 1,
      "sources": [ { "name": "kb", "url": "https://kb.com", "bundles": [ { "path": "k", "kind": "manifest" } ] } ],
      "collections": []
    }"""
    let r = readLocal json |> unwrapOk "stamped v1, v2 shape"
    Assert.Equal<Bundle list>([ { Path = "k"; Kind = Manifest } ], (LocalConfig.sources r).[0].Bundles)

[<Fact>]
let ``version 3 config round-trips through Serialization`` () =
    let cfg : LocalConfig =
        { LocalConfig.empty with
            Site   = Some { SiteSection.empty with IgnorePatterns = Some [ "a.md" ] }
            Sync   = Some { SyncSection.empty with StateFile = Some "s.lock" }
            Inbox  = Some { InboxSection.empty with Default = Some "kb"; Items = Some (Map.ofList [ "kb", makeInbox "/kb" ]) }
            Source = Some { Items = Some [ makeSource "kb" (Some "https://kb.com") ] } }
    let json = Serialization.serialize cfg
    let back = readLocal json |> unwrapOk "round trip"
    Assert.Equal<LocalConfig>(cfg, back)
    Assert.Contains("\"site\"", json)
    Assert.DoesNotContain("\"settings\"", json)

[<Fact>]
let ``real version 3 JSON with absent sections reads as no sections`` () =
    let r = readLocal """{ "version": 3, "source": { "items": [ { "name": "kb", "url": "https://kb.com" } ] } }""" |> unwrapOk "sparse v3"
    Assert.Equal(3, r.Version)
    Assert.True(r.Site.IsNone)
    Assert.Empty (LocalConfig.collections r)
    Assert.Empty (LocalConfig.sources r).[0].Bundles

[<Fact>]
let ``real version 4 JSON is rejected by merge`` () =
    let r = readLocal """{ "version": 4 }""" |> unwrapOk "v4 reads"
    Config.merge None (Some r) |> assertError "please upgrade eru"

[<Fact>]
let ``checkVersion (via merge) rejects a v4 global config`` () =
    let g = { makeGlobal [] [] with Version = 4 }
    Config.merge (Some g) None
    |> assertError "please upgrade eru"

[<Fact>]
let ``checkVersion (via merge) accepts a v3 global config`` () =
    let g = { makeGlobal [] [] with Version = 3 }
    Config.merge (Some g) None
    |> unwrapOk "v3 accepted"
    |> ignore

[<Fact>]
let ``merge resolves every section key local-over-global`` () =
    let g : GlobalConfig =
        { GlobalConfig.empty with
            Site  = Some { SiteSection.empty with IgnorePatterns = Some [ "g.md" ]; HideEmptyBundles = Some false }
            Okf   = Some { IgnorePatterns = Some [ "g/**" ] }
            Mcp   = Some { RefreshIntervalMinutes = Some 10 }
            Sync  = Some { Branch = Some "main"; CommitOnPull = Some false; StateFile = Some "g.lock" }
            Files = Some { BlockPatterns = Some [ "*.g" ]; AllowPatterns = Some [ "g" ]; AllowBinaries = Some false }
            Inbox = Some { Default = Some "g"; WatchIntervalSeconds = Some 10; Items = None } }
    let l : LocalConfig =
        { LocalConfig.empty with
            Site  = Some { SiteSection.empty with HideEmptyBundles = Some true }
            Mcp   = Some { RefreshIntervalMinutes = Some 5 }
            Sync  = Some { Branch = None; CommitOnPull = Some true; StateFile = None }
            Inbox = Some { Default = None; WatchIntervalSeconds = Some 3; Items = None } }
    let r = Config.merge (Some g) (Some l) |> unwrapOk "per-key merge"
    // local wins where set
    Assert.True r.SiteHideEmptyBundles
    Assert.Equal(5, r.McpRefreshIntervalMinutes)
    Assert.True r.CommitOnPull
    Assert.Equal(3, r.InboxWatchIntervalSeconds)
    // global fills the keys local leaves unset
    Assert.Equal<string list>([ "g.md" ], r.SiteIgnorePatterns)
    Assert.Equal<string list>([ "g/**" ], r.OkfIgnorePatterns)
    Assert.Equal("g.lock", r.StateFile)
    Assert.Equal<string list>([ "*.g" ], r.BlockPatterns)
    Assert.Equal(Some "g", r.DefaultInbox)

// ── merge: Inboxes ───────────────────────────────────────────────────────────

[<Fact>]
let ``merge returns empty Inboxes when both configs absent`` () =
    let result = Config.merge None None |> unwrapOk "no inboxes"
    Assert.Empty result.Inboxes

[<Fact>]
let ``merge local inbox of a given name wins over global inbox of the same name`` () =
    let g = (GlobalConfig.withInboxes (Map.ofList [ "kb", makeInbox "/global/path" ]) (makeGlobal [] []))
    let l : LocalConfig = { LocalConfig.empty with Inbox = Some { InboxSection.empty with Items = Some (Map.ofList [ "kb", makeInbox "/local/path" ]) } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "local wins"
    Assert.Equal(1, result.Inboxes.Count)
    Assert.Equal("/local/path", result.Inboxes["kb"].Path)

[<Fact>]
let ``merge appends global-only inboxes not present locally`` () =
    let g = (GlobalConfig.withInboxes (Map.ofList [ "global-only", makeInbox "/g" ]) (makeGlobal [] []))
    let l : LocalConfig = { LocalConfig.empty with Inbox = Some { InboxSection.empty with Items = Some (Map.ofList [ "local-one", makeInbox "/l" ]) } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "global appended"
    Assert.Equal(2, result.Inboxes.Count)
    Assert.True(result.Inboxes.ContainsKey "local-one")
    Assert.True(result.Inboxes.ContainsKey "global-only")

[<Fact>]
let ``merge resolves DefaultInbox from local settings over global default`` () =
    let g = { makeGlobal [] [] with Inbox = Some { InboxSection.empty with Default = Some "global-default" } }
    let l : LocalConfig = { LocalConfig.empty with Inbox = Some { InboxSection.empty with Default = Some "local-default" } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "default inbox"
    Assert.Equal(Some "local-default", result.DefaultInbox)

[<Fact>]
let ``merge falls back to global DefaultInbox when local settings do not set one`` () =
    let g = { makeGlobal [] [] with Inbox = Some { InboxSection.empty with Default = Some "global-default" } }
    let result = Config.merge (Some g) None |> unwrapOk "global fallback"
    Assert.Equal(Some "global-default", result.DefaultInbox)

[<Fact>]
let ``readAndMigrateLocalJson normalizes a null Inboxes map to empty`` () =
    // Simulates an old on-disk config that predates the Inboxes field: System.Text.Json
    // leaves it null rather than {} when absent from the JSON.
    let cfgWithNullInboxes : LocalConfig = { LocalConfig.empty with Inbox = Some { InboxSection.empty with Items = Some (Unchecked.defaultof<_>) } }
    let result =
        Config.readAndMigrateLocalJson (fun _ -> Ok cfgWithNullInboxes) noV2 noV2 """{ "version": 3 }"""
        |> unwrapOk "null inboxes normalized"
    Assert.Empty (LocalConfig.inboxes result)

// ── merge: OkfIgnorePatterns ─────────────────────────────────────────────────

[<Fact>]
let ``merge defaults OkfIgnorePatterns`` () =
    let result = Config.merge None None |> unwrapOk "okf ignore default"
    Assert.Equal<string list>([ "apm_modules/**"; "inbox/**"; "node_modules/**" ], result.OkfIgnorePatterns)

[<Fact>]
let ``merge uses OkfIgnorePatterns from global defaults`` () =
    let g = { makeGlobal [] [] with Okf = Some { OkfSection.empty with IgnorePatterns = Some [ "drafts/**" ] } }
    let result = Config.merge (Some g) None |> unwrapOk "okf ignore global"
    Assert.Equal<string list>([ "drafts/**" ], result.OkfIgnorePatterns)

[<Fact>]
let ``merge prefers local OkfIgnorePatterns over global default`` () =
    let g = { makeGlobal [] [] with Okf = Some { OkfSection.empty with IgnorePatterns = Some [ "drafts/**" ] } }
    let l : LocalConfig = { LocalConfig.empty with Okf = Some { OkfSection.empty with IgnorePatterns = Some [] } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "okf ignore local override"
    Assert.Empty result.OkfIgnorePatterns
