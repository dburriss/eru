module Eru.Tests.SyncTests

open Xunit
open Eru

// ── Helpers ───────────────────────────────────────────────────────────────────

let private makeSource name url : SourceConfig =
    { Name = name; Url = Some url; Branch = None; Bundles = []; Files = None; Search = None; Okf = None; Site = None; Enabled = None }

let private makeSourceWithBundles name url bundles : SourceConfig =
    { Name = name; Url = Some url; Branch = None; Bundles = bundles; Files = None; Search = None; Okf = None; Site = None; Enabled = None }

let private makeLocal sources : LocalConfig =
    { LocalConfig.empty with Source = Some { SourceSection.empty with Items = Some (sources) } }

let private makeGlobal sources : GlobalConfig =
    { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some (sources) } }

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
        ReadEnvConfig    = fun () -> Ok None
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
let ``uncached entry fetched from git is written to target path and to cache`` () =
    let state = newState ()
    let source = makeSource "kb" "https://example.com/kb.git"
    let local = makeLocal [ source ]
    let entry = makeLockEntry "docs/file.md" "kb" "docs/file.md" "hash:content:docs/file.md"
    let deps = makeDeps None (Some local) [entry] defaultFetch (fun path -> Ok (Some $"content:{path}")) (fun _ _ -> Ok ()) state
    let cachedWrites = System.Collections.Generic.List<string * string * string>()
    let indexWrites = System.Collections.Generic.List<string * SourceIndex>()
    let deps =
        { deps with
            CacheSourceContent = fun sourceName hash content ->
                cachedWrites.Add(sourceName, hash, content)
                Ok "files/fakehex"
            WriteSourceIndex = fun sourceName idx ->
                indexWrites.Add(sourceName, idx)
                Ok () }
    assertOk (Sync.execute deps { DryRun = false })
    Assert.Contains(cachedWrites, fun (sn, _, c) -> sn = "kb" && c = "content:docs/file.md")
    Assert.Contains(indexWrites, fun (sn, idx) ->
        sn = "kb" &&
        match Map.tryFind "docs/file.md" idx.Entries with
        | Some e -> e.ContentHash = Some "hash:content:docs/file.md" && e.CacheRelPath = Some "files/fakehex"
        | None   -> false)

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

// ── populateIndex: contributions, discovery SHA cache, 3-strikes escalation ────

// A persistent (in-memory, across calls) fake for the sources/<name>/index.json
// store, so tests can call populateIndex more than once and observe carried-over
// state (SourceHeadSha, ConsecutiveShaCheckFailures) the way the real filesystem
// adapter would.
type private PersistentIndexStore() =
    let store = System.Collections.Generic.Dictionary<string, SourceIndex>()
    member _.Read (name: string) : Result<SourceIndex option, string> =
        match store.TryGetValue name with
        | true, idx -> Ok (Some idx)
        | false, _  -> Ok None
    member _.Write (name: string) (idx: SourceIndex) : Result<unit, string> =
        store[name] <- idx
        Ok ()
    member _.TryGet (name: string) : SourceIndex option =
        match store.TryGetValue name with
        | true, idx -> Some idx
        | false, _  -> None

let private makePopulateDeps
    (globalCfg: GlobalConfig option)
    (fetch: string -> string -> string list -> Result<(string * string) list, string>)
    (listFiles: string -> string option -> string option -> Result<string list, string>)
    (getRemoteHeadSha: string -> string option -> Result<string, string>)
    (store: PersistentIndexStore) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok globalCfg
        ReadLocalConfig         = fun () -> Ok None
        ReadEnvConfig         = fun () -> Ok None
        WriteLocalConfig        = fun _ -> Ok ()
        WriteGlobalConfig       = fun _ -> Ok ()
        ReadLockEntries         = fun _ -> Ok []
        WriteLockEntries        = fun _ _ -> Ok ()
        FetchRemoteContent      = fetch
        ListRemoteTopLevel      = fun _ _ -> Ok []
        ListRemoteFiles         = listFiles
        WriteLocalFile          = fun _ _ -> Ok ()
        ReadLocalFile           = fun _ -> Ok None
        DeleteLocalFile         = fun _ -> Ok ()
        HashContent             = fun s -> $"hash:{s}"
        GetCwd                  = fun () -> "/tmp"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok None
        WriteLocalManifest      = fun _ -> Ok ()
        ResolveLocalGlob        = fun _ -> []
        ReadSourceIndex         = store.Read
        WriteSourceIndex        = store.Write
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        ParseYamlBlock          = Eru.Adapters.YamlAdapter.parse
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fun _ -> []
        GetRemoteHeadSha        = getRemoteHeadSha
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

