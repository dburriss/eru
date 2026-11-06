module Eru.Tests.EnvConfigTests

open Xunit
open Eru
open Eru.Adapters

let private unwrapOk label = function
    | Ok x -> x
    | Error e -> failwith $"{label}: expected Ok but got Error: {e}"

let private assertError (expected: string) = function
    | Ok _ -> failwith $"Expected Error containing \"{expected}\" but got Ok"
    | Error (e: string) -> Assert.Contains(expected, e)

let private layer (vars: (string * string) list) : EnvConfig.Layer =
    EnvConfig.read vars |> unwrapOk "read"

// The environment layer as the record `Config.mergeWithEnv` takes: same deserializer as a file.
let private envConfig (vars: (string * string) list) : LocalConfig option =
    (layer vars).Json
    |> Option.map (fun j -> Serialization.deserialize<LocalConfig> (j.ToJsonString()) |> unwrapOk "deserialize")

let private effective (env: (string * string) list) (g: GlobalConfig option) (l: LocalConfig option) : EffectiveConfig =
    Config.mergeWithEnv (envConfig env) g l |> unwrapOk "merge"

let private localWith (f: LocalConfig -> LocalConfig) : LocalConfig option = Some (f LocalConfig.empty)

// ── reading the environment ─────────────────────────────────────────────────

[<Fact>]
let ``no ERU__ variables leaves the layer empty`` () =
    let l = layer [ "PATH", "/usr/bin"; "HOME", "/home/me" ]
    Assert.True l.Json.IsNone
    Assert.Empty l.Warnings

[<Fact>]
let ``names mirror the config file shape and are case-insensitive`` () =
    let cfg = envConfig [ "ERU__SEARCH__MODE", "hybrid"; "eru__mcp__refreshIntervalMinutes", "5" ] |> Option.get
    Assert.Equal(Some "hybrid", cfg.Search |> Option.bind (fun s -> s.Mode))
    Assert.Equal(Some 5, cfg.Mcp |> Option.bind (fun m -> m.RefreshIntervalMinutes))

[<Fact>]
let ``values are typed by the setting`` () =
    let cfg =
        envConfig [ "ERU__FILES__ALLOWBINARIES", "true"
                    "ERU__INBOX__WATCHINTERVALSECONDS", "90"
                    "ERU__SYNC__STATEFILE", "123" ] |> Option.get
    Assert.Equal(Some true, cfg.Files |> Option.bind (fun f -> f.AllowBinaries))
    Assert.Equal(Some 90, cfg.Inbox |> Option.bind (fun i -> i.WatchIntervalSeconds))
    // a string setting stays a string even when it looks like a number
    Assert.Equal(Some "123", cfg.Sync |> Option.bind (fun s -> s.StateFile))

[<Fact>]
let ``lists are indexed and keep their order`` () =
    let cfg =
        envConfig [ "ERU__FILES__BLOCKPATTERNS__1", "*.dll"
                    "ERU__FILES__BLOCKPATTERNS__0", "*.exe"
                    "ERU__FILES__BLOCKPATTERNS__2", "*.so" ] |> Option.get
    Assert.Equal<string list>([ "*.exe"; "*.dll"; "*.so" ], cfg.Files |> Option.bind (fun f -> f.BlockPatterns) |> Option.get)

[<Fact>]
let ``objects inside lists use indexed names`` () =
    let cfg =
        envConfig [ "ERU__OKF__FIXTYPES__0__PATTERN", "README.md"
                    "ERU__OKF__FIXTYPES__0__TYPE", "concept"
                    "ERU__OKF__FIXTYPES__1__PATTERN", "docs/**"
                    "ERU__OKF__FIXTYPES__1__TYPE", "null" ] |> Option.get
    let rules = cfg.Okf |> Option.bind (fun o -> o.FixTypes) |> Option.get
    Assert.Equal<OkfFixTypeRule list>([ { Pattern = "README.md"; Type = Some "concept" }; { Pattern = "docs/**"; Type = None } ], rules)

[<Fact>]
let ``a list given as one value is rejected with the indexed name`` () =
    EnvConfig.read [ "ERU__FILES__BLOCKPATTERNS", "*.exe" ] |> assertError "ERU__FILES__BLOCKPATTERNS__0"

[<Fact>]
let ``gaps in list indexes are rejected`` () =
    EnvConfig.read [ "ERU__FILES__BLOCKPATTERNS__0", "a"; "ERU__FILES__BLOCKPATTERNS__2", "c" ] |> assertError "without gaps"

