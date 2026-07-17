module Eru.Tests.AddTests

open Xunit
open Eru

// ── Test helpers ─────────────────────────────────────────────────────────────

let private makeSource name url branch basePath : SourceConfig =
    { Name = name; Url = url; Branch = branch; BasePath = basePath }

let private emptyCmd : Add.Command = {
    RemotePath     = None
    Tags           = []
    SourceName     = None
    CollectionName = None
    Target         = None
    DryRun         = false
    IsGlobal       = false
}

type CapturedState = {
    mutable WrittenFiles        : (string * string) list
    mutable WrittenLock         : LockEntry list
    mutable WrittenLocalConfig  : LocalConfig option
    mutable WrittenGlobalConfig : GlobalConfig option
}

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (state: CapturedState) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        WriteLocalConfig   = fun cfg -> state.WrittenLocalConfig <- Some cfg; Ok ()
        WriteGlobalConfig  = fun cfg -> state.WrittenGlobalConfig <- Some cfg; Ok ()
        ReadLockEntries    = fun _ -> Ok state.WrittenLock
        WriteLockEntries   = fun _ entries -> state.WrittenLock <- entries; Ok ()
        FetchRemoteContent  = fun _ _ paths -> Ok (paths |> List.map (fun p -> (p, $"content:{p}")))
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun path content -> state.WrittenFiles <- state.WrittenFiles @ [(path, content)]; Ok ()
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
        BuildSearchIndex        = fun _ _ -> ()
    }

let private newState () : CapturedState = { WrittenFiles = []; WrittenLock = []; WrittenLocalConfig = None; WrittenGlobalConfig = None }

let private makeGlobal sources collections : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = collections; Defaults = None }

let private makeLocal sources : LocalConfig =
    { Version = 1; Sources = sources; Collections = []; Settings = None }

let private makeCollection name tags files : CollectionConfig =
    { Name = name; Tags = tags; Files = files; Description = None }

let private makeFileRef source remotePath tags : CollectionFileRef =
    { Source = source; RemotePath = remotePath; Tags = tags; Description = None }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

// ── Validation ───────────────────────────────────────────────────────────────

[<Fact>]
let ``errors when no remote-path tag or collection given`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    assertError (Add.execute deps emptyCmd)

// ── Direct path pull ─────────────────────────────────────────────────────────

[<Fact>]
let ``direct pull writes file and lock entry`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    assertOk (Add.execute deps cmd)
    Assert.Equal(1, state.WrittenFiles.Length)
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("shared/adr.md", path)
    Assert.Equal(1, state.WrittenLock.Length)
    let entry = state.WrittenLock[0]
    Assert.Equal("shared/adr.md", entry.LocalPath)
    Assert.Equal("kb", entry.SourceName)
    Assert.Equal("shared/adr.md", entry.RemotePath)

[<Fact>]
let ``source BasePath prefix is stripped from localPath`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "KNOWLEDGE")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "KNOWLEDGE/shared/adr.md" }
    Add.execute deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("shared/adr.md", path)

[<Fact>]
let ``target directory keeps only filename not full relative path`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; Target = Some "docs/" }
    Add.execute deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("docs/adr.md", path)

[<Fact>]
let ``BasePath strip and target prefix are both applied`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "KNOWLEDGE")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "KNOWLEDGE/shared/adr.md"; Target = Some "docs/" }
    Add.execute deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("docs/adr.md", path)

[<Fact>]
let ``target full file path is used as localPath directly`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; Target = Some "docs/custom.md" }
    Add.execute deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("docs/custom.md", path)
    Assert.Equal("docs/custom.md", state.WrittenLock[0].LocalPath)

[<Fact>]
let ``target bare filename remaps localPath to that filename`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "other/adr.md"; Target = Some "test.md" }
    Add.execute deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("test.md", path)
    Assert.Equal("test.md", state.WrittenLock[0].LocalPath)

[<Fact>]
let ``target file path without extension is used as localPath directly`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "tools/mybinary"; Target = Some "bin/mybinary" }
    Add.execute deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("bin/mybinary", path)
    Assert.Equal("bin/mybinary", state.WrittenLock[0].LocalPath)