[<Fact>]
let ``populateIndex staleness regression - a dropped frontmatter tag does not survive a sync`` () =
    // The source publishes one collection file. First sync sees tags [a; b];
    // second sync sees the same file with tag "b" dropped from its frontmatter.
    let source = makeSource "kb" "https://example.com/kb.git"
    let file : CollectionFileRef = { Source = "kb"; RemotePath = "adr.md"; Tags = []; Description = None }
    let col : CollectionConfig = { Name = "col"; Tags = []; Files = [ file ]; Description = None }
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) }; Collection = Some { CollectionSection.empty with Items = Some ([ col ]) } }

    let currentTags = ref "[a, b]"
    let fetch _ _ (paths: string list) =
        Ok (paths |> List.map (fun p -> p, $"---\ntags: {currentTags.Value}\n---\n"))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch (fun _ _ _ -> Ok []) (fun _ _ -> Error "no okf bundles") store

    Sync.populateIndex deps |> ignore
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx ->
        Assert.Equal<string list>(["a"; "b"], (Map.find "adr.md" idx.Entries).Tags)

    currentTags.Value <- "[a]"
    Sync.populateIndex deps |> ignore
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx ->
        Assert.Equal<string list>(["a"], (Map.find "adr.md" idx.Entries).Tags)

[<Fact>]
let ``populateIndex preserves SourceHeadSha across a collection-file-only write`` () =
    // A source with one Okf bundle (discovery sets SourceHeadSha) and one collection
    // file (Step 2 must not clobber SourceHeadSha when it rewrites the index).
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let file : CollectionFileRef = { Source = "kb"; RemotePath = "notes.md"; Tags = []; Description = None }
    let col : CollectionConfig = { Name = "col"; Tags = []; Files = [ file ]; Description = None }
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) }; Collection = Some { CollectionSection.empty with Items = Some ([ col ]) } }

    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntags: [x]\n---\n"))
    let listFiles _ _ _ = Ok [ "adr.md" ]
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    Sync.populateIndex deps |> ignore
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx -> Assert.Equal(Some "sha-1", idx.SourceHeadSha)

[<Fact>]
let ``populateIndex skips re-walking an Okf bundle when the remote SHA is unchanged`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let listCalls = ref 0
    let listFiles _ _ _ = listCalls.Value <- listCalls.Value + 1; Ok [ "adr.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntype: note\ntags: [x]\n---\n"))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    Sync.populateIndex deps |> ignore
    Assert.Equal(1, listCalls.Value)

    Sync.populateIndex deps |> ignore
    Assert.Equal(1, listCalls.Value)

[<Fact>]
let ``populateIndex re-walks an Okf bundle when the SHA is unchanged but the index has no entries`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let listCalls = ref 0
    let listFiles _ _ _ = listCalls.Value <- listCalls.Value + 1; Ok [ "adr.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntype: note\n---\n"))
    let store = PersistentIndexStore()
    store.Write "kb" { Version = 1; SourceHeadSha = Some "sha-1"; ConsecutiveShaCheckFailures = 0; Entries = Map.empty; WrittenBy = None } |> ignore
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    Sync.populateIndex deps |> ignore
    Assert.Equal(1, listCalls.Value)
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx -> Assert.Equal(1, idx.Entries.Count)

[<Fact>]
let ``populateIndex re-walks an Okf bundle when the remote SHA changes`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let listCalls = ref 0
    let listFiles _ _ _ = listCalls.Value <- listCalls.Value + 1; Ok [ "adr.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntags: [x]\n---\n"))
    let currentSha = ref "sha-1"
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok currentSha.Value) store

    Sync.populateIndex deps |> ignore
    Assert.Equal(1, listCalls.Value)

    currentSha.Value <- "sha-2"
    Sync.populateIndex deps |> ignore
    Assert.Equal(2, listCalls.Value)
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx -> Assert.Equal(Some "sha-2", idx.SourceHeadSha)

[<Fact>]
let ``populateIndex fails open on a SHA check failure and only escalates after 3 in a row`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let listFiles _ _ _ = Ok [ "adr.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntags: [x]\n---\n"))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Error "network down") store

    let errors1 = Sync.populateIndex deps
    Assert.DoesNotContain(errors1, fun e -> e.Contains "3 times in a row")
    match store.TryGet "kb" with
    | Some idx -> Assert.Equal(1, idx.ConsecutiveShaCheckFailures)
    | None -> Assert.Fail "expected an index to have been written"

    let errors2 = Sync.populateIndex deps
    Assert.DoesNotContain(errors2, fun e -> e.Contains "3 times in a row")
    match store.TryGet "kb" with
    | Some idx -> Assert.Equal(2, idx.ConsecutiveShaCheckFailures)
    | None -> Assert.Fail "expected an index to have been written"

    let errors3 = Sync.populateIndex deps
    Assert.Contains(errors3, fun e -> e.Contains "kb" && e.Contains "3 times in a row")
    match store.TryGet "kb" with
    | Some idx -> Assert.Equal(3, idx.ConsecutiveShaCheckFailures)
    | None -> Assert.Fail "expected an index to have been written"

