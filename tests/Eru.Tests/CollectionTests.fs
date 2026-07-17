module Eru.Tests.CollectionTests

open Xunit
open Eru

let private emptyLocal  : LocalConfig  = { Version = 1; Sources = []; Collections = []; Settings = None }
let private emptyGlobal : GlobalConfig = { Version = 1; DefaultSources = []; Collections = []; Defaults = None }

let private makeDeps
    (localCfg: LocalConfig option)
    (globalCfg: GlobalConfig option)
    (capturedLocal: LocalConfig option ref)
    (capturedGlobal: GlobalConfig option ref) : Deps =
    {
        ReadGlobalConfig    = fun () -> Ok globalCfg
        ReadLocalConfig     = fun () -> Ok localCfg
        WriteLocalConfig    = fun cfg -> capturedLocal.Value  <- Some cfg; Ok ()
        WriteGlobalConfig   = fun cfg -> capturedGlobal.Value <- Some cfg; Ok ()
        ReadLockEntries     = fun _ -> Ok []
        WriteLockEntries    = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok []
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
        BuildSearchIndex        = fun _ _ -> ()
    }

let private fileRef source path : CollectionFileRef =
    { Source = source; RemotePath = path; Tags = []; Description = None }

let private removeCmd colName source path isGlobal dryRun : CollectionRemoveFile.Command =
    { CollectionName = colName; Source = source; RemotePath = path; IsGlobal = isGlobal; DryRun = dryRun }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

// ── CollectionRemoveFile ──────────────────────────────────────────────────────

[<Fact>]
let ``removeFile removes matching file ref from local collection`` () =
    let col     = { Name = "my-col"; Tags = []; Files = [fileRef "src" "docs/a.md"; fileRef "src" "docs/b.md"]; Description = None }
    let local   = { emptyLocal with Collections = [col] }
    let written = ref None
    let deps    = makeDeps (Some local) None written (ref None)
    assertOk (CollectionRemoveFile.execute deps (removeCmd "my-col" "src" "docs/a.md" false false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg ->
        let remaining = (cfg.Collections |> List.find (fun c -> c.Name = "my-col")).Files
        Assert.Equal(1, remaining.Length)
        Assert.Equal("docs/b.md", remaining[0].RemotePath)

[<Fact>]
let ``removeFile fails when collection not found`` () =
    let local   = { emptyLocal with Collections = [] }
    let written = ref None
    let deps    = makeDeps (Some local) None written (ref None)
    assertError (CollectionRemoveFile.execute deps (removeCmd "missing" "src" "docs/a.md" false false))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``removeFile fails when file ref not found`` () =
    let col     = { Name = "my-col"; Tags = []; Files = [fileRef "src" "docs/b.md"]; Description = None }
    let local   = { emptyLocal with Collections = [col] }
    let written = ref None
    let deps    = makeDeps (Some local) None written (ref None)
    assertError (CollectionRemoveFile.execute deps (removeCmd "my-col" "src" "docs/a.md" false false))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``removeFile dryrun does not write`` () =
    let col     = { Name = "my-col"; Tags = []; Files = [fileRef "src" "docs/a.md"]; Description = None }
    let local   = { emptyLocal with Collections = [col] }
    let written = ref None
    let deps    = makeDeps (Some local) None written (ref None)
    assertOk (CollectionRemoveFile.execute deps (removeCmd "my-col" "src" "docs/a.md" false true))
    Assert.True(written.Value.IsNone)

[<Fact>]
let ``removeFile removes collection entry when last file is removed`` () =
    let col     = { Name = "my-col"; Tags = []; Files = [fileRef "src" "docs/a.md"]; Description = None }
    let local   = { emptyLocal with Collections = [col] }
    let written = ref None
    let deps    = makeDeps (Some local) None written (ref None)
    assertOk (CollectionRemoveFile.execute deps (removeCmd "my-col" "src" "docs/a.md" false false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Empty(cfg.Collections)

[<Fact>]
let ``removeFile fails when no local config`` () =
    let deps = makeDeps None None (ref None) (ref None)
    assertError (CollectionRemoveFile.execute deps (removeCmd "my-col" "src" "docs/a.md" false false))

[<Fact>]
let ``removeFile removes matching file ref from global collection`` () =
    let col       = { Name = "shared-col"; Tags = []; Files = [fileRef "src" "docs/a.md"; fileRef "src" "docs/b.md"]; Description = None }
    let globalCfg = { emptyGlobal with Collections = [col] }
    let written   = ref None
    let deps      = makeDeps None (Some globalCfg) (ref None) written
    assertOk (CollectionRemoveFile.execute deps (removeCmd "shared-col" "src" "docs/a.md" true false))
    match written.Value with
    | None     -> Assert.Fail "nothing written"
    | Some cfg ->
        let remaining = (cfg.Collections |> List.find (fun c -> c.Name = "shared-col")).Files
        Assert.Equal(1, remaining.Length)
        Assert.Equal("docs/b.md", remaining[0].RemotePath)
