module Eru.Cli.InboxWatchCli

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Argu
open Eru
open Eru.Cli.OutputFormat
open Eru.Cli.InboxProcessCli

type Cmd = {
    Command  : InboxProcess.Options
    // None means "use the configured/default interval" — resolved against
    // Config.EffectiveConfig.InboxWatchIntervalSeconds in `run`, since the
    // active pattern below has no access to `deps`.
    Interval : int option
}

let (|InboxWatchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Inbox args ->
            args.TryGetSubCommand() |> Option.bind (function
                | InboxArgs.Watch watchArgs ->
                    Some {
                        Command = {
                            InboxProcess.Options.InboxName = watchArgs.TryGetResult InboxWatchArgs.Inbox
                            InboxProcess.Options.Channel   = watchArgs.TryGetResult InboxWatchArgs.Channel
                            InboxProcess.Options.ItemName  = None
                            InboxProcess.Options.All       = true
                            InboxProcess.Options.DryRun    = watchArgs.Contains InboxWatchArgs.Dryrun
                            // See InboxProcessCli: reuses the top-level `eru --debug` flag.
                            InboxProcess.Options.Timing    = r.Contains EruArgs.Debug
                            InboxProcess.Options.Append    = watchArgs.GetResults InboxWatchArgs.Append
                        }
                        Interval = watchArgs.TryGetResult InboxWatchArgs.Interval
                    }
                | _ -> None)
        | _ -> None)

// Quiet period after the last raw-directory event before a trigger fires — long enough that
// a file still being written (inbox send writes then, separately, its .meta.json sidecar)
// doesn't get picked up mid-write.
let private debounceMs = 500

// Pure debounce decision, pulled out of the `FileSystemWatcher`/`Timer` callbacks so it's
// testable without either: given a burst of raw-directory event timestamps and a quiet
// period, decides the instants at which a processing pass would actually fire — one trigger
// per burst, `quietPeriodMs` after that burst's *last* event, coalescing any event that
// arrives before the pending trigger's deadline into the same burst rather than scheduling
// a second one.
let coalesceTriggers (quietPeriodMs: int) (eventsAt: DateTimeOffset list) : DateTimeOffset list =
    match eventsAt with
    | [] -> []
    | first :: rest ->
        let deadline (t: DateTimeOffset) = t.AddMilliseconds(float quietPeriodMs)
        let triggers, lastPending =
            rest
            |> List.fold
                (fun (triggers, pendingDeadline) evt ->
                    if evt <= pendingDeadline then
                        (triggers, deadline evt)
                    else
                        (triggers @ [ pendingDeadline ], deadline evt))
                ([], deadline first)
        triggers @ [ lastPending ]

let private runBatch (deps: Deps) (opts: InboxProcess.Options) : unit =
    match InboxProcess.execute deps opts with
    | Error e -> renderError e
    | Ok []   -> ()
    | Ok items -> renderText opts.DryRun None items

let run (deps: Deps) (cmd: Cmd) : int =
    match InboxProcess.resolveWatchTarget deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok target when not (Directory.Exists target.RawRootDir) ->
        renderError $"raw directory '{target.RawRootDir}' does not exist."
        1
    | Ok target ->

    let interval =
        match cmd.Interval with
        | Some i -> i
        | None ->
            let globalCfg = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
            let localCfg  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
            match Config.merge globalCfg localCfg with
            | Ok eff  -> eff.InboxWatchIntervalSeconds
            | Error _ -> 30

    let channelsDesc = target.Channels |> String.concat ", "
    printfn $"Watching inbox '{target.InboxName}' [{channelsDesc}] at {target.RawRootDir} (interval: {interval}s). Ctrl+C to stop."

    use cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun args ->
        args.Cancel <- true
        cts.Cancel())

    // Released once per debounced burst of raw-directory activity; consumed alongside the
    // poll timer via Task.WhenAny below, matching SiteServeServer's PeriodicTimer + cancellation
    // shutdown shape, plus this extra immediate-trigger path.
    use signal = new SemaphoreSlim(0)
    use debounceTimer = new Timer((fun _ -> signal.Release() |> ignore), null, Timeout.Infinite, Timeout.Infinite)

    use watcher =
        new FileSystemWatcher(
            target.RawRootDir,
            IncludeSubdirectories = true,
            NotifyFilter = (NotifyFilters.FileName ||| NotifyFilters.DirectoryName),
            EnableRaisingEvents = true)
    let armDebounce () = debounceTimer.Change(debounceMs, Timeout.Infinite) |> ignore
    watcher.Created.Add(fun _ -> armDebounce ())
    watcher.Renamed.Add(fun _ -> armDebounce ())

    let loop =
        task {
            use pollTimer = new PeriodicTimer(TimeSpan.FromSeconds(float interval))
            // `PeriodicTimer` allows only one outstanding `WaitForNextTickAsync` call at a
            // time, so this must be replaced only once it actually completes — not on every
            // loop iteration, since the debounce path below will usually win the race and
            // would otherwise leave this call still pending when a new one is started.
            let mutable pollWait = pollTimer.WaitForNextTickAsync(cts.Token).AsTask()
            let mutable keepGoing = true
            while keepGoing do
                let debounceWait = signal.WaitAsync(cts.Token)
                try
                    let! completed = Task.WhenAny(debounceWait, pollWait :> Task)
                    do! completed
                    if obj.ReferenceEquals(completed, pollWait) then
                        pollWait <- pollTimer.WaitForNextTickAsync(cts.Token).AsTask()
                    runBatch deps cmd.Command
                with :? OperationCanceledException ->
                    keepGoing <- false
        }
    loop.Wait()
    0
