module Eru.Tests.ConfigCmdTests

open Xunit
open Eru
open Eru.Adapters

let private globalPath = "/home/u/.config/eru/config.json"
let private localPath  = "/repo/.eru/config.json"

// In-memory filesystem over the real serializer/migration chain.
let private makeIOWithEnv (env: string option) (files: (string * string) list) =
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
        ReadEnv         = fun () -> Ok env
    }
    io, fs

let private makeIO (files: (string * string) list) = makeIOWithEnv None files

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
let ``get reports the built-in defaults of the search settings`` () =
    let io, _ = makeIO []
    let mode = ConfigCmd.get io "search.mode" |> ok "mode"
    Assert.Equal("\"lex\"", mode.Value)
    Assert.Equal("default", mode.Origin)
    let ignore = ConfigCmd.get io "search.ignorePatterns" |> ok "ignore"
    Assert.Equal("""["index.md","log.md"]""", ignore.Value)

[<Fact>]
let ``set writes a search setting`` () =
    let io, files = makeIO [ localPath, """{"version":3}""" ]
    ConfigCmd.set io LocalScope "search.mode" (Some "\"hybrid\"") |> ok "set mode" |> ignore
    Assert.Contains("hybrid", files.[localPath])

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

// ── environment overrides ───────────────────────────────────────────────────

let private envJson = """{"version":3,"mcp":{"refreshIntervalMinutes":5},"search":{"mode":"hybrid"}}"""

[<Fact>]
let ``get reports an environment override with origin env`` () =
    let io, _ = makeIOWithEnv (Some envJson) [ globalPath, v3Global; localPath, v2Local ]
    let mode = ConfigCmd.get io "search.mode" |> ok "mode"
    Assert.Equal("\"hybrid\"", mode.Value)
    Assert.Equal("env", mode.Origin)
    let mcp = ConfigCmd.get io "mcp.refreshIntervalMinutes" |> ok "mcp"
    Assert.Equal("5", mcp.Value)
    Assert.Equal("env", mcp.Origin)

[<Fact>]
let ``get still reads the files for keys the environment does not set`` () =
    let io, _ = makeIOWithEnv (Some envJson) [ globalPath, v3Global; localPath, v2Local ]
    let commit = ConfigCmd.get io "sync.commitOnPull" |> ok "commit"
    Assert.Equal("local", commit.Origin)
    let block = ConfigCmd.get io "files.blockPatterns" |> ok "block"
    Assert.Equal("global", block.Origin)

[<Fact>]
let ``view lists environment overrides with origin env`` () =
    let io, _ = makeIOWithEnv (Some envJson) [ globalPath, v3Global; localPath, v2Local ]
    let v = ConfigCmd.view io |> ok "view"
    let origin key = (v.Settings |> List.find (fun s -> s.Key = key)).Origin
    Assert.Equal("env", origin "search.mode")
    Assert.Equal("env", origin "mcp.refreshIntervalMinutes")
    Assert.Equal("local", origin "sync.commitOnPull")

[<Fact>]
let ``set never writes an environment value into the file`` () =
    let io, files = makeIOWithEnv (Some envJson) [ localPath, """{"version":3}""" ]
    ConfigCmd.set io LocalScope "sync.stateFile" (Some "\"mine.lock\"") |> ok "set" |> ignore
    Assert.Contains("mine.lock", files.[localPath])
    Assert.DoesNotContain("hybrid", files.[localPath])
    Assert.DoesNotContain("refreshIntervalMinutes", files.[localPath])

[<Fact>]
let ``set validates the file alone, so a bad environment value does not block it`` () =
    let bad = """{"version":3,"search":{"mode":"nope"}}"""
    let io, files = makeIOWithEnv (Some bad) [ localPath, """{"version":3}""" ]
    ConfigCmd.set io LocalScope "search.mode" (Some "\"sem\"") |> ok "set" |> ignore
    Assert.Contains("sem", files.[localPath])


// ── per-source settings (--source) ──────────────────────────────────────────

let private sourcesGlobal =
    """{"version":3,"files":{"allowBinaries":false},"source":{"items":[
        {"name":"kb","url":"https://x/kb.git","bundles":[],"files":{"allowBinaries":true},"search":{"ignorePatterns":["CHANGELOG.md"]}},
        {"name":"plain","url":"https://x/plain.git","bundles":[]}]}}"""

[<Fact>]
let ``get --source returns the source's own value and where it came from`` () =
    let io, _ = makeIO [ globalPath, sourcesGlobal ]
    let own = ConfigCmd.getForSource io "kb" "files.allowBinaries" |> ok "own"
    Assert.Equal("true", own.Value)
    Assert.Equal("source global", own.Origin)

[<Fact>]
let ``get --source falls back to the setting's normal resolution`` () =
    let io, _ = makeIO [ globalPath, sourcesGlobal ]
    let r = ConfigCmd.getForSource io "plain" "files.allowBinaries" |> ok "fallback"
    Assert.Equal("false", r.Value)
    Assert.Equal("global", r.Origin)
    let d = ConfigCmd.getForSource io "plain" "okf.ignorePatterns" |> ok "default"
    Assert.Equal("default", d.Origin)

[<Fact>]
let ``get --source reports an env override with origin source env`` () =
    let env = """{"version":3,"source":{"items":[{"name":"kb","files":{"allowBinaries":false}}]}}"""
    let io, _ = makeIOWithEnv (Some env) [ globalPath, sourcesGlobal ]
    let r = ConfigCmd.getForSource io "kb" "files.allowBinaries" |> ok "env"
    Assert.Equal("false", r.Value)
    Assert.Equal("source env", r.Origin)