[<Fact>]
let ``names that are not settings warn and are ignored`` () =
    let l =
        layer [ "ERU__SEARCH__MODEE", "lex"
                "ERU__NOPE__X", "1"
                "ERU__SOURCE__ITEMS__0__NAME", "a"
                "ERU__INBOX__ITEMS__x__PATH", "/tmp"
                "ERU__SEARCH__MODE", "sem" ]
    Assert.Equal(4, l.Warnings.Length)
    Assert.Contains(l.Warnings, fun w -> w.Contains "ERU__SEARCH__MODEE")
    Assert.Contains(l.Warnings, fun w -> w.Contains "ERU__SOURCE__ITEMS__0__NAME" && w.Contains "can override only")
    // the one real setting still applies
    let cfg = envConfig [ "ERU__SEARCH__MODEE", "lex"; "ERU__SEARCH__MODE", "sem" ] |> Option.get
    Assert.Equal(Some "sem", cfg.Search |> Option.bind (fun s -> s.Mode))
    Assert.True cfg.Source.IsNone

[<Fact>]
let ``blank values count as unset`` () =
    Assert.True (layer [ "ERU__SEARCH__MODE", ""; "ERU__MCP__REFRESHINTERVALMINUTES", "  " ]).Json.IsNone

[<Fact>]
let ``ERU_SEARCH_MODE is an alias that ERU__SEARCH__MODE overrides`` () =
    let mode vars = envConfig vars |> Option.get |> fun c -> c.Search |> Option.bind (fun s -> s.Mode)
    Assert.Equal(Some "sem", mode [ "ERU_SEARCH_MODE", "sem" ])
    Assert.Equal(Some "hybrid", mode [ "ERU_SEARCH_MODE", "sem"; "ERU__SEARCH__MODE", "hybrid" ])

[<Fact>]
let ``a value of the wrong type is an error naming the variable`` () =
    EnvConfig.read [ "ERU__MCP__REFRESHINTERVALMINUTES", "soon" ] |> assertError "ERU__MCP__REFRESHINTERVALMINUTES must be a whole number"
    EnvConfig.read [ "ERU__FILES__ALLOWBINARIES", "yes" ] |> assertError "ERU__FILES__ALLOWBINARIES must be true or false"
    EnvConfig.read [ "ERU__SITE__CARDS__COLUMNS", "many" ] |> assertError "ERU__SITE__CARDS__COLUMNS must be a whole number"

// ── precedence ──────────────────────────────────────────────────────────────

let private globalMcp minutes : GlobalConfig option =
    Some { GlobalConfig.empty with Mcp = Some { RefreshIntervalMinutes = Some minutes } }

let private localMcp minutes : LocalConfig option =
    localWith (fun l -> { l with Mcp = Some { RefreshIntervalMinutes = Some minutes } })

[<Fact>]
let ``env beats local beats global beats the default`` () =
    let env = [ "ERU__MCP__REFRESHINTERVALMINUTES", "30" ]
    Assert.Equal(30, (effective env (globalMcp 10) (localMcp 20)).McpRefreshIntervalMinutes)
    Assert.Equal(20, (effective [] (globalMcp 10) (localMcp 20)).McpRefreshIntervalMinutes)
    Assert.Equal(10, (effective [] (globalMcp 10) None).McpRefreshIntervalMinutes)
    Assert.Equal(60, (effective [] None None).McpRefreshIntervalMinutes)
    Assert.Equal(30, (effective env None None).McpRefreshIntervalMinutes)

[<Fact>]
let ``keys the env does not set fall through to the files`` () =
    let l = localWith (fun l -> { l with Sync = Some { SyncSection.empty with StateFile = Some "mine.lock" } })
    let eff = effective [ "ERU__SYNC__COMMITONPULL", "true" ] None l
    Assert.True eff.CommitOnPull
    Assert.Equal("mine.lock", eff.StateFile)

[<Fact>]
let ``files settings can be overridden like any other`` () =
    let l = localWith (fun l -> { l with Files = Some { BlockPatterns = Some [ "*.exe" ]; AllowPatterns = None; AllowBinaries = Some false } })
    let eff =
        effective [ "ERU__FILES__ALLOWBINARIES", "true"
                    "ERU__FILES__BLOCKPATTERNS__0", "*.bin"
                    "ERU__FILES__ALLOWPATTERNS__0", "vendor/**" ] None l
    Assert.True eff.AllowBinaries
    // a list set in the env replaces the file's list rather than extending it
    Assert.Equal<string list>([ "*.bin" ], eff.BlockPatterns)
    Assert.Equal<string list>([ "vendor/**" ], eff.AllowPatterns)

