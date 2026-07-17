module Eru.Tests.SyncTests

open Xunit
open Eru

// ── Helpers ───────────────────────────────────────────────────────────────────

let private makeSource name url : SourceConfig =
    { Name = name; Url = Some url; Branch = None; BasePath = None }

let private makeLocal sources : LocalConfig =
    { Version = 1; Sources = sources; Collections = []; Settings = None }

let private makeGlobal sources : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = []; Defaults = None }

let private makeLockEntry localPath sourceName remotePath hash : LockEntry =
    { LocalPath = localPath; SourceName = sourceName; RemotePath = remotePath; ContentHash = hash
      Tags = []; Description = None }

type CapturedState = {
    mutable WrittenFiles : (string * string) list
    mutable WrittenLock  : LockEntry list
    mutable LockWritten  : bool
}

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (initialLock: LockEntry list)
    (fetch: string -> string -> string list -> Result<(string * string) list, string>)
    (readLocalFile: string -> Result<string option, string>)
    (writeLock: string -> LockEntry list -> Result<unit, string>)
    (state: CapturedState) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok initialLock
        WriteLockEntries   = fun path entries ->
            state.LockWritten <- true
            state.WrittenLock <- entries
            writeLock path entries
        FetchRemoteContent  = fetch
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun path content ->
            state.WrittenFiles <- state.WrittenFiles @ [(path, content)]
            Ok ()
        ReadLocalFile       = readLocalFile
        DeleteLocalFile     = fun _ -> Ok ()
        HashContent         = fun s -> $"hash:{s}"
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

let private defaultFetch (_url: string) (_branch: string) (paths: string list) : Result<(string * string) list, string> =
    Ok (paths |> List.map (fun p -> (p, $"content:{p}")))

let private newState () : CapturedState =
    { WrittenFiles = []; WrittenLock = []; LockWritten = false }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Fact>]
let ``empty lock file exits 0 with no writes`` () =
    let state = newState ()
    let local = makeLocal [ makeSource "kb" "https://example.com/kb.git" ]
    let deps = makeDeps None (Some local) [] defaultFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)

[<Fact>]
let ``current entry causes no writes`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:content:docs/file.md"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun path -> Ok (Some $"content:{path}")) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)

[<Fact>]
let ``drifted entry overwrites file and updates lock hash`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:old-content"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Single(state.WrittenFiles) |> ignore
    let (path, content) = state.WrittenFiles.[0]
    Assert.Equal("docs/file.md", path)
    Assert.Equal("content:docs/file.md", content)
    Assert.True(state.LockWritten)
    let updated = state.WrittenLock |> List.find (fun e -> e.LocalPath = "docs/file.md")
    Assert.Equal("hash:content:docs/file.md", updated.ContentHash)

[<Fact>]
let ``multiple drifted entries all updated`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entries = [
        makeLockEntry "a.md" "kb" "a.md" "hash:old-a"
        makeLockEntry "b.md" "kb" "b.md" "hash:old-b"
    ]
    let deps = makeDeps None (Some local) entries defaultFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Equal(2, state.WrittenFiles.Length)
    Assert.Equal(2, state.WrittenLock.Length)

[<Fact>]
let ``dry-run with drifted entry writes nothing`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:old-content"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = true })
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)

[<Fact>]
let ``dry-run with current entry writes nothing`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:content:docs/file.md"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun path -> Ok (Some $"content:{path}")) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = true })
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)

[<Fact>]
let ``missing entry leaves lock unchanged and exits 0`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/gone.md" "kb" "docs/gone.md" "hash:old"
    let failFetch _ _ _ = Error "not found"
    let deps = makeDeps None (Some local) [entry] failFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)

[<Fact>]
let ``skipped when source not in config`` () =
    let state = newState ()
    let local = makeLocal []
    let entry = makeLockEntry "docs/file.md" "unknown" "docs/file.md" "hash:old"
    let fetchCalled = ref false
    let trackFetch url branch paths =
        fetchCalled.Value <- true
        defaultFetch url branch paths
    let deps = makeDeps None (Some local) [entry] trackFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.False(fetchCalled.Value)
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)

[<Fact>]
let ``no local config with empty lock exits 0`` () =
    let state = newState ()
    let deps = makeDeps None None [] defaultFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Empty(state.WrittenFiles)

[<Fact>]
let ``lock write failure returns error`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:old-content"
    let failWrite _ _ = Error "disk full"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun _ -> Ok None) failWrite state
    assertError (Sync.execute deps { DryRun = false })

[<Fact>]
let ``local file modified restores content and leaves lock unchanged`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:content:docs/file.md"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun _ -> Ok (Some "locally modified")) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Single(state.WrittenFiles) |> ignore
    let (path, content) = state.WrittenFiles.[0]
    Assert.Equal("docs/file.md", path)
    Assert.Equal("content:docs/file.md", content)
    Assert.False(state.LockWritten)

[<Fact>]
let ``local file missing restores content and leaves lock unchanged`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:content:docs/file.md"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun _ -> Ok None) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Single(state.WrittenFiles) |> ignore
    let (path, content) = state.WrittenFiles.[0]
    Assert.Equal("docs/file.md", path)
    Assert.Equal("content:docs/file.md", content)
    Assert.False(state.LockWritten)

[<Fact>]
let ``local file matching lock hash stays current and nothing is written`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:content:docs/file.md"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun path -> Ok (Some $"content:{path}")) (fun _ _ -> Ok ()) state
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Empty(state.WrittenFiles)
    Assert.False(state.LockWritten)
