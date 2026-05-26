module Eru.Tests.SourceTests

open Xunit
open Eru

let private emptyLocal : LocalConfig = { Version = 1; Sources = []; Settings = None }
let private emptyGlobal : GlobalConfig = { Version = 1; DefaultSources = []; Collections = []; Defaults = None }

let private makeDeps
    (localCfg: LocalConfig option)
    (globalCfg: GlobalConfig option)
    (topLevel: string list)
    (capturedLocal: LocalConfig option ref)
    (capturedGlobal: GlobalConfig option ref) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        WriteLocalConfig   = fun cfg -> capturedLocal.Value <- Some cfg; Ok ()
        WriteGlobalConfig  = fun cfg -> capturedGlobal.Value <- Some cfg; Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel = fun _ _ -> Ok topLevel
        WriteLocalFile     = fun _ _ -> Ok ()
        HashContent        = fun s -> $"sha256:{s}"
        GetCwd             = fun () -> "/tmp"
    }

let private simpleCmd url = {
    Source.AddCommand.Url      = url
    Source.AddCommand.Name     = None
    Source.AddCommand.Branch   = None
    Source.AddCommand.BasePath = None
    Source.AddCommand.IsGlobal = false
}

// ── Name derivation ──────────────────────────────────────────────────────────

[<Fact>]
let ``derives name from HTTPS URL stripping .git`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    let exitCode = Source.add deps (simpleCmd "https://github.com/acme/knowledge-base.git")
    Assert.Equal(0, exitCode)
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("knowledge-base", cfg.Sources[0].Name)

[<Fact>]
let ``derives name from SSH URL stripping .git`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    let exitCode = Source.add deps (simpleCmd "git@github.com:acme/knowledge-base.git")
    Assert.Equal(0, exitCode)
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("knowledge-base", cfg.Sources[0].Name)

[<Fact>]
let ``name override is respected`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "my-kb" }
    Source.add deps cmd |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("my-kb", cfg.Sources[0].Name)

// ── Local config writes ──────────────────────────────────────────────────────

[<Fact>]
let ``writes to local config when eru.json present`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    let exitCode = Source.add deps (simpleCmd "https://github.com/acme/kb.git")
    Assert.Equal(0, exitCode)
    Assert.True(written.Value.IsSome)

[<Fact>]
let ``errors when eru.json absent in local mode`` () =
    let deps = makeDeps None None [] (ref None) (ref None)
    let exitCode = Source.add deps (simpleCmd "https://github.com/acme/kb.git")
    Assert.Equal(1, exitCode)

// ── Global config writes ─────────────────────────────────────────────────────

[<Fact>]
let ``writes to global config when --global flag set`` () =
    let writtenGlobal = ref None
    let deps = makeDeps None (Some emptyGlobal) [] (ref None) writtenGlobal
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with IsGlobal = true }
    let exitCode = Source.add deps cmd
    Assert.Equal(0, exitCode)
    Assert.True(writtenGlobal.Value.IsSome)

[<Fact>]
let ``creates empty global config when none exists`` () =
    let writtenGlobal = ref None
    let deps = makeDeps None None [] (ref None) writtenGlobal
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with IsGlobal = true }
    let exitCode = Source.add deps cmd
    Assert.Equal(0, exitCode)
    match writtenGlobal.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(1, cfg.DefaultSources.Length)

// ── KNOWLEDGE detection ──────────────────────────────────────────────────────

[<Fact>]
let ``detects KNOWLEDGE basePath from top-level listing`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["README.md"; "KNOWLEDGE"; "src"] written (ref None)
    Source.add deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(Some "KNOWLEDGE", cfg.Sources[0].BasePath)

[<Fact>]
let ``detects lowercase knowledge basePath`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["README.md"; "knowledge"] written (ref None)
    Source.add deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(Some "knowledge", cfg.Sources[0].BasePath)

[<Fact>]
let ``no basePath when top-level listing returns empty`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    Source.add deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(None, cfg.Sources[0].BasePath)

[<Fact>]
let ``explicit --basepath overrides auto-detection and skips remote listing`` () =
    let listingCalled = ref false
    let written = ref None
    let deps =
        { makeDeps (Some emptyLocal) None [] written (ref None) with
            ListRemoteTopLevel = fun _ _ -> listingCalled.Value <- true; Ok ["KNOWLEDGE"] }
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with BasePath = Some "docs" }
    Source.add deps cmd |> ignore
    Assert.False(listingCalled.Value, "remote listing should not be called when --basepath is explicit")
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(Some "docs", cfg.Sources[0].BasePath)

// ── Duplicate name ───────────────────────────────────────────────────────────

[<Fact>]
let ``errors on duplicate source name in local config`` () =
    let existing = { emptyLocal with Sources = [{ Name = "kb"; Url = Some "https://x.com"; Branch = None; BasePath = None }] }
    let deps = makeDeps (Some existing) None [] (ref None) (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "kb" }
    let exitCode = Source.add deps cmd
    Assert.Equal(1, exitCode)

[<Fact>]
let ``errors on duplicate source name in global config`` () =
    let existing = { emptyGlobal with DefaultSources = [{ Name = "kb"; Url = Some "https://x.com"; Branch = None; BasePath = None }] }
    let deps = makeDeps None (Some existing) [] (ref None) (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "kb"; IsGlobal = true }
    let exitCode = Source.add deps cmd
    Assert.Equal(1, exitCode)
