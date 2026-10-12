module Eru.Tests.ConfigCmdTests

open Xunit
open Eru
open Eru.Adapters

let private globalPath = "/home/u/.config/eru/config.json"
let private localPath  = "/repo/.eru/config.json"

// In-memory filesystem over the real serializer/migration chain.
let private makeIO (files: (string * string) list) =
    let fs = System.Collections.Generic.Dictionary<string, string>()
    for (p, c) in files do fs[p] <- c
    let io : ConfigIO = {
        Path = (function LocalScope -> localPath | GlobalScope -> globalPath)
        ReadRaw  = fun p -> Ok (match fs.TryGetValue p with | true, c -> Some c | _ -> None)
        WriteRaw = fun p c -> fs[p] <- c; Ok ()
        ParseGlobal =
            Config.readAndMigrateGlobalJson
                Serialization.deserialize<GlobalConfig>
                Serialization.deserialize<GlobalConfigV2>
                Serialization.deserialize<GlobalConfigV1>
        ParseLocal =
            Config.readAndMigrateLocalJson
                Serialization.deserialize<LocalConfig>
                Serialization.deserialize<LocalConfigV2>
                Serialization.deserialize<LocalConfigV1>
        SerializeGlobal = Serialization.serialize<GlobalConfig>
        SerializeLocal  = Serialization.serialize<LocalConfig>
    }
    io, fs

let private ok label = function
    | Ok x -> x
    | Error e -> failwith $"{label}: expected Ok but got Error: {e}"

let private v2Local = """{"version":2,"sources":[{"name":"a","url":"https://x/a.git","bundles":[]}],"settings":{"commitOnPull":true}}"""
let private v3Global = """{"version":3,"files":{"blockPatterns":["*.exe"]}}"""

// ── list ────────────────────────────────────────────────────────────────────

[<Fact>]
let ``list reports missing, current and outdated files`` () =
    let io, _ = makeIO [ globalPath, v3Global; localPath, v2Local ]
    let rows = ConfigCmd.list io
    let g = rows |> List.find (fun r -> r.Scope = "global")
    let l = rows |> List.find (fun r -> r.Scope = "local")
    Assert.Equal(Some 3, g.Version)
    Assert.False g.NeedsMigration
    Assert.Equal(Some 2, l.Version)
    Assert.True l.NeedsMigration

[<Fact>]
let ``list marks absent files as not existing`` () =
    let io, _ = makeIO []
    let rows = ConfigCmd.list io
    Assert.All(rows, fun r -> Assert.False r.Exists)

[<Fact>]
let ``list treats a file without version as version 1`` () =
    let io, _ = makeIO [ localPath, """{"sources":[]}""" ]
    let l = ConfigCmd.list io |> List.find (fun r -> r.Scope = "local")
    Assert.Equal(Some 1, l.Version)
    Assert.True l.NeedsMigration

// ── get / view ──────────────────────────────────────────────────────────────

[<Fact>]
let ``get reports local override, global value and default with origin`` () =
    let io, _ = makeIO [ globalPath, v3Global; localPath, v2Local ]
    let local = ConfigCmd.get io "sync.commitOnPull" |> ok "local"
    Assert.Equal("true", local.Value)
    Assert.Equal("local", local.Origin)
    let glob = ConfigCmd.get io "files.blockPatterns" |> ok "global"
    Assert.Equal("""["*.exe"]""", glob.Value)
    Assert.Equal("global", glob.Origin)
    let def = ConfigCmd.get io "mcp.refreshIntervalMinutes" |> ok "default"
    Assert.Equal("60", def.Value)
    Assert.Equal("default", def.Origin)

[<Fact>]
let ``get of an unknown unset key is an error`` () =
    let io, _ = makeIO []
    Assert.True (ConfigCmd.get io "nope.nothing" |> Result.isError)

[<Fact>]
let ``view lists settings with origin and sources`` () =
    let io, _ = makeIO [ globalPath, v3Global; localPath, v2Local ]
    let v = ConfigCmd.view io |> ok "view"
    let commit = v.Settings |> List.find (fun s -> s.Key = "sync.commitOnPull")
    Assert.Equal("local", commit.Origin)
    let src = v.Entities |> List.find (fun e -> e.Name = "a")
    Assert.Equal("local", src.Origin)

// ── set ─────────────────────────────────────────────────────────────────────

[<Fact>]
let ``set writes a scalar and creates a missing file at the current version`` () =
    let io, fs = makeIO []
    ConfigCmd.set io LocalScope "sync.commitOnPull" (Some "true") |> ok "set" |> ignore
    Assert.Contains("\"commitOnPull\": true", fs[localPath])
    let got = ConfigCmd.get io "sync.commitOnPull" |> ok "get"
    Assert.Equal("true", got.Value)

