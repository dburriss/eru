module Eru.Tests.SourceTests

open Xunit
open Eru

let private emptyLocal : LocalConfig = LocalConfig.empty
let private emptyGlobal : GlobalConfig = GlobalConfig.empty

let private makeDeps
    (localCfg: LocalConfig option)
    (globalCfg: GlobalConfig option)
    (topLevel: string list)
    (capturedLocal: LocalConfig option ref)
    (capturedGlobal: GlobalConfig option ref) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        ReadEnvConfig    = fun () -> Ok None
        WriteLocalConfig   = fun cfg -> capturedLocal.Value <- Some cfg; Ok ()
        WriteGlobalConfig  = fun cfg -> capturedGlobal.Value <- Some cfg; Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok topLevel
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
        ReadLocalFile       = fun _ -> Ok None
        DeleteLocalFile     = fun _ -> Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/tmp"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok None
        WriteLocalManifest      = fun _ -> Ok ()
        ResolveLocalGlob        = fun _ -> []
        ReadSourceIndex         = fun _ -> Ok None
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fun _ -> []
        GetRemoteHeadSha        = fun _ _ -> Error "not implemented"
        IsPlainFolder           = fun _ -> false
        ScanLocalFolder         = fun _ _ _ -> Ok []
        DirectoryExists        = fun _ -> true
        GetUtcNow        = fun () -> System.DateTimeOffset.UtcNow
        ListLocalFiles   = fun _ -> Ok []
        ListLocalDirectories = fun _ -> Ok []
        MoveLocalFile    = fun _ _ -> Ok ()
        PushToRemote         = fun _ _ _ _ -> Ok "main"
        RunAgent         = fun _ _ _ _ -> Ok { Response = ""; Timings = { InitializeMs = 0.0; SessionNewMs = 0.0; PromptMs = 0.0 } }
    }

let private simpleCmd url : SourceAdd.Command = {
    Url      = url
    Name     = None
    Branch   = None
    BasePath = None
    Scan     = false
    Nested   = false
    Exclude  = []
    IsGlobal = false
    DryRun   = false
}

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

// ── Name derivation ──────────────────────────────────────────────────────────

[<Fact>]
let ``derives name from HTTPS URL stripping .git`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    assertOk (SourceAdd.execute deps (simpleCmd "https://github.com/acme/knowledge-base.git"))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("knowledge-base", (LocalConfig.sources cfg).[0].Name)

[<Fact>]
let ``derives name from SSH URL stripping .git`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    assertOk (SourceAdd.execute deps (simpleCmd "git@github.com:acme/knowledge-base.git"))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("knowledge-base", (LocalConfig.sources cfg).[0].Name)

[<Fact>]
let ``name override is respected`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "my-kb" }
    SourceAdd.execute deps cmd |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("my-kb", (LocalConfig.sources cfg).[0].Name)

// ── Local config writes ──────────────────────────────────────────────────────

[<Fact>]
let ``writes to local config when .eru/config.json present`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    assertOk (SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git"))
    Assert.True(written.Value.IsSome)

[<Fact>]
let ``errors when .eru/config.json absent in local mode`` () =
    let deps = makeDeps None None [] (ref None) (ref None)
    assertError (SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git"))

// ── Global config writes ─────────────────────────────────────────────────────

[<Fact>]
let ``writes to global config when --global flag set`` () =
    let writtenGlobal = ref None
    let deps = makeDeps None (Some emptyGlobal) [] (ref None) writtenGlobal
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with IsGlobal = true }
    assertOk (SourceAdd.execute deps cmd)
    Assert.True(writtenGlobal.Value.IsSome)

[<Fact>]
let ``creates empty global config when none exists`` () =
    let writtenGlobal = ref None
    let deps = makeDeps None None [] (ref None) writtenGlobal
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with IsGlobal = true }
    assertOk (SourceAdd.execute deps cmd)
    match writtenGlobal.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(1, (GlobalConfig.sources cfg).Length)

// ── KNOWLEDGE detection ──────────────────────────────────────────────────────