[<Fact>]
let ``populateIndex resets the failure counter after a subsequent successful SHA check`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let listFiles _ _ _ = Ok [ "adr.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntags: [x]\n---\n"))
    let shaResult = ref (Error "network down" : Result<string, string>)
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> shaResult.Value) store

    Sync.populateIndex deps |> ignore
    match store.TryGet "kb" with
    | Some idx -> Assert.Equal(1, idx.ConsecutiveShaCheckFailures)
    | None -> Assert.Fail "expected an index to have been written"

    shaResult.Value <- Ok "sha-1"
    Sync.populateIndex deps |> ignore
    match store.TryGet "kb" with
    | Some idx -> Assert.Equal(0, idx.ConsecutiveShaCheckFailures)
    | None -> Assert.Fail "expected an index to have been written"

[<Fact>]
let ``populateIndex reports bundle discovery failure immediately and does not persist SourceHeadSha`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let listCalls = ref 0
    let listFiles _ _ _ = listCalls.Value <- listCalls.Value + 1; Error "clone failed"
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntags: [x]\n---\n"))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    let errors = Sync.populateIndex deps
    Assert.Single(errors) |> ignore
    Assert.Contains("clone failed", errors.Head)
    match store.TryGet "kb" with
    | Some idx -> Assert.Equal(None, idx.SourceHeadSha)
    | None -> ()

    // Same SHA on the next run must retry discovery rather than skip.
    Sync.populateIndex deps |> ignore
    Assert.Equal(2, listCalls.Value)

let private okfGlobal (bundles: Bundle list) : GlobalConfig =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" bundles
    { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

let private typedConcept = "---\ntype: explanation\ntags: [x]\n---\n"

[<Fact>]
let ``populateIndex keeps discovered Okf entries when the remote SHA is unchanged`` () =
    // Regression: the manifest reseed used to wipe Entries every sync while the SHA
    // gate skipped rediscovery, leaving an empty index from the second sync on.
    let g = okfGlobal [ { Path = ""; Kind = Okf } ]
    let listCalls = ref 0
    let listFiles _ _ _ = listCalls.Value <- listCalls.Value + 1; Ok [ "a.md"; "b.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, typedConcept))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    Sync.populateIndex deps |> ignore
    Sync.populateIndex deps |> ignore
    Sync.populateIndex deps |> ignore

    Assert.Equal(1, listCalls.Value)
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx ->
        Assert.Equal<string list>([ "a.md"; "b.md" ], idx.Entries |> Map.toList |> List.map fst)
        Assert.All(idx.Entries |> Map.toList, fun (_, e) ->
            Assert.True e.ContentHash.IsSome
            Assert.Equal(Some "explanation", e.Type))
        Assert.Equal(Some "sha-1", idx.SourceHeadSha)

[<Fact>]
let ``populateIndex drops discovered entries for files removed upstream when the SHA changes`` () =
    let g = okfGlobal [ { Path = ""; Kind = Okf } ]
    let files = ref [ "a.md"; "b.md" ]
    let sha = ref "sha-1"
    let listFiles _ _ _ = Ok files.Value
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, typedConcept))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok sha.Value) store

    Sync.populateIndex deps |> ignore
    files.Value <- [ "a.md" ]
    sha.Value <- "sha-2"
    Sync.populateIndex deps |> ignore

    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx -> Assert.Equal<string list>([ "a.md" ], idx.Entries |> Map.toList |> List.map fst)