[<Fact>]
let ``source discriminator prefix in remote-path selects source`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let deps = makeDeps (Some (makeGlobal [src1; src2] [])) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with RemotePath = Some "kb2:shared/adr.md" }
    assertOk (Add.execute deps cmd)
    Assert.Equal("kb2", state.WrittenLock[0].SourceName)

[<Fact>]
let ``explicit --source flag selects source`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let deps = makeDeps (Some (makeGlobal [src1; src2] [])) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; SourceName = Some "kb2" }
    assertOk (Add.execute deps cmd)
    Assert.Equal("kb2", state.WrittenLock[0].SourceName)

[<Fact>]
let ``defaults to first source when no prefix or --source given`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let deps = makeDeps (Some (makeGlobal [src1; src2] [])) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    Add.execute deps cmd |> ignore
    Assert.Equal("kb1", state.WrittenLock[0].SourceName)

[<Fact>]
let ``errors when no sources configured`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    assertError (Add.execute deps cmd)

[<Fact>]
let ``errors when named source not found`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; SourceName = Some "nope" }
    assertError (Add.execute deps cmd)

[<Fact>]
let ``errors when source has no URL`` () =
    let state = newState ()
    let src = makeSource "kb" None None None
    let deps = makeDeps None (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    assertError (Add.execute deps cmd)

[<Fact>]
let ``existing lock entry for same localPath is replaced`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let existing : LockEntry = { LocalPath = "shared/adr.md"; SourceName = "kb"; RemotePath = "shared/adr.md"; ContentHash = "sha256:old"; Tags = []; Description = None }
    state.WrittenLock <- [existing]
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    Add.execute deps cmd |> ignore
    Assert.Equal(1, state.WrittenLock.Length)
    Assert.NotEqual<string>("sha256:old", state.WrittenLock[0].ContentHash)

// ── Short-name resolution ─────────────────────────────────────────────────────

[<Fact>]
let ``bare name with BasePath expands to basePath prefix and md extension`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "github-cli" }
    assertOk (Add.execute deps cmd)
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("github-cli.md", path)
    Assert.Equal("knowledge/github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``bare name with extension and BasePath gets prefix but no double extension`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "github-cli.md" }
    Add.execute deps cmd |> ignore
    Assert.Equal("knowledge/github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``bare name with BasePath already prefixed does not double-prefix`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "knowledge/github-cli" }
    Add.execute deps cmd |> ignore
    Assert.Equal("knowledge/github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``bare name without BasePath appends md extension only`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "github-cli" }
    Add.execute deps cmd |> ignore
    Assert.Equal("github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``explicit sub-path without extension gets md appended`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "tools/adr" }
    Add.execute deps cmd |> ignore
    Assert.Equal("tools/adr.md", state.WrittenLock[0].RemotePath)

// ── Tag-based pull ────────────────────────────────────────────────────────────

[<Fact>]
let ``tag pull fetches all files matching tags from collections`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let col =
        makeCollection "dotnet-starter" ["dotnet"] [
            makeFileRef "kb" "shared/logging.fs" ["dotnet"]
            makeFileRef "kb" "shared/metrics.fs" ["dotnet"]
        ]
    let gcfg = makeGlobal [src] [col]
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with Tags = ["dotnet"] }
    assertOk (Add.execute deps cmd)
    Assert.Equal(2, state.WrittenFiles.Length)

[<Fact>]
let ``tag pull errors when no global config`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps None (Some (makeLocal [src])) state
    let cmd = { emptyCmd with Tags = ["dotnet"] }
    assertError (Add.execute deps cmd)

[<Fact>]
let ``tag pull errors when no files match tags`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let gcfg = makeGlobal [src] []
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with Tags = ["dotnet"] }
    assertError (Add.execute deps cmd)

// ── Collection pull ───────────────────────────────────────────────────────────

[<Fact>]
let ``collection pull fetches all files in named collection`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let col =
        makeCollection "starter" [] [
            makeFileRef "kb" "a.md" []
            makeFileRef "kb" "b.md" []
        ]
    let gcfg = makeGlobal [src] [col]
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with CollectionName = Some "starter" }
    assertOk (Add.execute deps cmd)
    Assert.Equal(2, state.WrittenFiles.Length)

[<Fact>]
let ``collection pull with source prefix filters to that source only`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let col =
        makeCollection "starter" [] [
            makeFileRef "kb1" "a.md" []
            makeFileRef "kb2" "b.md" []
        ]
    let gcfg = makeGlobal [src1; src2] [col]
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with CollectionName = Some "kb1:starter" }
    assertOk (Add.execute deps cmd)
    Assert.Equal(1, state.WrittenFiles.Length)
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("a.md", path)

[<Fact>]
let ``collection pull errors when collection not found`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let gcfg = makeGlobal [src] []
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with CollectionName = Some "nonexistent" }
    assertError (Add.execute deps cmd)

[<Fact>]
let ``collection pull errors when no global config`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps None (Some (makeLocal [src])) state
    let cmd = { emptyCmd with CollectionName = Some "starter" }
    assertError (Add.execute deps cmd)

[<Fact>]
let ``collection pull with source filter leaving no files errors`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let col = makeCollection "starter" [] [makeFileRef "kb" "a.md" []]
    let gcfg = makeGlobal [src] [col]
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with CollectionName = Some "other:starter" }
    assertError (Add.execute deps cmd)