[<Fact>]
let ``detects KNOWLEDGE basePath from top-level listing`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["README.md"; "KNOWLEDGE"; "src"] written (ref None)
    SourceAdd.execute deps ({ simpleCmd "https://github.com/acme/kb.git" with Scan = true }) |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "KNOWLEDGE"; Kind = Manifest } ], (LocalConfig.sources cfg).[0].Bundles)

[<Fact>]
let ``detects lowercase knowledge basePath`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["README.md"; "knowledge"] written (ref None)
    SourceAdd.execute deps ({ simpleCmd "https://github.com/acme/kb.git" with Scan = true }) |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "knowledge"; Kind = Manifest } ], (LocalConfig.sources cfg).[0].Bundles)

[<Fact>]
let ``no basePath when top-level listing returns empty`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Empty((LocalConfig.sources cfg).[0].Bundles)

[<Fact>]
let ``registers no bundles without --scan even when knowledge folder exists`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["knowledge"] written (ref None)
    SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Empty((LocalConfig.sources cfg).[0].Bundles)

let private okfDeps (files: (string * string) list) written =
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    { deps with
        ListRemoteFiles    = fun _ _ _ -> Ok (files |> List.map fst)
        FetchRemoteContent = fun _ _ paths -> Ok (files |> List.filter (fun (p, _) -> List.contains p paths))
        ParseYamlBlock     = fun y -> if y.Contains "okf_version" then Ok (Yaml.Map [ "okf_version", Yaml.Scalar "0.2" ]) else Ok Yaml.Null }

let private okfIndex = "---\nokf_version: \"0.2\"\n---\n"

[<Fact>]
let ``--scan registers a root okf bundle and skips nested ones`` () =
    let written = ref None
    let deps = okfDeps [ "index.md", okfIndex; "a/index.md", okfIndex; "a/n.md", "x" ] written
    SourceAdd.execute deps ({ simpleCmd "https://github.com/acme/kb.git" with Scan = true }) |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = ""; Kind = Okf } ], (LocalConfig.sources cfg).[0].Bundles)

[<Fact>]
let ``--scan --nested keeps the root bundle and every nested okf bundle`` () =
    let written = ref None
    let deps = okfDeps [ "index.md", okfIndex; "a/index.md", okfIndex; "a/b/index.md", okfIndex; "c/index.md", "# x" ] written
    SourceAdd.execute deps ({ simpleCmd "https://github.com/acme/kb.git" with Scan = true; Nested = true }) |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg ->
        Assert.Equal<Bundle list>(
            [ { Path = ""; Kind = Okf }; { Path = "a"; Kind = Okf }; { Path = "a/b"; Kind = Okf } ],
            (LocalConfig.sources cfg).[0].Bundles)

