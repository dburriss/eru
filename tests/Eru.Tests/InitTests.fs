module Eru.Tests.InitTests

open Xunit
open Eru

let private emptyGlobal : GlobalConfig = { Version = 1; DefaultSources = []; Collections = []; Defaults = None }

let private makeDeps
    (globalCfg: GlobalConfig option)
    (capturedFile: (string * string) option ref)
    (capturedGlobal: GlobalConfig option ref) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok None
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun cfg -> capturedGlobal.Value <- Some cfg; Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok []
        WriteLocalFile      = fun path content -> capturedFile.Value <- Some (path, content); Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/tmp/cwd"
        ReadCachedManifest  = fun _ -> Ok None
        CacheSourceManifest = fun _ _ -> Ok ()
    }

let private cmd force isGlobal path =
    { Init.Command.Force = force; Init.Command.IsGlobal = isGlobal; Init.Command.Path = path }

// ── Local init ───────────────────────────────────────────────────────────────

[<Fact>]
let ``init writes .eru/config.json in cwd by default`` () =
    let capturedFile = ref None
    let deps = makeDeps None capturedFile (ref None)
    let exitCode = Init.run deps (cmd false false None)
    Assert.Equal(0, exitCode)
    match capturedFile.Value with
    | None           -> Assert.Fail "nothing written"
    | Some (path, _) -> Assert.Equal("/tmp/cwd/.eru/config.json", path)

[<Fact>]
let ``init writes .eru/config.json in provided path`` () =
    let capturedFile = ref None
    let deps = makeDeps None capturedFile (ref None)
    let exitCode = Init.run deps (cmd false false (Some "/custom/dir"))
    Assert.Equal(0, exitCode)
    match capturedFile.Value with
    | None           -> Assert.Fail "nothing written"
    | Some (path, _) -> Assert.Equal("/custom/dir/.eru/config.json", path)

// ── Global init ──────────────────────────────────────────────────────────────

[<Fact>]
let ``init --global creates empty global config when none exists`` () =
    let capturedGlobal = ref None
    let deps = makeDeps None (ref None) capturedGlobal
    let exitCode = Init.run deps (cmd false true None)
    Assert.Equal(0, exitCode)
    match capturedGlobal.Value with
    | None     -> Assert.Fail "WriteGlobalConfig not called"
    | Some cfg ->
        Assert.Equal(1, cfg.Version)
        Assert.Empty(cfg.DefaultSources)
        Assert.Empty(cfg.Collections)
        match cfg.Defaults with
        | None -> Assert.Fail "expected Defaults to be Some"
        | Some d ->
            Assert.Equal<string list>(Config.defaultBlockPatterns, d.BlockPatterns.Value)
            Assert.Equal<string list>(Config.defaultAllowPatterns, d.AllowPatterns.Value)
            Assert.Equal(Config.defaultAllowBinaries, d.AllowBinaries.Value)

[<Fact>]
let ``init --global errors when global config already exists without --force`` () =
    let capturedGlobal = ref None
    let deps = makeDeps (Some emptyGlobal) (ref None) capturedGlobal
    let exitCode = Init.run deps (cmd false true None)
    Assert.Equal(1, exitCode)
    Assert.True(capturedGlobal.Value.IsNone)

[<Fact>]
let ``init --global --force overwrites existing global config`` () =
    let capturedGlobal = ref None
    let deps = makeDeps (Some emptyGlobal) (ref None) capturedGlobal
    let exitCode = Init.run deps (cmd true true None)
    Assert.Equal(0, exitCode)
    Assert.True(capturedGlobal.Value.IsSome)

// ── Mutual exclusion ─────────────────────────────────────────────────────────

[<Fact>]
let ``init --global with path is an error`` () =
    let capturedFile = ref None
    let capturedGlobal = ref None
    let deps = makeDeps None capturedFile capturedGlobal
    let exitCode = Init.run deps (cmd false true (Some "/some/path"))
    Assert.Equal(1, exitCode)
    Assert.True(capturedFile.Value.IsNone)
    Assert.True(capturedGlobal.Value.IsNone)
