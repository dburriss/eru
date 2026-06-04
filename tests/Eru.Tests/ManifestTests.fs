module Eru.Tests.ManifestTests

open Xunit
open Eru

let private emptyManifest : SourceManifest = { Version = 1; Description = None; Files = [] }

let private makeFileRef path tags description : ManifestFileRef =
    { Path = path; Tags = tags; Description = description }

let private makeDeps
    (manifest: SourceManifest option)
    (capturedManifest: SourceManifest option ref)
    (globResults: string -> string list) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok None
        ReadLocalConfig    = fun () -> Ok None
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
        DeleteLocalFile     = fun _ -> Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/tmp"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok manifest
        WriteLocalManifest      = fun m -> capturedManifest.Value <- Some m; Ok ()
        ResolveLocalGlob        = globResults
        ReadSourceIndex         = fun _ -> Ok None
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        BuildSearchIndex        = fun _ _ -> ()
    }

let private initCmd force = { ManifestInit.Command.Force = force }

let private addCmd path tags description dryrun : ManifestAdd.Command =
    { Path = path; Tags = tags; Description = description; DryRun = dryrun }

let private removeCmd path dryrun : ManifestRemove.Command =
    { Path = path; DryRun = dryrun }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

// ── init ─────────────────────────────────────────────────────────────────────

[<Fact>]
let ``init creates empty manifest when none exists`` () =
    let captured = ref None
    let deps = makeDeps None captured (fun _ -> [])
    assertOk (ManifestInit.execute deps (initCmd false))
    match captured.Value with
    | None -> Assert.Fail "no manifest written"
    | Some m ->
        Assert.Equal(1, m.Version)
        Assert.Empty(m.Files)

[<Fact>]
let ``init without force fails when manifest already exists`` () =
    let captured = ref None
    let deps = makeDeps (Some emptyManifest) captured (fun _ -> [])
    assertError (ManifestInit.execute deps (initCmd false))
    Assert.True(captured.Value.IsNone)

[<Fact>]
let ``init with force overwrites existing manifest`` () =
    let existing = { emptyManifest with Files = [ makeFileRef "README.md" [] None ] }
    let captured = ref None
    let deps = makeDeps (Some existing) captured (fun _ -> [])
    assertOk (ManifestInit.execute deps (initCmd true))
    match captured.Value with
    | None -> Assert.Fail "no manifest written"
    | Some m -> Assert.Empty(m.Files)

// ── addFile ───────────────────────────────────────────────────────────────────

[<Fact>]
let ``addFile appends a new entry`` () =
    let captured = ref None
    let deps = makeDeps (Some emptyManifest) captured (fun _ -> [])
    assertOk (ManifestAdd.execute deps (addCmd "docs/*.md" ["docs"] (Some "All docs") false))
    match captured.Value with
    | None -> Assert.Fail "no manifest written"
    | Some m ->
        Assert.Single(m.Files) |> ignore
        Assert.Equal("docs/*.md", m.Files[0].Path)
        Assert.Equal(1, m.Files[0].Tags.Length)
        Assert.Equal("docs", m.Files[0].Tags[0])
        Assert.Equal(Some "All docs", m.Files[0].Description)

[<Fact>]
let ``addFile fails when path already present`` () =
    let existing = { emptyManifest with Files = [ makeFileRef "README.md" [] None ] }
    let captured = ref None
    let deps = makeDeps (Some existing) captured (fun _ -> [])
    assertError (ManifestAdd.execute deps (addCmd "README.md" [] None false))
    Assert.True(captured.Value.IsNone)

[<Fact>]
let ``addFile dryrun does not write`` () =
    let captured = ref None
    let deps = makeDeps (Some emptyManifest) captured (fun _ -> [])
    assertOk (ManifestAdd.execute deps (addCmd "README.md" [] None true))
    Assert.True(captured.Value.IsNone)

[<Fact>]
let ``addFile fails when no manifest exists`` () =
    let captured = ref None
    let deps = makeDeps None captured (fun _ -> [])
    assertError (ManifestAdd.execute deps (addCmd "README.md" [] None false))

// ── removeFile ────────────────────────────────────────────────────────────────

[<Fact>]
let ``removeFile removes matching entry`` () =
    let existing = { emptyManifest with Files = [ makeFileRef "README.md" [] None; makeFileRef "docs/*.md" ["docs"] None ] }
    let captured = ref None
    let deps = makeDeps (Some existing) captured (fun _ -> [])
    assertOk (ManifestRemove.execute deps (removeCmd "README.md" false))
    match captured.Value with
    | None -> Assert.Fail "no manifest written"
    | Some m ->
        Assert.Single(m.Files) |> ignore
        Assert.Equal("docs/*.md", m.Files[0].Path)

[<Fact>]
let ``removeFile fails when path not found`` () =
    let captured = ref None
    let deps = makeDeps (Some emptyManifest) captured (fun _ -> [])
    assertError (ManifestRemove.execute deps (removeCmd "missing.md" false))
    Assert.True(captured.Value.IsNone)

[<Fact>]
let ``removeFile dryrun does not write`` () =
    let existing = { emptyManifest with Files = [ makeFileRef "README.md" [] None ] }
    let captured = ref None
    let deps = makeDeps (Some existing) captured (fun _ -> [])
    assertOk (ManifestRemove.execute deps (removeCmd "README.md" true))
    Assert.True(captured.Value.IsNone)

[<Fact>]
let ``removeFile fails when no manifest exists`` () =
    let captured = ref None
    let deps = makeDeps None captured (fun _ -> [])
    assertError (ManifestRemove.execute deps (removeCmd "README.md" false))

// ── verify ────────────────────────────────────────────────────────────────────

[<Fact>]
let ``verify returns ok with no missing when all paths resolve`` () =
    let existing = {
        emptyManifest with
            Files = [ makeFileRef "README.md" [] None; makeFileRef "docs/*.md" [] None ]
    }
    let deps = makeDeps (Some existing) (ref None) (fun p ->
        if p = "README.md" then ["README.md"]
        elif p = "docs/*.md" then ["docs/guide.md"; "docs/api.md"]
        else [])
    match ManifestVerify.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok r -> Assert.Empty(r.Missing)

[<Fact>]
let ``verify returns ok with missing paths when a path resolves to nothing`` () =
    let existing = {
        emptyManifest with
            Files = [ makeFileRef "README.md" [] None; makeFileRef "missing/*.md" [] None ]
    }
    let deps = makeDeps (Some existing) (ref None) (fun p ->
        if p = "README.md" then ["README.md"] else [])
    match ManifestVerify.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok r -> Assert.NotEmpty(r.Missing)

[<Fact>]
let ``verify returns ok with no missing for empty manifest`` () =
    let deps = makeDeps (Some emptyManifest) (ref None) (fun _ -> [])
    match ManifestVerify.execute deps with
    | Error e -> Assert.Fail(e)
    | Ok r -> Assert.Empty(r.Missing)

[<Fact>]
let ``verify fails when no manifest exists`` () =
    let deps = makeDeps None (ref None) (fun _ -> [])
    assertError (ManifestVerify.execute deps)