[<Fact>]
let ``--nested without --scan is rejected`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    assertError (SourceAdd.execute deps ({ simpleCmd "https://github.com/acme/kb.git" with Nested = true }))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``--scan registers each nested okf bundle when there is no root marker`` () =
    let written = ref None
    let deps = okfDeps [ "index.md", "# no fm"; "kb/alpha/index.md", okfIndex; "kb/beta/index.md", okfIndex; "kb/plain/index.md", "# x" ] written
    SourceAdd.execute deps ({ simpleCmd "https://github.com/acme/kb.git" with Scan = true }) |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg ->
        Assert.Equal<Bundle list>([ { Path = "kb/alpha"; Kind = Okf }; { Path = "kb/beta"; Kind = Okf } ], (LocalConfig.sources cfg).[0].Bundles)

[<Fact>]
let ``explicit --basepath overrides auto-detection and skips remote listing`` () =
    let listingCalled = ref false
    let written = ref None
    let deps =
        { makeDeps (Some emptyLocal) None [] written (ref None) with
            ListRemoteTopLevel = fun _ _ -> listingCalled.Value <- true; Ok ["KNOWLEDGE"] }
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with BasePath = Some "docs" }
    SourceAdd.execute deps cmd |> ignore
    Assert.False(listingCalled.Value, "remote listing should not be called when --basepath is explicit")
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Manifest } ], (LocalConfig.sources cfg).[0].Bundles)

// ── Duplicate name ───────────────────────────────────────────────────────────

[<Fact>]
let ``errors on duplicate source name in local config`` () =
    let existing = (LocalConfig.withSources ([{ Name = "kb"; Url = Some "https://x.com"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyLocal)
    let deps = makeDeps (Some existing) None [] (ref None) (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "kb" }
    assertError (SourceAdd.execute deps cmd)

[<Fact>]
let ``errors on duplicate source name in global config`` () =
    let existing = (GlobalConfig.withSources ([{ Name = "kb"; Url = Some "https://x.com"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyGlobal)
    let deps = makeDeps None (Some existing) [] (ref None) (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "kb"; IsGlobal = true }
    assertError (SourceAdd.execute deps cmd)

// ── SourceList ────────────────────────────────────────────────────────────────

[<Fact>]
let ``list returns empty list when both configs have no sources`` () =
    let deps = makeDeps (Some emptyLocal) (Some emptyGlobal) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows -> Assert.Empty(rows)

[<Fact>]
let ``list returns error on global config read error`` () =
    let deps = { makeDeps None None [] (ref None) (ref None) with ReadGlobalConfig = fun () -> Error "boom" }
    assertError (SourceList.execute deps)

[<Fact>]
let ``list returns error on local config read error`` () =
    let deps = { makeDeps None None [] (ref None) (ref None) with ReadLocalConfig = fun () -> Error "boom" }
    assertError (SourceList.execute deps)

[<Fact>]
let ``list shows local source with local scope`` () =
    let local = (LocalConfig.withSources ([{ Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyLocal)
    let deps  = makeDeps (Some local) (Some emptyGlobal) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Equal(Some "https://example.com/kb.git", row.Url)
        Assert.Equal("local", row.Scope)

[<Fact>]
let ``list shows local alias source with alias scope`` () =
    let localCfg  = (LocalConfig.withSources ([{ Name = "kb"; Url = None; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyLocal)
    let globalCfg = (GlobalConfig.withSources ([{ Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyGlobal)
    let deps      = makeDeps (Some localCfg) (Some globalCfg) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Equal("local → global alias", row.Scope)

[<Fact>]
let ``list shows global-only source with global scope`` () =
    let globalCfg = (GlobalConfig.withSources ([{ Name = "shared"; Url = Some "https://example.com/shared.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyGlobal)
    let deps      = makeDeps (Some emptyLocal) (Some globalCfg) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "shared")
        Assert.Equal(Some "https://example.com/shared.git", row.Url)
        Assert.Equal("global", row.Scope)

[<Fact>]
let ``list shows local sources before global-only sources`` () =
    let localCfg  = (LocalConfig.withSources ([{ Name = "local-src";  Url = Some "https://example.com/local.git";  Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyLocal)
    let globalCfg = (GlobalConfig.withSources ([{ Name = "global-src"; Url = Some "https://example.com/global.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyGlobal)
    let deps      = makeDeps (Some localCfg) (Some globalCfg) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let localIdx  = rows |> List.findIndex (fun r -> r.Name = "local-src")
        let globalIdx = rows |> List.findIndex (fun r -> r.Name = "global-src")
        Assert.True(localIdx < globalIdx, "local source should appear before global-only source")

[<Fact>]
let ``list includes branch and basepath when set`` () =
    let src    = { Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = Some "main"; Bundles = [ { Path = "KNOWLEDGE"; Kind = Manifest } ]; Files = None; Search = None; Okf = None; Site = None; Enabled = None }
    let local  = (LocalConfig.withSources ([src]) emptyLocal)
    let deps   = makeDeps (Some local) (Some emptyGlobal) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Equal(Some "main", row.Branch)
        Assert.Equal<Bundle list>([ { Path = "KNOWLEDGE"; Kind = Manifest } ], row.Bundles)

[<Fact>]
let ``list shows tags from cached manifest`` () =
    let local    = (LocalConfig.withSources ([{ Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }]) emptyLocal)
    let f1       = { Path = "a.md"; Tags = ["dotnet"; "adr"];          Description = None }
    let f2       = { Path = "b.md"; Tags = ["dotnet"; "architecture"]; Description = None }
    let manifest = { Version = 1; Description = None; Files = [f1; f2] }
    let deps     = { makeDeps (Some local) (Some emptyGlobal) [] (ref None) (ref None) with
                       ReadCachedManifest = fun _ -> Ok (Some manifest) }
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Contains("adr", row.Tags)
        Assert.Contains("architecture", row.Tags)
        Assert.Equal(1, row.Tags |> List.filter (fun t -> t = "dotnet") |> List.length)

// ── SourceRemove ──────────────────────────────────────────────────────────────

let private removeCmd name isGlobal dryRun : SourceRemove.Command =
    { Name = name; IsGlobal = isGlobal; DryRun = dryRun }

let private srcEntry name : SourceConfig = { Name = name; Url = Some $"https://example.com/{name}.git"; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }

[<Fact>]
let ``remove removes source from local config`` () =
    let local   = (LocalConfig.withSources ([srcEntry "kb"; srcEntry "other"]) emptyLocal)
    let written = ref None
    let deps    = makeDeps (Some local) None [] written (ref None)
    assertOk (SourceRemove.execute deps (removeCmd "kb" false false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<string seq>(["other"], (LocalConfig.sources cfg) |> List.map (fun s -> s.Name))

[<Fact>]
let ``remove fails when source not found in local config`` () =
    let local = (LocalConfig.withSources ([srcEntry "other"]) emptyLocal)
    let written = ref None
    let deps  = makeDeps (Some local) None [] written (ref None)
    assertError (SourceRemove.execute deps (removeCmd "missing" false false))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``remove dryrun does not write local config`` () =
    let local   = (LocalConfig.withSources ([srcEntry "kb"]) emptyLocal)
    let written = ref None
    let deps    = makeDeps (Some local) None [] written (ref None)
    assertOk (SourceRemove.execute deps (removeCmd "kb" false true))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``remove fails when no local config`` () =
    let deps = makeDeps None None [] (ref None) (ref None)
    assertError (SourceRemove.execute deps (removeCmd "kb" false false))

[<Fact>]
let ``remove removes source from global config`` () =
    let globalCfg = (GlobalConfig.withSources ([srcEntry "shared"; srcEntry "other"]) emptyGlobal)
    let written   = ref None
    let deps      = makeDeps None (Some globalCfg) [] (ref None) written
    assertOk (SourceRemove.execute deps (removeCmd "shared" true false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<string seq>(["other"], (GlobalConfig.sources cfg) |> List.map (fun s -> s.Name))

[<Fact>]
let ``remove fails when source not found in global config`` () =
    let globalCfg = (GlobalConfig.withSources ([srcEntry "other"]) emptyGlobal)
    let written   = ref None
    let deps      = makeDeps None (Some globalCfg) [] (ref None) written
    assertError (SourceRemove.execute deps (removeCmd "missing" true false))
    Assert.True(written.Value.IsNone)

let private scanBundlesOf files (cmd: SourceAdd.Command) =
    let written = ref None
    let deps = okfDeps files written
    let result = SourceAdd.execute deps cmd
    result, written.Value |> Option.map (fun cfg -> (LocalConfig.sources cfg).[0].Bundles)

let private kbUrl = "https://github.com/acme/kb.git"

[<Fact>]
let ``--scan --exclude . registers nested bundles instead of the root`` () =
    let _, bundles = scanBundlesOf [ "index.md", okfIndex; "a/index.md", okfIndex; "b/index.md", okfIndex ] { simpleCmd kbUrl with Scan = true; Exclude = [ "." ] }
    Assert.Equal<Bundle list>([ { Path = "a"; Kind = Okf }; { Path = "b"; Kind = Okf } ], bundles.Value)

[<Fact>]
let ``--scan --exclude a keeps only the root, and with --nested drops just a`` () =
    let files = [ "index.md", okfIndex; "a/index.md", okfIndex; "b/index.md", okfIndex ]
    let _, plain = scanBundlesOf files { simpleCmd kbUrl with Scan = true; Exclude = [ "a" ] }
    Assert.Equal<Bundle list>([ { Path = ""; Kind = Okf } ], plain.Value)
    let _, nested = scanBundlesOf files { simpleCmd kbUrl with Scan = true; Nested = true; Exclude = [ "a" ] }
    Assert.Equal<Bundle list>([ { Path = ""; Kind = Okf }; { Path = "b"; Kind = Okf } ], nested.Value)

[<Fact>]
let ``--scan --exclude glob drops children but keeps the parent`` () =
    let files = [ "docs/index.md", okfIndex; "docs/x/index.md", okfIndex ]
    let _, bundles = scanBundlesOf files { simpleCmd kbUrl with Scan = true; Nested = true; Exclude = [ "docs/*" ] }
    Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Okf } ], bundles.Value)

[<Fact>]
let ``--exclude without --scan is rejected`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    assertError (SourceAdd.execute deps { simpleCmd kbUrl with Exclude = [ "a" ] })
    Assert.True(written.Value.IsNone)

