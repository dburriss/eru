namespace Eru.Mcp

open System
open System.Threading
open Eru.Adapters
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

/// Registers this server and stops it when a newer eru is running. An upgrade replaces the binary on
/// disk but not the processes already running it; those keep syncing with old code. A server that sees
/// a newer one in the registry exits instead, and the client starts a fresh one when it next needs it.
/// Servers of the same version never stop each other.
type SupersedeService(lifetime: IHostApplicationLifetime, logger: ILogger<SupersedeService>) =
    inherit BackgroundService()

    let registryDir = Paths.mcpRegistryDir ()
    let pid = Environment.ProcessId

    let check (self: string) =
        match McpRegistry.newerRunningAt registryDir McpRegistry.isAlive self with
        | Some newer ->
            logger.LogWarning("A newer eru ({Newer}) is running; this server ({Self}) is stale and is stopping.", newer, self)
            lifetime.StopApplication()
            true
        | None -> false

    override _.ExecuteAsync(ct: CancellationToken) =
        task {
            match EruVersion.current with
            | None -> ()   // version unknown (not the packaged CLI): neither register nor yield
            | Some self ->
                McpRegistry.registerAt registryDir { Pid = pid; Version = self; StartedAt = DateTimeOffset.UtcNow }
                try
                    let mutable running = not (check self)
                    use timer = new PeriodicTimer(TimeSpan.FromMinutes 1.0)
                    while running do
                        let! tick = timer.WaitForNextTickAsync(ct)
                        running <- tick && not (check self)
                with :? OperationCanceledException -> ()
        }

    override _.StopAsync(ct: CancellationToken) =
        McpRegistry.unregisterAt registryDir pid
        base.StopAsync ct
