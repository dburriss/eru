module Eru.Tests.SourceTests

open Xunit
open Eru

let private emptyLocal : LocalConfig = { Version = 1; Sources = []; Collections = []; Settings = None }
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
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok topLevel
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
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

let private simpleCmd url : SourceAdd.Command = {
    Url      = url
    Name     = None
    Branch   = None
    BasePath = None
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
    | Some cfg -> Assert.Equal("knowledge-base", cfg.Sources[0].Name)

[<Fact>]
let ``derives name from SSH URL stripping .git`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    assertOk (SourceAdd.execute deps (simpleCmd "git@github.com:acme/knowledge-base.git"))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("knowledge-base", cfg.Sources[0].Name)

[<Fact>]
let ``name override is respected`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    let cmd = { simpleCmd "https://github.com/acme/kb.git" with Name = Some "my-kb" }
    SourceAdd.execute deps cmd |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal("my-kb", cfg.Sources[0].Name)

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
    | Some cfg -> Assert.Equal(1, cfg.DefaultSources.Length)

// ── KNOWLEDGE detection ──────────────────────────────────────────────────────

[<Fact>]
let ``detects KNOWLEDGE basePath from top-level listing`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["README.md"; "KNOWLEDGE"; "src"] written (ref None)
    SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(Some "KNOWLEDGE", cfg.Sources[0].BasePath)

[<Fact>]
let ``detects lowercase knowledge basePath`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None ["README.md"; "knowledge"] written (ref None)
    SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal(Some "knowledge", cfg.Sources[0].BasePath)

[<Fact>]
let ``no basePath when top-level listing returns empty`` () =
    let written = ref None
    let deps = makeDeps (Some emptyLocal) None [] written (ref None)
    SourceAdd.execute deps (simpleCmd "https://github.com/acme/kb.git") |> ignore
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
    SourceAdd.execute deps cmd |> ignore
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
    assertError (SourceAdd.execute deps cmd)

[<Fact>]
let ``errors on duplicate source name in global config`` () =
    let existing = { emptyGlobal with DefaultSources = [{ Name = "kb"; Url = Some "https://x.com"; Branch = None; BasePath = None }] }
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
    let local = { emptyLocal with Sources = [{ Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; BasePath = None }] }
    let deps  = makeDeps (Some local) (Some emptyGlobal) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Equal(Some "https://example.com/kb.git", row.Url)
        Assert.Equal("local", row.Scope)

[<Fact>]
let ``list shows local alias source with alias scope`` () =
    let localCfg  = { emptyLocal  with Sources        = [{ Name = "kb"; Url = None; Branch = None; BasePath = None }] }
    let globalCfg = { emptyGlobal with DefaultSources = [{ Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; BasePath = None }] }
    let deps      = makeDeps (Some localCfg) (Some globalCfg) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Equal("local → global alias", row.Scope)

[<Fact>]
let ``list shows global-only source with global scope`` () =
    let globalCfg = { emptyGlobal with DefaultSources = [{ Name = "shared"; Url = Some "https://example.com/shared.git"; Branch = None; BasePath = None }] }
    let deps      = makeDeps (Some emptyLocal) (Some globalCfg) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "shared")
        Assert.Equal(Some "https://example.com/shared.git", row.Url)
        Assert.Equal("global", row.Scope)

[<Fact>]
let ``list shows local sources before global-only sources`` () =
    let localCfg  = { emptyLocal  with Sources        = [{ Name = "local-src";  Url = Some "https://example.com/local.git";  Branch = None; BasePath = None }] }
    let globalCfg = { emptyGlobal with DefaultSources = [{ Name = "global-src"; Url = Some "https://example.com/global.git"; Branch = None; BasePath = None }] }
    let deps      = makeDeps (Some localCfg) (Some globalCfg) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let localIdx  = rows |> List.findIndex (fun r -> r.Name = "local-src")
        let globalIdx = rows |> List.findIndex (fun r -> r.Name = "global-src")
        Assert.True(localIdx < globalIdx, "local source should appear before global-only source")

[<Fact>]
let ``list includes branch and basepath when set`` () =
    let src    = { Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = Some "main"; BasePath = Some "KNOWLEDGE" }
    let local  = { emptyLocal with Sources = [src] }
    let deps   = makeDeps (Some local) (Some emptyGlobal) [] (ref None) (ref None)
    match SourceList.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok rows ->
        let row = rows |> List.find (fun r -> r.Name = "kb")
        Assert.Equal(Some "main", row.Branch)
        Assert.Equal(Some "KNOWLEDGE", row.BasePath)

[<Fact>]
let ``list shows tags from cached manifest`` () =
    let local    = { emptyLocal with Sources = [{ Name = "kb"; Url = Some "https://example.com/kb.git"; Branch = None; BasePath = None }] }
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

let private srcEntry name = { Name = name; Url = Some $"https://example.com/{name}.git"; Branch = None; BasePath = None }

[<Fact>]
let ``remove removes source from local config`` () =
    let local   = { emptyLocal with Sources = [srcEntry "kb"; srcEntry "other"] }
    let written = ref None
    let deps    = makeDeps (Some local) None [] written (ref None)
    assertOk (SourceRemove.execute deps (removeCmd "kb" false false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<string seq>(["other"], cfg.Sources |> List.map (fun s -> s.Name))

[<Fact>]
let ``remove fails when source not found in local config`` () =
    let local = { emptyLocal with Sources = [srcEntry "other"] }
    let written = ref None
    let deps  = makeDeps (Some local) None [] written (ref None)
    assertError (SourceRemove.execute deps (removeCmd "missing" false false))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``remove dryrun does not write local config`` () =
    let local   = { emptyLocal with Sources = [srcEntry "kb"] }
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
    let globalCfg = { emptyGlobal with DefaultSources = [srcEntry "shared"; srcEntry "other"] }
    let written   = ref None
    let deps      = makeDeps None (Some globalCfg) [] (ref None) written
    assertOk (SourceRemove.execute deps (removeCmd "shared" true false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<string seq>(["other"], cfg.DefaultSources |> List.map (fun s -> s.Name))

[<Fact>]
let ``remove fails when source not found in global config`` () =
    let globalCfg = { emptyGlobal with DefaultSources = [srcEntry "other"] }
    let written   = ref None
    let deps      = makeDeps None (Some globalCfg) [] (ref None) written
    assertError (SourceRemove.execute deps (removeCmd "missing" true false))
    Assert.True(written.Value.IsNone)