// ── enable / disable ────────────────────────────────────────────────────────

let private toggle name enable isGlobal : SourceToggle.Command = { Name = name; Enable = enable; IsGlobal = isGlobal; DryRun = false }

[<Fact>]
let ``disable sets enabled false on a local source`` () =
    let local = LocalConfig.withSources [ srcEntry "kb" ] emptyLocal
    let written = ref None
    let deps = makeDeps (Some local) None [] written (ref None)
    Assert.True (SourceToggle.execute deps (toggle "kb" false false) |> Result.isOk)
    Assert.Equal(Some false, (LocalConfig.sources written.Value.Value).Head.Enabled)

[<Fact>]
let ``enable clears the flag again`` () =
    let local = LocalConfig.withSources [ { srcEntry "kb" with Enabled = Some false } ] emptyLocal
    let written = ref None
    let deps = makeDeps (Some local) None [] written (ref None)
    Assert.True (SourceToggle.execute deps (toggle "kb" true false) |> Result.isOk)
    Assert.Equal(None, (LocalConfig.sources written.Value.Value).Head.Enabled)

[<Fact>]
let ``disabling a global-only source adds a local alias`` () =
    let global' = GlobalConfig.withSources [ srcEntry "shared" ] emptyGlobal
    let written = ref None
    let deps = makeDeps (Some emptyLocal) (Some global') [] written (ref None)
    Assert.True (SourceToggle.execute deps (toggle "shared" false false) |> Result.isOk)
    let alias = (LocalConfig.sources written.Value.Value).Head
    Assert.Equal("shared", alias.Name)
    Assert.Equal(None, alias.Url)
    Assert.Equal(Some false, alias.Enabled)

[<Fact>]
let ``disable with global flag edits the global config`` () =
    let global' = GlobalConfig.withSources [ srcEntry "shared" ] emptyGlobal
    let writtenGlobal = ref None
    let deps = makeDeps (Some emptyLocal) (Some global') [] (ref None) writtenGlobal
    Assert.True (SourceToggle.execute deps (toggle "shared" false true) |> Result.isOk)
    Assert.Equal(Some false, (GlobalConfig.sources writtenGlobal.Value.Value).Head.Enabled)

[<Fact>]
let ``toggling an unknown source is an error`` () =
    let deps = makeDeps (Some emptyLocal) None [] (ref None) (ref None)
    Assert.True (SourceToggle.execute deps (toggle "nope" false false) |> Result.isError)

// ── Local folder sources ─────────────────────────────────────────────────────

let private folderDeps (written: LocalConfig option ref) =
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    { deps with
        IsPlainFolder = fun p -> p = "/notes/second-brain"
        ParseYamlBlock = Eru.Adapters.YamlAdapter.parse
        ScanLocalFolder = fun _ _ _ -> Ok [ "index.md", "1:1"; "people/dev.md", "1:2" ]
        ReadLocalFile = fun p ->
            if p = "/notes/second-brain/index.md" then Ok (Some "---\nokf_version: 0.2\n---\n") else Ok None
        FetchRemoteContent = fun _ _ _ -> Error "git must not be used" }

[<Fact>]
let ``folder source takes its name from the last path segment`` () =
    let written = ref None
    assertOk (SourceAdd.execute (folderDeps written) (simpleCmd "/notes/second-brain"))
    Assert.Equal("second-brain", (LocalConfig.sources written.Value.Value).[0].Name)

[<Fact>]
let ``folder source rejects --branch and --basepath`` () =
    let written = ref None
    assertError (SourceAdd.execute (folderDeps written) { simpleCmd "/notes/second-brain" with Branch = Some "main" })
    assertError (SourceAdd.execute (folderDeps written) { simpleCmd "/notes/second-brain" with BasePath = Some "x" })
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``folder source --scan registers an okf bundle without touching git`` () =
    let written = ref None
    assertOk (SourceAdd.execute (folderDeps written) { simpleCmd "/notes/second-brain" with Scan = true })
    let src = (LocalConfig.sources written.Value.Value).[0]
    Assert.Equal<Bundle list>([ { Path = ""; Kind = Okf } ], src.Bundles)
