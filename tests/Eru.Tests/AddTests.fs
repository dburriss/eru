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
        FetchRemoteContent  = fun _ _ path -> Ok [(path, $"content:{path}")]
        ListRemoteTopLevel  = fun _ _ -> Ok []
        WriteLocalFile      = fun path content -> state.WrittenFiles <- state.WrittenFiles @ [(path, content)]; Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/tmp"
        ReadCachedManifest  = fun _ -> Ok None
        CacheSourceManifest = fun _ _ -> Ok ()
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

// ── Validation ───────────────────────────────────────────────────────────────

[<Fact>]
let ``errors when no remote-path tag or collection given`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let exitCode = Add.run deps emptyCmd
    Assert.Equal(1, exitCode)

// ── Direct path pull ─────────────────────────────────────────────────────────

[<Fact>]
let ``direct pull writes file and lock entry`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
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
    Add.run deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("shared/adr.md", path)

[<Fact>]
let ``target prefix is prepended to localPath`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; Target = Some "docs" }
    Add.run deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("docs/shared/adr.md", path)

[<Fact>]
let ``BasePath strip and target prefix are both applied`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "KNOWLEDGE")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "KNOWLEDGE/shared/adr.md"; Target = Some "docs" }
    Add.run deps cmd |> ignore
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("docs/shared/adr.md", path)

[<Fact>]
let ``source discriminator prefix in remote-path selects source`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let deps = makeDeps (Some (makeGlobal [src1; src2] [])) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with RemotePath = Some "kb2:shared/adr.md" }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    Assert.Equal("kb2", state.WrittenLock[0].SourceName)

[<Fact>]
let ``explicit --source flag selects source`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let deps = makeDeps (Some (makeGlobal [src1; src2] [])) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; SourceName = Some "kb2" }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    Assert.Equal("kb2", state.WrittenLock[0].SourceName)

[<Fact>]
let ``defaults to first source when no prefix or --source given`` () =
    let state = newState ()
    let src1 = makeSource "kb1" (Some "https://one.com") None None
    let src2 = makeSource "kb2" (Some "https://two.com") None None
    let deps = makeDeps (Some (makeGlobal [src1; src2] [])) (Some (makeLocal [src1; src2])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    Add.run deps cmd |> ignore
    Assert.Equal("kb1", state.WrittenLock[0].SourceName)

[<Fact>]
let ``errors when no sources configured`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``errors when named source not found`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md"; SourceName = Some "nope" }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``errors when source has no URL`` () =
    let state = newState ()
    let src = makeSource "kb" None None None
    let deps = makeDeps None (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``existing lock entry for same localPath is replaced`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let existing : LockEntry = { LocalPath = "shared/adr.md"; SourceName = "kb"; RemotePath = "shared/adr.md"; ContentHash = "sha256:old" }
    state.WrittenLock <- [existing]
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "shared/adr.md" }
    Add.run deps cmd |> ignore
    Assert.Equal(1, state.WrittenLock.Length)
    Assert.NotEqual<string>("sha256:old", state.WrittenLock[0].ContentHash)

// ── Short-name resolution ─────────────────────────────────────────────────────

[<Fact>]
let ``bare name with BasePath expands to basePath prefix and md extension`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "github-cli" }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    let (path, _) = state.WrittenFiles[0]
    Assert.Equal("github-cli.md", path)
    Assert.Equal("knowledge/github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``bare name with extension and BasePath gets prefix but no double extension`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "github-cli.md" }
    Add.run deps cmd |> ignore
    Assert.Equal("knowledge/github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``bare name with BasePath already prefixed does not double-prefix`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "knowledge/github-cli" }
    Add.run deps cmd |> ignore
    Assert.Equal("knowledge/github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``bare name without BasePath appends md extension only`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "github-cli" }
    Add.run deps cmd |> ignore
    Assert.Equal("github-cli.md", state.WrittenLock[0].RemotePath)

[<Fact>]
let ``explicit sub-path without extension gets md appended`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None (Some "knowledge")
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some "tools/adr" }
    Add.run deps cmd |> ignore
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
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    Assert.Equal(2, state.WrittenFiles.Length)

[<Fact>]
let ``tag pull errors when no global config`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps None (Some (makeLocal [src])) state
    let cmd = { emptyCmd with Tags = ["dotnet"] }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``tag pull errors when no files match tags`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let gcfg = makeGlobal [src] []
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with Tags = ["dotnet"] }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

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
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
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
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
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
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``collection pull errors when no global config`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let deps = makeDeps None (Some (makeLocal [src])) state
    let cmd = { emptyCmd with CollectionName = Some "starter" }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``collection pull with source filter leaving no files errors`` () =
    let state = newState ()
    let src = makeSource "kb" (Some "https://x.com") None None
    let col = makeCollection "starter" [] [makeFileRef "kb" "a.md" []]
    let gcfg = makeGlobal [src] [col]
    let deps = makeDeps (Some gcfg) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with CollectionName = Some "other:starter" }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)

// ── URL shorthand pull ────────────────────────────────────────────────────────

let private githubUrl = "https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md"
let private gitlabUrl = "https://gitlab.com/dburriss/orcai/-/blob/main/knowledge/github-cli.md"

[<Fact>]
let ``GitHub URL auto-registers source to local config and pulls file`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
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
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    Assert.Equal(1, state.WrittenFiles.Length)
    Assert.True(state.WrittenLocalConfig.IsNone)

[<Fact>]
let ``GitHub URL errors when source name exists with different URL`` () =
    let state = newState ()
    let src = makeSource "orcai" (Some "https://github.com/other/orcai") None None
    let deps = makeDeps (Some (makeGlobal [src] [])) (Some (makeLocal [src])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl }
    let exitCode = Add.run deps cmd
    Assert.Equal(1, exitCode)
    Assert.Empty(state.WrittenFiles)

[<Fact>]
let ``GitHub URL with --global writes source to global config`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some githubUrl; IsGlobal = true }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    Assert.True(state.WrittenGlobalConfig.IsSome)
    Assert.Equal(1, state.WrittenGlobalConfig.Value.DefaultSources.Length)
    Assert.Equal("orcai", state.WrittenGlobalConfig.Value.DefaultSources[0].Name)
    Assert.True(state.WrittenLocalConfig.IsNone)

[<Fact>]
let ``GitLab URL auto-registers source to local config and pulls file`` () =
    let state = newState ()
    let deps = makeDeps (Some (makeGlobal [] [])) (Some (makeLocal [])) state
    let cmd = { emptyCmd with RemotePath = Some gitlabUrl }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
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
            FetchRemoteContent = fun _ _ path ->
                fetchCalled.Value <- path
                Ok [(path + "/a.md", "content-a")] }
    let cmd = { emptyCmd with RemotePath = Some "dotnet/*.md" }
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
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
    let exitCode = Add.run deps cmd
    Assert.Equal(0, exitCode)
    Assert.Equal(2, state.WrittenFiles.Length)
    Assert.Equal(2, state.WrittenLock.Length)
    let paths = state.WrittenLock |> List.map (fun e -> e.RemotePath) |> Set.ofList
    Assert.Contains("docs/a.md", paths)
    Assert.Contains("docs/b.md", paths)
