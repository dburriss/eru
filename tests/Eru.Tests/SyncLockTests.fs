module Eru.Tests.SyncLockTests

open System
open System.IO
open Xunit
open Eru.Adapters

let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "eru-lock-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir finally try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``the lock is exclusive until it is released`` () =
    withTempDir (fun dir ->
        let path = Path.Combine(dir, "nested", "sync.lock")
        let first = SyncLock.tryAcquireAt path
        Assert.True first.IsSome
        Assert.True((SyncLock.tryAcquireAt path).IsNone)
        first.Value.Dispose()
        let again = SyncLock.tryAcquireAt path
        Assert.True again.IsSome
        again.Value.Dispose())

[<Fact>]
let ``acquireWaitingAt gives up after the timeout while the lock is held`` () =
    withTempDir (fun dir ->
        let path = Path.Combine(dir, "sync.lock")
        use _held = (SyncLock.tryAcquireAt path).Value
        Assert.True((SyncLock.acquireWaitingAt path (TimeSpan.FromMilliseconds 300.0)).IsNone))

[<Fact>]
let ``acquireWaitingAt succeeds once the holder releases`` () =
    withTempDir (fun dir ->
        let path = Path.Combine(dir, "sync.lock")
        let held = (SyncLock.tryAcquireAt path).Value
        let release = Threading.Tasks.Task.Run(fun () -> Threading.Thread.Sleep 300; held.Dispose())
        let got = SyncLock.acquireWaitingAt path (TimeSpan.FromSeconds 5.0)
        release.Wait()
        Assert.True got.IsSome
        got.Value.Dispose())

[<Fact>]
let ``markSyncedAt records a time that lastSyncAt reads back`` () =
    withTempDir (fun dir ->
        let path = Path.Combine(dir, "last-sync")
        Assert.True((SyncLock.lastSyncAt path).IsNone)
        SyncLock.markSyncedAt path
        match SyncLock.lastSyncAt path with
        | Some t -> Assert.True(DateTimeOffset.UtcNow - t < TimeSpan.FromMinutes 1.0)
        | None -> Assert.Fail "expected a last-sync time")
