module Eru.Tests.DisconnectTests

open Xunit
open Eru

let private entry localPath sourceName remotePath hash : LockEntry =
    { LocalPath = localPath; SourceName = sourceName; RemotePath = remotePath; ContentHash = hash }

let private makeDeps
    (lockEntries: LockEntry list)
    (writeLockResult: Result<unit, string>)
    (capturedLock: LockEntry list ref) : Deps =
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
        DeleteLocalFile     = fun _ -> Assert.Fail("disconnect must not delete files"); Error "unexpected"
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/repo"
        ReadCachedManifest  = fun _ -> Ok None
        CacheSourceManifest = fun _ _ -> Ok ()
        ReadLocalManifest   = fun () -> Ok None
        WriteLocalManifest  = fun _ -> Ok ()
        ResolveLocalGlob    = fun _ -> []
    }

let private cmd target dryrun : Disconnect.Command = { Target = target; DryRun = dryrun }

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

[<Fact>]
let ``returns error when path is not in lock file`` () =
    let lock    = ref []
    let entries = [ entry "knowledge/other.md" "src" "docs/other.md" "sha256:abc" ]
    let deps    = makeDeps entries (Ok ()) lock
    let result  = Disconnect.execute deps (cmd "knowledge/missing.md" false)
    assertError result
    match result with
    | Error msg -> Assert.Contains("did not match", msg)
    | Ok _ -> ()

[<Fact>]
let ``dryrun returns message without writing lock`` () =
    let lock    = ref []
    let entries = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:abc" ]
    let deps    = makeDeps entries (Ok ()) lock
    let result  = Disconnect.execute deps (cmd "knowledge/adr.md" true)
    assertOk result
    match result with
    | Ok msg -> Assert.Contains("Would disconnect", msg)
    | Error _ -> ()
    Assert.Empty(!lock)

[<Fact>]
let ``removes entry from lock without deleting file`` () =
    let lock    = ref []
    let entries = [
        entry "knowledge/adr.md"   "src" "docs/adr.md"   "sha256:aaa"
        entry "knowledge/guide.md" "src" "docs/guide.md" "sha256:bbb"
    ]
    let deps = makeDeps entries (Ok ()) lock
    assertOk (Disconnect.execute deps (cmd "knowledge/adr.md" false))
    Assert.Equal(1, lock.Value.Length)
    Assert.Equal("knowledge/guide.md", lock.Value.[0].LocalPath)

[<Fact>]
let ``propagates error when WriteLockEntries fails`` () =
    let lock    = ref []
    let entries = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:abc" ]
    let deps    = makeDeps entries (Error "disk full") lock
    assertError (Disconnect.execute deps (cmd "knowledge/adr.md" false))

[<Fact>]
let ``removes entry by short hash of remote path`` () =
    let lock    = ref []
    let entries = [ entry "knowledge/adr.md" "src" "docs/adr.md" "sha256:aaa" ]
    let hash    = Patterns.pathShortHash "docs/adr.md"
    let deps    = makeDeps entries (Ok ()) lock
    assertOk (Disconnect.execute deps (cmd hash false))
    Assert.Empty(lock.Value)

[<Fact>]
let ``returns error when target matches multiple entries`` () =
    let lock  = ref []
    let hash1 = Patterns.pathShortHash "docs/adr.md"
    let entries = [
        entry "knowledge/adr.md" "src" "docs/adr.md"  "sha256:aaa"
        entry hash1              "src" "docs/adr2.md" "sha256:bbb"
    ]
    let deps   = makeDeps entries (Ok ()) lock
    let result = Disconnect.execute deps (cmd hash1 false)
    assertError result
    match result with
    | Error msg -> Assert.Contains("matched", msg)
    | Ok _ -> ()