[<Fact>]
let ``get --source rejects unknown sources and keys that are not per source`` () =
    let io, _ = makeIO [ globalPath, sourcesGlobal ]
    match ConfigCmd.getForSource io "ghost" "files.allowBinaries" with
    | Error e -> Assert.Contains("unknown source 'ghost'", e)
    | Ok _ -> failwith "expected an error"
    match ConfigCmd.getForSource io "kb" "search.mode" with
    | Error e -> Assert.Contains("cannot be set per source", e)
    | Ok _ -> failwith "expected an error"

[<Fact>]
let ``source names are matched exactly`` () =
    let io, _ = makeIO [ globalPath, sourcesGlobal ]
    Assert.True (ConfigCmd.getForSource io "KB" "files.allowBinaries" |> Result.isError)

[<Fact>]
let ``view --source lists every per-source setting`` () =
    let io, _ = makeIO [ globalPath, sourcesGlobal ]
    let v = ConfigCmd.viewSource io "kb" |> ok "view"
    Assert.Equal<string list>(ConfigCmd.sourceSettings, v.Settings |> List.map (fun s -> s.Key))
    Assert.Equal("source global", (v.Settings |> List.find (fun s -> s.Key = "search.ignorePatterns")).Origin)

[<Fact>]
let ``set --source writes into the entry of a local source`` () =
    let local = """{"version":3,"source":{"items":[{"name":"mine","url":"https://x/mine.git","bundles":[]}]}}"""
    let io, files = makeIO [ localPath, local ]
    ConfigCmd.setForSource io LocalScope "mine" "files.allowBinaries" (Some "true") |> ok "set" |> ignore
    let r = ConfigCmd.getForSource io "mine" "files.allowBinaries" |> ok "get"
    Assert.Equal("true", r.Value)
    Assert.Equal("source local", r.Origin)
    Assert.Contains("https://x/mine.git", files.[localPath])

[<Fact>]
let ``set --source on a global source adds a local entry without a url`` () =
    let io, files = makeIO [ globalPath, sourcesGlobal; localPath, """{"version":3}""" ]
    ConfigCmd.setForSource io LocalScope "plain" "search.ignorePatterns" (Some """["a.md"]""") |> ok "set" |> ignore
    Assert.DoesNotContain("https://x/plain.git", files.[localPath])
    let r = ConfigCmd.getForSource io "plain" "search.ignorePatterns" |> ok "get"
    Assert.Equal("""["a.md"]""", r.Value)
    Assert.Equal("source local", r.Origin)
    // the global entry is untouched
    Assert.Equal(sourcesGlobal, files.[globalPath])

[<Fact>]
let ``set --source --global needs the source to be in the global config`` () =
    let local = """{"version":3,"source":{"items":[{"name":"mine","url":"https://x/mine.git","bundles":[]}]}}"""
    let io, _ = makeIO [ globalPath, sourcesGlobal; localPath, local ]
    match ConfigCmd.setForSource io GlobalScope "mine" "files.allowBinaries" (Some "true") with
    | Error e -> Assert.Contains("not in the global config", e)
    | Ok _ -> failwith "expected an error"

[<Fact>]
let ``set --source rejects an unknown source, an unsupported key and a badly typed value`` () =
    let io, files = makeIO [ globalPath, sourcesGlobal; localPath, """{"version":3}""" ]
    Assert.True (ConfigCmd.setForSource io LocalScope "ghost" "files.allowBinaries" (Some "true") |> Result.isError)
    Assert.True (ConfigCmd.setForSource io LocalScope "kb" "okf.defaultType" (Some "\"x\"") |> Result.isError)
    Assert.True (ConfigCmd.setForSource io LocalScope "kb" "files.allowBinaries" (Some "\"yes\"") |> Result.isError)
    Assert.Equal("""{"version":3}""", files.[localPath])

[<Fact>]
let ``unset --source removes the override and tidies a stub entry`` () =
    let io, files = makeIO [ globalPath, sourcesGlobal; localPath, """{"version":3}""" ]
    ConfigCmd.setForSource io LocalScope "plain" "files.allowBinaries" (Some "true") |> ok "set" |> ignore
    ConfigCmd.setForSource io LocalScope "plain" "files.allowBinaries" None |> ok "unset" |> ignore
    Assert.DoesNotContain("plain", files.[localPath])
    let r = ConfigCmd.getForSource io "plain" "files.allowBinaries" |> ok "get"
    Assert.Equal("global", r.Origin)

[<Fact>]
let ``unset --source keeps the entry's url and its other overrides`` () =
    let io, files = makeIO [ globalPath, sourcesGlobal ]
    ConfigCmd.setForSource io GlobalScope "kb" "files.allowBinaries" None |> ok "unset" |> ignore
    Assert.Contains("https://x/kb.git", files.[globalPath])
    Assert.Contains("CHANGELOG.md", files.[globalPath])
    let r = ConfigCmd.getForSource io "kb" "files.allowBinaries" |> ok "get"
    Assert.Equal("global", r.Origin)   // back to the files.allowBinaries section value

[<Fact>]
let ``unset --source on a source with nothing to remove writes nothing`` () =
    let io, files = makeIO [ globalPath, sourcesGlobal; localPath, """{"version":3}""" ]
    ConfigCmd.setForSource io LocalScope "plain" "files.allowBinaries" None |> ok "unset" |> ignore
    Assert.DoesNotContain("plain", files.[localPath])
