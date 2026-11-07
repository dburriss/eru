namespace Eru.Adapters

open System
open System.IO
open System.Threading

/// A cross-process "one sync at a time" lock. It is an OS file lock (FileShare.None), so it is released
/// automatically if the holder crashes: unlike a pid file it can never be left stale.
module SyncLock =

    let tryAcquireAt (path: string) : IDisposable option =
        try
            let dir = Path.GetDirectoryName path
            if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory dir |> ignore
            Some (new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None) :> IDisposable)
        with
        | :? IOException | :? UnauthorizedAccessException -> None

    /// Polls for the lock until `timeout` elapses. For `eru sync`, which should wait out a background sync.
    let acquireWaitingAt (path: string) (timeout: TimeSpan) : IDisposable option =
        let deadline = DateTime.UtcNow + timeout
        let rec loop () =
            match tryAcquireAt path with
            | Some l -> Some l
            | None when DateTime.UtcNow >= deadline -> None
            | None -> Thread.Sleep 250; loop ()
        loop ()

    let lastSyncAt (path: string) : DateTimeOffset option =
        try
            if File.Exists path then Some (DateTimeOffset(File.GetLastWriteTimeUtc path, TimeSpan.Zero)) else None
        with _ -> None

    let markSyncedAt (path: string) : unit =
        try
            let dir = Path.GetDirectoryName path
            if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, DateTimeOffset.UtcNow.ToString "o")
        with _ -> ()

    let tryAcquire () = tryAcquireAt (Paths.syncLockPath ())
    let acquireWaiting (timeout: TimeSpan) = acquireWaitingAt (Paths.syncLockPath ()) timeout
    let lastSync () = lastSyncAt (Paths.lastSyncPath ())
    let markSynced () = markSyncedAt (Paths.lastSyncPath ())