[<Fact>]
let ``every settings section can be overridden`` () =
    let eff =
        effective [ "ERU__SEARCH__MODE", "hybrid"
                    "ERU__SEARCH__IGNOREPATTERNS__0", "CHANGELOG.md"
                    "ERU__OKF__IGNOREPATTERNS__0", "vendor/**"
                    "ERU__SITE__IGNOREPATTERNS__0", "TODO.md"
                    "ERU__SITE__HIDEEMPTYBUNDLES", "true"
                    "ERU__INBOX__DEFAULT", "team"
                    "ERU__INBOX__WATCHINTERVALSECONDS", "5" ] None None
    Assert.Equal(Hybrid, eff.SearchMode)
    Assert.Equal<string list>([ "CHANGELOG.md" ], eff.SearchIgnorePatterns)
    Assert.Equal<string list>([ "vendor/**" ], eff.OkfIgnorePatterns)
    Assert.Equal<string list>([ "TODO.md" ], eff.SiteIgnorePatterns)
    Assert.True eff.SiteHideEmptyBundles
    Assert.Equal(Some "team", eff.DefaultInbox)
    Assert.Equal(5, eff.InboxWatchIntervalSeconds)

[<Fact>]
let ``okf settings resolve through the env layer`` () =
    let env =
        envConfig [ "ERU__OKF__DEFAULTTYPE", "note"
                    "ERU__OKF__FIXTYPES__0__PATTERN", "README.md"
                    "ERU__OKF__FIXTYPES__0__TYPE", "concept" ]
    Assert.Equal("note", Config.resolveOkfDefaultTypeWith env None None)
    Assert.Equal(1, (Config.resolveOkfFixTypesWith env None None).Length)
    // without the env layer nothing changes
    Assert.Equal(Config.defaultOkfType, Config.resolveOkfDefaultType None None)

[<Fact>]
let ``site settings from the env merge over the files`` () =
    let l =
        localWith (fun l ->
            { l with Site = Some { SiteSection.empty with Home = Some "tags"; DefaultView = Some "list" } })
    let eff =
        effective [ "ERU__SITE__HOME", "types"
                    "ERU__SITE__CARDS__COLUMNS", "2"
                    "ERU__SITE__LAYOUT__NAV__0", "tags"
                    "ERU__SITE__LAYOUT__NAV__1", "types" ] None l
    Assert.Equal(Some "types", eff.Site.Home)
    Assert.Equal(Some "list", eff.Site.DefaultView)
    Assert.Equal(Some 2, eff.Site.CardsColumns)
    Assert.Equal(Some (Items [ PageRef ("tags", None); PageRef ("types", None) ]), eff.Site.Layout.Nav)

[<Fact>]
let ``env overrides leave sources and inboxes to the files`` () =
    let src : SourceConfig = { Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None }
    let g = Some { GlobalConfig.empty with Source = Some { Items = Some [ src ] } }
    let eff = effective [ "ERU__SEARCH__MODE", "lex" ] g None
    Assert.Equal<string list>([ "kb" ], eff.Sources |> List.map (fun s -> s.Name))

// ── bad values ──────────────────────────────────────────────────────────────

[<Fact>]
let ``an invalid search mode fails the merge and validateEnv`` () =
    let env = envConfig [ "ERU__SEARCH__MODE", "nope" ]
    Config.mergeWithEnv env None None |> assertError "search.mode must be one of lex, sem, hybrid"
    Config.validateEnv env |> assertError "search.mode"

[<Fact>]
let ``an invalid site value fails validateEnv naming the environment`` () =
    let env = envConfig [ "ERU__SITE__LAYOUT__NAV__0", "no-such-page"; "ERU__SITE__DEFAULTVIEW", "cards"; "ERU__SITE__LAYOUT__BOTTOM__0", "x" ]
    Config.validateEnv env |> assertError "ERU__SITE__"

[<Fact>]
let ``validateEnv accepts an empty or valid layer`` () =
    Assert.True (Config.validateEnv None |> Result.isOk)
    Assert.True (Config.validateEnv (envConfig [ "ERU__SEARCH__MODE", "sem" ]) |> Result.isOk)


// ── per-source overrides ────────────────────────────────────────────────────

