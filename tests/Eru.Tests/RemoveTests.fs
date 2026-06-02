module Eru.Tests.RemoveTests

open Xunit
open Eru

let private entry localPath sourceName remotePath hash : LockEntry =
    { LocalPath = localPath; SourceName = sourceName; RemotePath = remotePath; ContentHash = hash
      Tags = []; Description = None }

let private makeDeps
    (lockEntries: LockEntry list)
    (writeLockResult: Result<unit, string>)
    (deleteResult: Result<unit, string>)
    (capturedLock: LockEntry list ref)
    (capturedDeletePath: string option ref) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok None
        ReadLocalConfig    = fun () -> Ok None
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok lockEntries
        WriteLockEntries   = fun _ entries -> capturedLock.Value <- entries; writeLockResult
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
        DeleteLocalFile     = fun path -> capturedDeletePath.Value <- Some path; deleteResult
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/repo"
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

let private cmd target dryrun : Remove.Command = { Target = target; DryRun = dryrun }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

[<Fact>]
let ``returns error when path is not in lock file`` () =
    let lock     = ref []
    let deleted  = ref None
    let entries  = [ entry "knowledge/other.md" "src" "docs/other.md" "sha256:abc" ]
    let deps     = makeDeps entries (Ok ()) (Ok ()) lock deleted
    let result   = Remove.execute deps (cmd "knowledge/missing.md" false)
    assertError result
    match result with
    | Error msg -> Assert.Contains("did not match", msg)
    | Ok _ -> ()

[<Fact>]
let ``dryrun returns message without writing lock or deleting file`` () =
    let lock     = ref []
    let deleted  = ref None
    let entries  = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:abc" ]
    let deps     = makeDeps entries (Ok ()) (Ok ()) lock deleted
    let result   = Remove.execute deps (cmd "knowledge/adr.md" true)
    assertOk result
    match result with
    | Ok msg -> Assert.Contains("Would remove", msg)
    | Error _ -> ()
    Assert.Empty(!lock)
    Assert.True(deleted.Value.IsNone)

[<Fact>]
let ``removes entry from lock and deletes file`` () =
    let lock     = ref []
    let deleted  = ref None
    let entries  = [
        entry "knowledge/adr.md"   "src" "docs/adr.md"   "sha256:aaa"
        entry "knowledge/guide.md" "src" "docs/guide.md" "sha256:bbb"
    ]
    let deps = makeDeps entries (Ok ()) (Ok ()) lock deleted
    assertOk (Remove.execute deps (cmd "knowledge/adr.md" false))
    Assert.Equal(1, lock.Value.Length)
    Assert.Equal("knowledge/guide.md", lock.Value.[0].LocalPath)
    Assert.Equal(Some "/repo/knowledge/adr.md", deleted.Value)

[<Fact>]
let ``propagates error when WriteLockEntries fails`` () =
    let lock     = ref []
    let deleted  = ref None
    let entries  = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:abc" ]
    let deps     = makeDeps entries (Error "disk full") (Ok ()) lock deleted
    assertError (Remove.execute deps (cmd "knowledge/adr.md" false))
    Assert.True(deleted.Value.IsNone)

[<Fact>]
let ``returns error with context when DeleteLocalFile fails after lock write`` () =
    let lock     = ref []
    let deleted  = ref None
    let entries  = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:abc" ]
    let deps     = makeDeps entries (Ok ()) (Error "permission denied") lock deleted
    let result   = Remove.execute deps (cmd "knowledge/adr.md" false)
    assertError result
    match result with
    | Error msg -> Assert.Contains("could not delete file", msg)
    | Ok _ -> ()
    Assert.Equal(Some "/repo/knowledge/adr.md", deleted.Value)

[<Fact>]
let ``removes entry by short hash of remote path`` () =
    let lock    = ref []
    let deleted = ref None
    let entries = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:aaa" ]
    let hash    = Patterns.pathShortHash "docs/adr.md"
    let deps    = makeDeps entries (Ok ()) (Ok ()) lock deleted
    assertOk (Remove.execute deps (cmd hash false))
    Assert.Empty(lock.Value)
    Assert.Equal(Some "/repo/knowledge/adr.md", deleted.Value)

[<Fact>]
let ``resolves entry uniquely when full hash is given`` () =
    let lock    = ref []
    let deleted = ref None
    let hash1   = Patterns.pathShortHash "docs/adr.md"
    let hash2   = Patterns.pathShortHash "docs/guide.md"
    let entries = [
        entry "knowledge/adr.md"   "src" "docs/adr.md"   "sha256:aaa"
        entry "knowledge/guide.md" "src" "docs/guide.md" "sha256:bbb"
    ]
    let deps = makeDeps entries (Ok ()) (Ok ()) lock deleted
    assertOk (Remove.execute deps (cmd hash1 true))
    assertOk (Remove.execute deps (cmd hash2 true))

[<Fact>]
let ``returns error when target matches multiple entries`` () =
    let lock    = ref []
    let deleted = ref None
    // craft an entry whose local path IS the remote hash prefix of another, causing a cross-match
    let hash1   = Patterns.pathShortHash "docs/adr.md"
    let entries = [
        entry "knowledge/adr.md"  "src" "docs/adr.md"  "sha256:aaa"
        entry hash1               "src" "docs/adr2.md" "sha256:bbb"  // local path == hash of first entry's remote path
    ]
    let deps   = makeDeps entries (Ok ()) (Ok ()) lock deleted
    let result = Remove.execute deps (cmd hash1 false)
    assertError result
    match result with
    | Error msg -> Assert.Contains("matched", msg)
    | Ok _ -> ()

[<Fact>]
let ``returns error when target matches no entry`` () =
    let lock    = ref []
    let deleted = ref None
    let entries = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:abc" ]
    let deps    = makeDeps entries (Ok ()) (Ok ()) lock deleted
    let result  = Remove.execute deps (cmd "00000000" false)
    assertError result
    match result with
    | Error msg -> Assert.Contains("did not match", msg)
    | Ok _ -> ()
