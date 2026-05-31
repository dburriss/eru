module Eru.Tests.SearchTests

open Xunit
open Eru

// ── Helpers ───────────────────────────────────────────────────────────────────

let private makeSource name url : SourceConfig =
    { Name = name; Url = Some url; Branch = None; BasePath = None }

let private makeLocal sources : LocalConfig =
    { Version = 1; Sources = sources; Collections = []; Settings = None }

let private makeGlobal sources collections : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = collections; Defaults = None }

let private makeCollection name tags files : CollectionConfig =
    { Name = name; Tags = tags; Files = files; Description = None }

let private makeCollectionWithDesc name tags files desc : CollectionConfig =
    { Name = name; Tags = tags; Files = files; Description = Some desc }

let private makeFileRef source remotePath tags : CollectionFileRef =
    { Source = source; RemotePath = remotePath; Tags = tags; Description = None }

let private makeFileRefWithDesc source remotePath tags desc : CollectionFileRef =
    { Source = source; RemotePath = remotePath; Tags = tags; Description = Some desc }

let private makeLockEntry localPath sourceName remotePath : LockEntry =
    { LocalPath = localPath; SourceName = sourceName; RemotePath = remotePath; ContentHash = "sha256:abc" }

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (lockEntries: LockEntry list) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok lockEntries
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ path -> Ok [(path, $"content:{path}")]
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/tmp"
        ReadCachedManifest  = fun _ -> Ok None
        CacheSourceManifest = fun _ _ -> Ok ()
        ReadLocalManifest   = fun () -> Ok None
        WriteLocalManifest  = fun _ -> Ok ()
        ResolveLocalGlob    = fun _ -> []
    }

let private emptyQuery : Search.Query = { Terms = []; Tags = [] }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()

// ── No filter — returns everything ───────────────────────────────────────────

[<Fact>]
let ``no query returns all collection and lock results`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "logging" [] [ file ]
    let lock   = [ makeLockEntry "ops/deploy.sh" "kb" "ops/deploy.sh" ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) lock
    assertOk (Search.execute deps emptyQuery)

// ── Term filter ───────────────────────────────────────────────────────────────

[<Fact>]
let ``term matches remote path substring`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps { emptyQuery with Terms = ["logging"] })

[<Fact>]
let ``term is case insensitive`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps { emptyQuery with Terms = ["LOGGING"] })

[<Fact>]
let ``multiple terms use OR semantics`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let fileA  = makeFileRef "kb" "dotnet/Logging.fs" []
    let fileB  = makeFileRef "kb" "ops/Deploy.sh" []
    let col    = makeCollection "col" [] [ fileA; fileB ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps { emptyQuery with Terms = ["logging"; "deploy"] })

[<Fact>]
let ``term matches description text`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRefWithDesc "kb" "utils/helper.fs" [] "Shared logging utilities"
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps { emptyQuery with Terms = ["logging"] })

[<Fact>]
let ``term with no match returns empty list`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    match Search.execute deps { emptyQuery with Terms = ["xyz-no-match"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Empty(results)

// ── Tag filter ────────────────────────────────────────────────────────────────

[<Fact>]
let ``single tag matches collection file`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" ["dotnet"]
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps { emptyQuery with Tags = ["dotnet"] })

[<Fact>]
let ``collection-level tag applies to all files`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "col" ["dotnet"] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps { emptyQuery with Tags = ["dotnet"] })

[<Fact>]
let ``all tags must match AND semantics`` () =
    let source  = makeSource "kb" "https://example.com/kb.git"
    let fileA   = makeFileRef "kb" "a.fs" ["dotnet"]
    let fileB   = makeFileRef "kb" "b.fs" ["dotnet"; "observability"]
    let col     = makeCollection "col" [] [ fileA; fileB ]
    let deps    = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    match Search.execute deps { emptyQuery with Tags = ["dotnet"; "observability"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Single(results) |> ignore

[<Fact>]
let ``partial tag match excludes file`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "a.fs" ["dotnet"]
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    match Search.execute deps { emptyQuery with Tags = ["dotnet"; "observability"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Empty(results)

// ── Combined term + tag ───────────────────────────────────────────────────────

[<Fact>]
let ``combined term and tag both must match`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let fileA  = makeFileRef "kb" "dotnet/Logging.fs" ["dotnet"]
    let fileB  = makeFileRef "kb" "ops/Deploy.sh" ["ops"]
    let col    = makeCollection "col" [] [ fileA; fileB ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    match Search.execute deps { Terms = ["logging"]; Tags = ["ops"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Empty(results)

// ── Lock file integration ─────────────────────────────────────────────────────

[<Fact>]
let ``lock-only entry appears in results`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let lock   = [ makeLockEntry "local/script.sh" "kb" "ops/script.sh" ]
    let deps   = makeDeps (Some (makeGlobal [source] [])) (Some (makeLocal [source])) lock
    match Search.execute deps emptyQuery with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Single(results) |> ignore

[<Fact>]
let ``collection entry enriched with local path from lock`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "col" [] [ file ]
    let lock   = [ makeLockEntry "local/Logging.fs" "kb" "dotnet/Logging.fs" ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) lock
    match Search.execute deps emptyQuery with
    | Error e -> Assert.Fail(e)
    | Ok results ->
        Assert.Single(results) |> ignore
        Assert.Equal(Some "local/Logging.fs", results[0].LocalPath)

[<Fact>]
let ``file in both collection and lock appears once`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "shared/utils.fs" ["dotnet"]
    let col    = makeCollection "col" [] [ file ]
    let lock   = [ makeLockEntry "utils.fs" "kb" "shared/utils.fs" ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) lock
    match Search.execute deps { emptyQuery with Tags = ["dotnet"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Single(results) |> ignore

// ── Description inheritance ───────────────────────────────────────────────────

[<Fact>]
let ``file description takes precedence over collection description`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRefWithDesc "kb" "utils/helper.fs" [] "File-level description"
    let col    = makeCollectionWithDesc "col" [] [ file ] "Collection-level description"
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    match Search.execute deps { emptyQuery with Terms = ["file-level"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Single(results) |> ignore

[<Fact>]
let ``collection description used when file has none`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "utils/helper.fs" []
    let col    = makeCollectionWithDesc "col" [] [ file ] "Collection-level description"
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    match Search.execute deps { emptyQuery with Terms = ["collection-level"] } with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Single(results) |> ignore

// ── Absent config / lock ──────────────────────────────────────────────────────

[<Fact>]
let ``global config absent returns only lock results`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let lock   = [ makeLockEntry "local/script.sh" "kb" "ops/script.sh" ]
    let deps   = makeDeps None (Some (makeLocal [source])) lock
    assertOk (Search.execute deps emptyQuery)

[<Fact>]
let ``lock absent returns only collection results`` () =
    let source = makeSource "kb" "https://example.com/kb.git"
    let file   = makeFileRef "kb" "dotnet/Logging.fs" []
    let col    = makeCollection "col" [] [ file ]
    let deps   = makeDeps (Some (makeGlobal [source] [col])) (Some (makeLocal [source])) []
    assertOk (Search.execute deps emptyQuery)

[<Fact>]
let ``empty global config and empty lock returns empty results`` () =
    let deps = makeDeps (Some (makeGlobal [] [])) None []
    match Search.execute deps emptyQuery with
    | Error e -> Assert.Fail(e)
    | Ok results -> Assert.Empty(results)