[<Fact>]
let ``populateIndex keeps previously discovered entries when a later re-walk fails`` () =
    let g = okfGlobal [ { Path = ""; Kind = Okf } ]
    let walk = ref (Ok [ "a.md"; "b.md" ] : Result<string list, string>)
    let sha = ref "sha-1"
    let listFiles _ _ _ = walk.Value
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, typedConcept))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok sha.Value) store

    Sync.populateIndex deps |> ignore
    walk.Value <- Error "clone failed"
    sha.Value <- "sha-2"
    let errors = Sync.populateIndex deps

    Assert.Single(errors) |> ignore
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx ->
        Assert.Equal<string list>([ "a.md"; "b.md" ], idx.Entries |> Map.toList |> List.map fst)
        // SHA stays at the last successful walk so the next sync retries.
        Assert.Equal(Some "sha-1", idx.SourceHeadSha)

[<Fact>]
let ``populateIndex keeps manifest-backed entries when an Okf re-walk runs`` () =
    // A path covered by a Manifest bundle keeps its manifest contribution after the
    // Okf walk refreshes (and prunes) discovered entries.
    let docsManifest : SourceManifest =
        { Version = 1
          Description = None
          Files = [ { Path = "m.md"; Tags = [ "mtag" ]; Description = Some "from manifest" } ] }
    let g = okfGlobal [ { Path = ""; Kind = Okf }; { Path = "docs"; Kind = Manifest } ]
    let files = ref [ "a.md" ]
    let sha = ref "sha-1"
    let listFiles _ _ _ = Ok files.Value
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, typedConcept))
    let store = PersistentIndexStore()
    let deps =
        { makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok sha.Value) store with
            ReadCachedManifest = fun key ->
                if key.EndsWith "docs" then Ok (Some docsManifest) else Ok None }

    Sync.populateIndex deps |> ignore
    sha.Value <- "sha-2"
    Sync.populateIndex deps |> ignore

    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx ->
        Assert.True(Map.containsKey "a.md" idx.Entries)
        match Map.tryFind "docs/m.md" idx.Entries with
        | None -> Assert.Fail "expected the manifest-backed entry to survive"
        | Some e -> Assert.Contains("mtag", e.Tags)

