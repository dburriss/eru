module Eru.Tests.McpRegistryTests

open System
open System.IO
open Xunit
open Eru.Adapters

let private withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "eru-reg-" + Guid.NewGuid().ToString("N"), "mcp")
    try f dir
    finally try Directory.Delete(Path.GetDirectoryName dir, true) with _ -> ()

let private entry pid version : McpRegistry.Entry =
    { Pid = pid; Version = version; StartedAt = DateTimeOffset.UtcNow }

let private allAlive (_: McpRegistry.Entry) = true

[<Fact>]
let ``a newer live server makes an older one yield`` () =
    withTempDir (fun dir ->
        McpRegistry.registerAt dir (entry 100 "0.15.1")
        McpRegistry.registerAt dir (entry 200 "0.16.0")
        Assert.Equal(Some "0.16.0", McpRegistry.newerRunningAt dir allAlive "0.15.1"))

[<Fact>]
let ``servers of the same or an older version do not make a server yield`` () =
    withTempDir (fun dir ->
        McpRegistry.registerAt dir (entry 100 "0.15.1")
        McpRegistry.registerAt dir (entry 200 "0.15.1")
        McpRegistry.registerAt dir (entry 300 "0.15.0")
        Assert.Equal(None, McpRegistry.newerRunningAt dir allAlive "0.15.1"))

[<Fact>]
let ``an alpha of the next patch counts as newer than the release`` () =
    withTempDir (fun dir ->
        McpRegistry.registerAt dir (entry 200 "0.15.2-alpha")
        Assert.Equal(Some "0.15.2-alpha", McpRegistry.newerRunningAt dir allAlive "0.15.1"))

[<Fact>]
let ``entries of dead processes are ignored and deleted`` () =
    withTempDir (fun dir ->
        McpRegistry.registerAt dir (entry 200 "9.9.9")
        let alive (e: McpRegistry.Entry) = e.Pid <> 200
        Assert.Equal(None, McpRegistry.newerRunningAt dir alive "0.15.1")
        Assert.False(File.Exists(Path.Combine(dir, "200.json"))))

[<Fact>]
let ``an unreadable registry file is pruned, not fatal`` () =
    withTempDir (fun dir ->
        Directory.CreateDirectory dir |> ignore
        File.WriteAllText(Path.Combine(dir, "junk.json"), "not json")
        Assert.Equal(None, McpRegistry.newerRunningAt dir allAlive "0.15.1")
        Assert.False(File.Exists(Path.Combine(dir, "junk.json"))))

[<Fact>]
let ``register writes the entry and unregister removes it`` () =
    withTempDir (fun dir ->
        McpRegistry.registerAt dir (entry 100 "0.15.1")
        Assert.Equal(1, (McpRegistry.liveEntriesAt dir allAlive).Length)
        McpRegistry.unregisterAt dir 100
        Assert.Empty(McpRegistry.liveEntriesAt dir allAlive))

[<Fact>]
let ``the current process is alive and a made-up pid is not`` () =
    let me = { entry Environment.ProcessId "0.15.1" with StartedAt = DateTimeOffset.UtcNow }
    Assert.True(McpRegistry.isAlive me)
    Assert.False(McpRegistry.isAlive (entry 2147483000 "0.15.1"))

[<Fact>]
let ``a pid reused by a later process is not the registered server`` () =
    // The entry claims to have started long before this process did, so this pid is a newcomer.
    let reused = { entry Environment.ProcessId "9.9.9" with StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromDays 30.0 }
    Assert.False(McpRegistry.isAlive reused)