[<Fact>]
let ``set replaces a list value`` () =
    let io, fs = makeIO [ localPath, """{"version":3}""" ]
    ConfigCmd.set io LocalScope "files.blockPatterns" (Some """["*.zip"]""") |> ok "set" |> ignore
    let got = ConfigCmd.get io "files.blockPatterns" |> ok "get"
    Assert.Equal("""["*.zip"]""", got.Value)
    Assert.Equal("local", got.Origin)

[<Fact>]
let ``set treats a bare word as a string`` () =
    let io, _ = makeIO [ localPath, """{"version":3}""" ]
    ConfigCmd.set io LocalScope "sync.stateFile" (Some "my.lock") |> ok "set" |> ignore
    Assert.Equal("\"my.lock\"", (ConfigCmd.get io "sync.stateFile" |> ok "get").Value)

[<Fact>]
let ``set unset removes the key`` () =
    let io, _ = makeIO [ localPath, """{"version":3,"sync":{"stateFile":"x.lock"}}""" ]
    ConfigCmd.set io LocalScope "sync.stateFile" None |> ok "unset" |> ignore
    Assert.Equal("default", (ConfigCmd.get io "sync.stateFile" |> ok "get").Origin)

[<Fact>]
let ``set to global writes the global file`` () =
    let io, fs = makeIO [ globalPath, v3Global ]
    ConfigCmd.set io GlobalScope "mcp.refreshIntervalMinutes" (Some "5") |> ok "set" |> ignore
    Assert.Contains("refreshIntervalMinutes", fs[globalPath])
    Assert.False (fs.ContainsKey localPath)

[<Fact>]
let ``set rejects a wrongly typed value and writes nothing`` () =
    let io, fs = makeIO [ localPath, """{"version":3}""" ]
    let before = fs[localPath]
    Assert.True (ConfigCmd.set io LocalScope "mcp.refreshIntervalMinutes" (Some "abc") |> Result.isError)
    Assert.Equal(before, fs[localPath])

[<Fact>]
let ``set refuses an outdated file`` () =
    let io, fs = makeIO [ localPath, v2Local ]
    match ConfigCmd.set io LocalScope "sync.stateFile" (Some "x") with
    | Error e -> Assert.Contains("migrate", e)
    | Ok _ -> failwith "expected error"
    Assert.Equal(v2Local, fs[localPath])

[<Fact>]
let ``set refuses sources, collections and inbox items`` () =
    let io, _ = makeIO [ localPath, """{"version":3}""" ]
    for key in [ "source.items"; "collection.items"; "inbox.items.x"; "version"; "sync" ] do
        Assert.True (ConfigCmd.set io LocalScope key (Some "1") |> Result.isError, key)

// ── migrate ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``migrate dry run writes nothing`` () =
    let io, fs = makeIO [ localPath, v2Local ]
    match ConfigCmd.migrate io LocalScope true |> ok "migrate" with
    | ConfigCmd.Migrated (2, _, None, content) -> Assert.Contains("\"version\": 3", content)
    | other -> failwith $"unexpected {other}"
    Assert.Equal(v2Local, fs[localPath])
    Assert.False (fs.ContainsKey (localPath + ".bak"))

[<Fact>]
let ``migrate writes v3 and a backup, preserving the merged result`` () =
    let io, fs = makeIO [ globalPath, v3Global; localPath, v2Local ]
    let before = Config.merge None (Some (Config.readAndMigrateLocalJson Serialization.deserialize<LocalConfig> Serialization.deserialize<LocalConfigV2> Serialization.deserialize<LocalConfigV1> v2Local |> ok "before")) |> ok "merge before"
    ConfigCmd.migrate io LocalScope false |> ok "migrate" |> ignore
    Assert.Equal(v2Local, fs[localPath + ".bak"])
    Assert.Equal(Some 3, (ConfigCmd.list io |> List.find (fun r -> r.Scope = "local")).Version)
    let after = Config.merge None (Some (io.ParseLocal fs[localPath] |> ok "after")) |> ok "merge after"
    Assert.Equal<SourceConfig list>(before.Sources, after.Sources)
    Assert.Equal(before.CommitOnPull, after.CommitOnPull)

[<Fact>]
let ``migrate is a no-op on a current file`` () =
    let io, fs = makeIO [ globalPath, v3Global ]
    match ConfigCmd.migrate io GlobalScope false |> ok "migrate" with
    | ConfigCmd.AlreadyCurrent 3 -> ()
    | other -> failwith $"unexpected {other}"
    Assert.False (fs.ContainsKey (globalPath + ".bak"))

[<Fact>]
let ``migrate rejects a newer version and a missing file`` () =
    let io, _ = makeIO [ localPath, """{"version":4}""" ]
    Assert.True (ConfigCmd.migrate io LocalScope false |> Result.isError)
    Assert.True (ConfigCmd.migrate io GlobalScope false |> Result.isError)