[<Fact>]
let ``execute surfaces bundle discovery failures in SyncResult.Errors`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) (fun _ _ _ -> Ok []) (fun _ _ _ -> Error "clone failed") (fun _ _ -> Ok "sha-1") store

    match Sync.execute deps { DryRun = false } with
    | Ok r -> Assert.Contains(r.Errors, fun e -> e.Contains "clone failed")
    | Error e -> Assert.Fail e

[<Fact>]
let ``execute reports the index size per source, stable across repeated syncs`` () =
    let g = okfGlobal [ { Path = ""; Kind = Okf } ]
    let listFiles _ _ _ = Ok [ "a.md"; "b.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, typedConcept))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    for _ in 1 .. 2 do
        match Sync.execute deps { DryRun = false } with
        | Error e -> Assert.Fail e
        | Ok r ->
            let summary = Assert.Single r.Indexes
            Assert.Equal("kb", summary.Source)
            Assert.Equal(2, summary.Entries)
            Assert.Equal<string list>([ "okf:/" ], summary.Bundles)
            Assert.Equal(None, Sync.indexWarning summary)

[<Fact>]
let ``indexWarning flags a source that indexed nothing and says why`` () =
    let withBundles = Sync.indexWarning { Source = "norms"; Entries = 0; Bundles = [ "okf:/" ] }
    Assert.True(withBundles.IsSome)
    Assert.Contains("norms", withBundles.Value)
    Assert.Contains("okf:/", withBundles.Value)
    Assert.Contains("type", withBundles.Value)

    let noBundles = Sync.indexWarning { Source = "kb"; Entries = 0; Bundles = [] }
    Assert.True(noBundles.IsSome)
    Assert.Contains("no bundles registered", noBundles.Value)

    Assert.Equal(None, Sync.indexWarning { Source = "kb"; Entries = 3; Bundles = [] })

[<Fact>]
let ``execute reports a zero-entry index for a source with no bundles`` () =
    let g = okfGlobal []
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) (fun _ _ _ -> Ok []) (fun _ _ _ -> Ok []) (fun _ _ -> Ok "sha-1") store

    match Sync.execute deps { DryRun = false } with
    | Error e -> Assert.Fail e
    | Ok r ->
        let summary = Assert.Single r.Indexes
        Assert.Equal(0, summary.Entries)
        Assert.True((Sync.indexWarning summary).IsSome)

[<Fact>]
let ``populateIndex walks a nested Okf bundle only once when a root bundle already covers it`` () =
    let source =
        makeSourceWithBundles "kb" "https://example.com/kb.git"
            [ { Path = "sub"; Kind = Okf }; { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some ([ source ]) } }

    let walkedPaths = ResizeArray<string option>()
    let listFiles _ _ (path: string option) =
        walkedPaths.Add path
        match path with
        | None -> Ok [ "top.md"; "sub/inner.md" ]
        | Some _ -> Ok [ "inner.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntype: note\n---\n"))
    let store = PersistentIndexStore()
    let deps = makePopulateDeps (Some g) fetch listFiles (fun _ _ -> Ok "sha-1") store

    Sync.populateIndex deps |> ignore

    Assert.Equal<string option list>([ None ], List.ofSeq walkedPaths)
    match store.TryGet "kb" with
    | None -> Assert.Fail "expected an index to have been written"
    | Some idx ->
        Assert.True(Map.containsKey "top.md" idx.Entries)
        Assert.True(Map.containsKey "sub/inner.md" idx.Entries)

// ── Folder sources: a plain local directory, read in place ─────────────────────

type private FakeFolder() =
    member val Files : Map<string, string * string> = Map.empty with get, set   // path -> (fingerprint, content)
    member val ScanError : string option = None with get, set
    member val Reads : string list = [] with get, set
    member val Cached : int = 0 with get, set

let private folderDeps (folder: FakeFolder) (store: PersistentIndexStore) : Deps =
    let g = makeGlobal [ makeSourceWithBundles "brain" "/notes/brain" [ { Path = ""; Kind = Okf } ] ]
    let fail _ = Error "git must not be used for a folder source"
    let baseDeps = makePopulateDeps (Some g) (fun _ _ _ -> fail ()) (fun _ _ _ -> fail ()) (fun _ _ -> fail ()) store
    { baseDeps with
        IsPlainFolder = fun p -> p = "/notes/brain"
        ScanLocalFolder = fun _ _ _ ->
            match folder.ScanError with
            | Some e -> Error e
            | None -> Ok (folder.Files |> Map.toList |> List.map (fun (p, (fp, _)) -> p, fp))
        ReadLocalFile = fun path ->
            folder.Reads <- path :: folder.Reads
            let rel = path.Substring("/notes/brain/".Length)
            match Map.tryFind rel folder.Files with
            | Some (_, content) -> Ok (Some content)
            | None -> Ok None
        CacheSourceContent = fun _ _ _ -> folder.Cached <- folder.Cached + 1; Ok "files/x" }

let private note title = $"---\ntype: note\ntitle: {title}\n---\nbody"

[<Fact>]
let ``folder source is indexed in place, with no cache copy`` () =
    let folder = FakeFolder()
    folder.Files <- Map.ofList [ "a.md", ("1:10", note "A"); "sub/b.md", ("1:20", note "B") ]
    let store = PersistentIndexStore()
    let deps = folderDeps folder store
    Sync.populateIndex deps |> ignore
    let idx = (store.TryGet "brain").Value
    Assert.Equal(2, idx.Entries.Count)
    let a = idx.Entries["a.md"]
    Assert.Equal(Some "/notes/brain/a.md", a.SourcePath)
    Assert.Equal(Some "1:10", a.Fingerprint)
    Assert.Equal(Some "A", a.Title)
    Assert.True(a.CacheRelPath.IsNone)
    Assert.Equal(0, folder.Cached)

[<Fact>]
let ``folder source re-sync reads only files whose fingerprint changed`` () =
    let folder = FakeFolder()
    folder.Files <- Map.ofList [ "a.md", ("1:10", note "A"); "b.md", ("1:20", note "B") ]
    let store = PersistentIndexStore()
    let deps = folderDeps folder store
    Sync.populateIndex deps |> ignore
    Assert.Equal(2, folder.Reads.Length)

    folder.Reads <- []
    Sync.populateIndex deps |> ignore
    Assert.Empty(folder.Reads)

    folder.Files <- folder.Files |> Map.add "a.md" ("2:11", note "A2")
    Sync.populateIndex deps |> ignore
    Assert.Equal<string list>([ "/notes/brain/a.md" ], folder.Reads)
    Assert.Equal(Some "A2", (store.TryGet "brain").Value.Entries["a.md"].Title)

[<Fact>]
let ``folder source drops entries for files that were removed`` () =
    let folder = FakeFolder()
    folder.Files <- Map.ofList [ "a.md", ("1:10", note "A"); "b.md", ("1:20", note "B") ]
    let store = PersistentIndexStore()
    let deps = folderDeps folder store
    Sync.populateIndex deps |> ignore
    folder.Files <- folder.Files |> Map.remove "b.md"
    Sync.populateIndex deps |> ignore
    let idx = (store.TryGet "brain").Value
    Assert.True(Map.containsKey "a.md" idx.Entries)
    Assert.False(Map.containsKey "b.md" idx.Entries)

[<Fact>]
let ``folder source keeps its index and reports an error when the scan fails`` () =
    let folder = FakeFolder()
    folder.Files <- Map.ofList [ "a.md", ("1:10", note "A") ]
    let store = PersistentIndexStore()
    let deps = folderDeps folder store
    Sync.populateIndex deps |> ignore
    folder.ScanError <- Some "folder not found"
    let errors = Sync.populateIndex deps
    Assert.True(Map.containsKey "a.md" (store.TryGet "brain").Value.Entries)
    Assert.Contains(errors, fun e -> e.Contains "folder not found")

[<Fact>]
let ``folder source does not publish untyped concept files`` () =
    let folder = FakeFolder()
    folder.Files <- Map.ofList [ "typed.md", ("1:1", note "T"); "untyped.md", ("1:2", "just text") ]
    let store = PersistentIndexStore()
    Sync.populateIndex (folderDeps folder store) |> ignore
    let idx = (store.TryGet "brain").Value
    Assert.True(Map.containsKey "typed.md" idx.Entries)
    Assert.False(Map.containsKey "untyped.md" idx.Entries)

[<Fact>]
let ``FolderAdapter scan is stat-only, md-only, skips dot-dirs and ignored directories`` () =
    let root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eru-folder-" + System.Guid.NewGuid().ToString("N"))
    try
        let write (rel: string) =
            let p = System.IO.Path.Combine(root, rel)
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName p) |> ignore
            System.IO.File.WriteAllText(p, "x")
        write "a.md"; write "notes/b.md"; write "notes/c.txt"; write ".hidden/d.md"; write "node_modules/e.md"; write "inbox/f.md"; write "other/g.md"
        let all = Eru.Adapters.FolderAdapter.scan root "" Config.defaultOkfIgnorePatterns |> Result.defaultValue []
        Assert.Equal<string list>([ "a.md"; "notes/b.md"; "other/g.md" ], all |> List.map fst)
        let sub = Eru.Adapters.FolderAdapter.scan root "notes" Config.defaultOkfIgnorePatterns |> Result.defaultValue []
        Assert.Equal<string list>([ "notes/b.md" ], sub |> List.map fst)
        Assert.True((Eru.Adapters.FolderAdapter.scan root "missing" []) |> Result.isError)
        Assert.True(Eru.Adapters.FolderAdapter.isPlainFolder root)
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, ".git")) |> ignore
        Assert.False(Eru.Adapters.FolderAdapter.isPlainFolder root)
    finally
        try System.IO.Directory.Delete(root, true) with _ -> ()

[<Fact>]
let ``populateIndex reports a stale writer once per source and stops writing that source`` () =
    let sources =
        [ makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
          makeSourceWithBundles "other" "https://example.com/other.git" [ { Path = ""; Kind = Okf } ] ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some sources } }
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntype: note\n---\n"))
    let store = PersistentIndexStore()
    let attempts = System.Collections.Generic.Dictionary<string, int>()
    let refusing =
        { makePopulateDeps (Some g) fetch (fun _ _ _ -> Ok [ "adr.md" ]) (fun _ _ -> Ok "sha-1") store with
            WriteSourceIndex = fun name _ ->
                attempts[name] <- (match attempts.TryGetValue name with | true, n -> n + 1 | _ -> 1)
                Error (StaleWriter.message "0.99.0" "0.15.1") }

    let errors = Sync.populateIndex refusing

    Assert.True(Sync.hasStaleWriter errors)
    Assert.Equal(1, errors |> List.filter (fun e -> e.Contains "source 'kb'" && e.Contains StaleWriter.prefix) |> List.length)
    Assert.Equal(1, errors |> List.filter (fun e -> e.Contains "source 'other'" && e.Contains StaleWriter.prefix) |> List.length)
    Assert.Equal(1, attempts["kb"])      // later writes in the same run are skipped
    Assert.Equal(1, attempts["other"])

[<Fact>]
let ``a stale writer cannot replace a newer populated index with an empty one`` () =
    // Real-adapter shape of the 710 -> 0 incident, using the same refusal rule the adapter applies.
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let g : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some [ source ] } }
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntype: note\n---\n"))
    let store = PersistentIndexStore()
    let populated =
        { Version = 1; SourceHeadSha = Some "sha-1"; ConsecutiveShaCheckFailures = 0; WrittenBy = Some "0.99.0"
          Entries = Map.ofList [ "adr.md", IndexEntry.empty ] }
    store.Write "kb" populated |> ignore
    let guarded =
        let baseDeps = makePopulateDeps (Some g) fetch (fun _ _ _ -> Error "network down") (fun _ _ -> Error "network down") store
        { baseDeps with
            WriteSourceIndex = fun name idx ->
                match store.TryGet name with
                | Some onDisk when onDisk.WrittenBy.IsSome && SemVer.isNewer onDisk.WrittenBy.Value "0.15.1" ->
                    Error (StaleWriter.message onDisk.WrittenBy.Value "0.15.1")
                | _ -> store.Write name idx }

    Sync.populateIndex guarded |> ignore

    Assert.Equal(populated, store.TryGet "kb" |> Option.get)

// ── KnowledgeSyncService: one background sync across processes ──────────────────

open Eru.Search

type private FakeGate(lastSync: System.DateTimeOffset option, canAcquire: bool) =
    let mutable disposed = 0
    let mutable marked = 0
    let now = System.DateTimeOffset(2026, 11, 7, 12, 0, 0, System.TimeSpan.Zero)
    member _.Disposed = System.Threading.Volatile.Read(&disposed)
    member _.Marked = System.Threading.Volatile.Read(&marked)
    member _.Now = now
    member this.Gate : SyncGate =
        { TryAcquire = fun () ->
            if canAcquire then
                Some { new System.IDisposable with member _.Dispose() = System.Threading.Interlocked.Increment(&disposed) |> ignore }
            else None
          LastSync   = fun () -> lastSync
          MarkSynced = fun () -> System.Threading.Interlocked.Increment(&marked) |> ignore
          Now        = fun () -> now }

let private syncService (deps: Deps) (gate: SyncGate) =
    KnowledgeSyncService(deps, EffectiveConfig.empty, gate, Microsoft.Extensions.Logging.Abstractions.NullLogger<KnowledgeSyncService>.Instance)

let private plainDeps () =
    makePopulateDeps None (fun _ _ _ -> Ok []) (fun _ _ _ -> Ok []) (fun _ _ -> Ok "sha") (PersistentIndexStore())

let private waitFor (cond: unit -> bool) =
    let deadline = System.DateTime.UtcNow.AddSeconds 5.0
    while not (cond ()) && System.DateTime.UtcNow < deadline do System.Threading.Thread.Sleep 10

[<Fact>]
let ``a background sync runs, marks the sync time and releases the lock`` () =
    let g = FakeGate(None, true)
    let svc = syncService (plainDeps ()) g.Gate
    Assert.True(svc.TriggerBackgroundSync false)
    waitFor (fun () -> g.Disposed = 1)
    Assert.Equal(1, g.Marked)
    Assert.Equal(1, g.Disposed)

[<Fact>]
let ``a background sync is skipped while another process holds the lock`` () =
    let g = FakeGate(None, false)
    let svc = syncService (plainDeps ()) g.Gate
    Assert.False(svc.TriggerBackgroundSync true)    // even a forced refresh waits for the holder
    Assert.Equal(0, g.Marked)
    // The in-process flag was released, so a later attempt can still run.
    let g2 = FakeGate(None, true)
    Assert.True((syncService (plainDeps ()) g2.Gate).TriggerBackgroundSync false)

[<Fact>]
let ``a timer sync is skipped when another process synced less than half an interval ago`` () =
    let probe = FakeGate(None, true)
    let g = FakeGate(Some (probe.Now - System.TimeSpan.FromMinutes 10.0), true)   // default interval 60 -> window 30
    let svc = syncService (plainDeps ()) g.Gate
    Assert.False(svc.TriggerBackgroundSync false)
    Assert.Equal(1, g.Disposed)                      // the lock is handed back, not leaked
    Assert.Equal(0, g.Marked)

[<Fact>]
let ``a forced refresh ignores the throttle`` () =
    let probe = FakeGate(None, true)
    let g = FakeGate(Some (probe.Now - System.TimeSpan.FromMinutes 1.0), true)
    let svc = syncService (plainDeps ()) g.Gate
    Assert.True(svc.TriggerBackgroundSync true)
    waitFor (fun () -> g.Marked = 1)
    Assert.Equal(1, g.Marked)

[<Fact>]
let ``a timer sync runs once the last sync is older than half an interval`` () =
    let probe = FakeGate(None, true)
    let g = FakeGate(Some (probe.Now - System.TimeSpan.FromMinutes 45.0), true)
    let svc = syncService (plainDeps ()) g.Gate
    Assert.True(svc.TriggerBackgroundSync false)
    waitFor (fun () -> g.Marked = 1)

[<Fact>]
let ``a service that finds a newer index becomes stale and stops syncing`` () =
    let source = makeSourceWithBundles "kb" "https://example.com/kb.git" [ { Path = ""; Kind = Okf } ]
    let cfg : GlobalConfig = { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some [ source ] } }
    let baseDeps = makePopulateDeps (Some cfg) (fun _ _ _ -> Ok []) (fun _ _ _ -> Ok []) (fun _ _ -> Ok "sha") (PersistentIndexStore())
    let refusing = { baseDeps with WriteSourceIndex = fun _ _ -> Error (StaleWriter.message "0.99.0" "0.15.1") }
    let g = FakeGate(None, true)
    let svc = syncService refusing g.Gate
    Assert.True(svc.TriggerBackgroundSync true)
    waitFor (fun () -> svc.IsStale)
    Assert.True svc.IsStale
    waitFor (fun () -> g.Disposed = 1)
    Assert.Equal(0, g.Marked)                        // a stale sync is not recorded as done
    Assert.False(svc.TriggerBackgroundSync true)     // and no further sync starts

// ── Idle pause ───────────────────────────────────────────────────────────────

type private Clock(start: System.DateTimeOffset) =
    let mutable now = start
    member _.Now with get () = now and set v = now <- v
    member this.Advance (m: float) = now <- now + System.TimeSpan.FromMinutes m

let private idleService (idleMinutes: int) (clock: Clock) (marked: int ref) =
    let gate : SyncGate =
        { TryAcquire = fun () -> Some { new System.IDisposable with member _.Dispose() = () }
          LastSync   = fun () -> None
          MarkSynced = fun () -> System.Threading.Interlocked.Increment(&marked.contents) |> ignore
          Now        = fun () -> clock.Now }
    let eff = { EffectiveConfig.empty with McpIdleTimeoutMinutes = idleMinutes }
    KnowledgeSyncService(plainDeps (), eff, gate, Microsoft.Extensions.Logging.Abstractions.NullLogger<KnowledgeSyncService>.Instance)

[<Fact>]
let ``a server is idle only after the timeout with no requests`` () =
    let clock = Clock(System.DateTimeOffset(2026, 11, 7, 8, 0, 0, System.TimeSpan.Zero))
    let svc = idleService 30 clock (ref 0)
    Assert.False svc.IsIdle
    clock.Advance 29.0
    Assert.False svc.IsIdle
    clock.Advance 2.0
    Assert.True svc.IsIdle

[<Fact>]
let ``a request resets the idle clock`` () =
    let clock = Clock(System.DateTimeOffset(2026, 11, 7, 8, 0, 0, System.TimeSpan.Zero))
    let svc = idleService 30 clock (ref 0)
    clock.Advance 29.0
    svc.NoteActivity()
    clock.Advance 29.0
    Assert.False svc.IsIdle

[<Fact>]
let ``an idle timeout of 0 never pauses`` () =
    let clock = Clock(System.DateTimeOffset(2026, 11, 7, 8, 0, 0, System.TimeSpan.Zero))
    let svc = idleService 0 clock (ref 0)
    clock.Advance (60.0 * 24.0 * 30.0)
    Assert.False svc.IsIdle

[<Fact>]
let ``the first request after an idle spell resumes syncing`` () =
    let clock = Clock(System.DateTimeOffset(2026, 11, 7, 8, 0, 0, System.TimeSpan.Zero))
    let marked = ref 0
    let svc = idleService 30 clock marked
    clock.Advance 120.0
    Assert.True svc.IsIdle
    svc.NoteActivity()
    waitFor (fun () -> marked.Value = 1)
    Assert.Equal(1, marked.Value)
    Assert.False svc.IsIdle

[<Fact>]
let ``a request while active does not trigger a sync`` () =
    let clock = Clock(System.DateTimeOffset(2026, 11, 7, 8, 0, 0, System.TimeSpan.Zero))
    let marked = ref 0
    let svc = idleService 30 clock marked
    clock.Advance 5.0
    svc.NoteActivity()
    System.Threading.Thread.Sleep 100
    Assert.Equal(0, marked.Value)