let private kbSource name : SourceConfig =
    { Name = name; Url = Some $"https://example.com/{name}.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None }

let private globalWithSources (names: string list) : GlobalConfig option =
    Some (GlobalConfig.withSources (names |> List.map kbSource) GlobalConfig.empty)

[<Fact>]
let ``a source's settings can be set from the environment`` () =
    let eff =
        effective [ "ERU__SOURCE__KB__FILES__ALLOWBINARIES", "true"
                    "ERU__SOURCE__KB__SEARCH__IGNOREPATTERNS__0", "CHANGELOG.md"
                    "ERU__SOURCE__KB__SEARCH__IGNOREPATTERNS__1", "drafts/**" ] (globalWithSources [ "kb"; "other" ]) None
    let kb = Config.effectiveFor eff (eff.Sources |> List.find (fun s -> s.Name = "kb"))
    let other = Config.effectiveFor eff (eff.Sources |> List.find (fun s -> s.Name = "other"))
    Assert.True kb.AllowBinaries
    Assert.Equal<string list>([ "CHANGELOG.md"; "drafts/**" ], kb.SearchIgnorePatterns)
    Assert.False other.AllowBinaries
    Assert.Equal<string list>(Config.defaultSearchIgnorePatterns, other.SearchIgnorePatterns)

[<Fact>]
let ``env overrides for a source beat the config files`` () =
    let g = Some (GlobalConfig.withSources [ { kbSource "kb" with Files = Some { FilesSection.empty with AllowBinaries = Some false } } ] GlobalConfig.empty)
    let eff = effective [ "ERU__SOURCE__KB__FILES__ALLOWBINARIES", "true" ] g None
    Assert.True (Config.effectiveFor eff eff.Sources.Head).AllowBinaries

[<Fact>]
let ``the source name in an env var matches case-insensitively with - and . as underscores`` () =
    let eff = effective [ "ERU__SOURCE__MY_DOCS__FILES__ALLOWBINARIES", "true" ] (globalWithSources [ "my-docs" ]) None
    Assert.True (Config.effectiveFor eff eff.Sources.Head).AllowBinaries
    let eff2 = effective [ "ERU__SOURCE__V1_2__FILES__ALLOWBINARIES", "true" ] (globalWithSources [ "v1.2" ]) None
    Assert.True (Config.effectiveFor eff2 eff2.Sources.Head).AllowBinaries

[<Fact>]
let ``an env var for an unknown source is ignored and never creates a source`` () =
    let eff = effective [ "ERU__SOURCE__GHOST__FILES__ALLOWBINARIES", "true" ] (globalWithSources [ "kb" ]) None
    Assert.Equal<string list>([ "kb" ], eff.Sources |> List.map (fun s -> s.Name))
    Assert.False (Config.effectiveFor eff eff.Sources.Head).AllowBinaries

[<Fact>]
let ``an env var that matches two sources is ambiguous`` () =
    let env = envConfig [ "ERU__SOURCE__DOCS__FILES__ALLOWBINARIES", "true" ]
    Config.mergeWithEnv env (globalWithSources [ "Docs"; "docs" ]) None |> assertError "ambiguous"

[<Fact>]
let ``only the per-source settings are read from the environment`` () =
    let l = layer [ "ERU__SOURCE__KB__URL", "https://x"; "ERU__SOURCE__KB__SEARCH__MODE", "sem"; "ERU__SOURCE__KB__OKF__DEFAULTTYPE", "guide" ]
    Assert.True l.Json.IsNone
    Assert.Equal(3, l.Warnings.Length)

[<Fact>]
let ``per-source env values are typed like the settings they name`` () =
    let cfg = envConfig [ "ERU__SOURCE__KB__FILES__BLOCKPATTERNS__0", "123"; "ERU__SOURCE__KB__FILES__ALLOWBINARIES", "true" ] |> Option.get
    let kb = (LocalConfig.sources cfg).Head
    Assert.Equal(Some [ "123" ], kb.Files |> Option.bind (fun f -> f.BlockPatterns))
    Assert.Equal(Some true, kb.Files |> Option.bind (fun f -> f.AllowBinaries))

[<Fact>]
let ``a bad per-source env value names the variable`` () =
    EnvConfig.read [ "ERU__SOURCE__KB__FILES__ALLOWBINARIES", "maybe" ] |> assertError "ERU__SOURCE__KB__FILES__ALLOWBINARIES must be true or false"