// ── URL shorthand pull ────────────────────────────────────────────────────────

let private githubUrl = "https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md"
let private gitlabUrl = "https://gitlab.com/dburriss/orcai/-/blob/main/knowledge/github-cli.md"

[<Fact>]
let ``GitHub URL auto-registers source to local config and pulls file`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl }
    assertOk (Add.execute deps cmd)
    Assert.Equal(1, state.WrittenFiles.Length)
    Assert.True(state.WrittenLocalConfig.IsSome)
    Assert.Equal(1, state.WrittenLocalConfig.Value.Sources.Length)
    Assert.Equal("orcai", state.WrittenLocalConfig.Value.Sources[0].Name)

[<Fact>]
let ``GitHub URL reuses existing source without writing config`` () =
    let state = newState ()
    let src = makeSource "orcai" (Some "https://github.com/dburriss/orcai") (Some "main") None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl }
    assertOk (Add.execute deps cmd)
    Assert.Equal(1, state.WrittenFiles.Length)
    Assert.True(state.WrittenLocalConfig.IsNone)

[<Fact>]
let ``GitHub URL errors when source name exists with different URL`` () =
    let state = newState ()
    let src = makeSource "orcai" (Some "https://github.com/other/orcai") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl }
    assertError (Add.execute deps cmd)
    Assert.Empty(state.WrittenFiles)

[<Fact>]
let ``GitHub URL with --global writes source to global config`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl; IsGlobal = true }
    assertOk (Add.execute deps cmd)
    Assert.True(state.WrittenGlobalConfig.IsSome)
    Assert.Equal(1, state.WrittenGlobalConfig.Value.DefaultSources.Length)
    Assert.Equal("orcai", state.WrittenGlobalConfig.Value.DefaultSources[0].Name)
    Assert.True(state.WrittenLocalConfig.IsNone)

[<Fact>]
let ``GitLab URL auto-registers source to local config and pulls file`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some gitlabUrl }
    assertOk (Add.execute deps cmd)
    Assert.Equal(1, state.WrittenFiles.Length)
    Assert.True(state.WrittenLocalConfig.IsSome)
    let src = state.WrittenLocalConfig.Value.Sources[0]
    Assert.Equal("orcai", src.Name)
    Assert.Equal(Some "https://gitlab.com/dburriss/orcai", src.Url)

// ── Glob pattern support ──────────────────────────────────────────────────────

[<Fact>]
let ``glob pattern does not get md extension appended`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let fetchCalled = ref ""
    let deps =
        { makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state with
            FetchRemoteContent = fun _ _ paths ->
                fetchCalled.Value <- List.head paths
                Ok [(List.head paths + "/a.md", "content-a")] }
    let cmd = { emptyCmd with RemotePath = Some "dotnet/*.md" }
    assertOk (Add.execute deps cmd)
    Assert.Equal("dotnet/*.md", fetchCalled.Value)

[<Fact>]
let ``glob pattern producing multiple files writes all files and lock entries`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps =
        { makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state with
            FetchRemoteContent = fun _ _ _ ->
                Ok [("docs/a.md", "content-a"); ("docs/b.md", "content-b")] }
    let cmd = { emptyCmd with RemotePath = Some "docs/*.md" }
    assertOk (Add.execute deps cmd)
    Assert.Equal(2, state.WrittenFiles.Length)
    Assert.Equal(2, state.WrittenLock.Length)
    let paths = state.WrittenLock |> List.map (fun e -> e.RemotePath) |> Set.ofList
    Assert.Contains("docs/a.md", paths)
    Assert.Contains("docs/b.md", paths)
