module Eru.Tests.ConfigTests

open Xunit
open Eru

[<Fact>]
let ``merge returns empty sources when both configs absent`` () =
    let result = Config.merge None None
    Assert.Empty result.Sources

[<Fact>]
let ``merge uses global sources when no local config`` () =
    let globalCfg = {
        Version = 1
        DefaultSources = [
            { Name = "main"; Url = Some "https://example.com/repo.git"; Branch = Some "main"; Prefix = None }
        ]
        Collections = []
        Defaults = None
    }
    let result = Config.merge (Some globalCfg) None
    Assert.Equal(1, result.Sources.Length)
    Assert.Equal("main", result.Sources[0].Name)

[<Fact>]
let ``merge prefers local CommitOnPull over global default`` () =
    let globalCfg = {
        Version = 1
        DefaultSources = []
        Collections = []
        Defaults = Some { Branch = None; CommitOnPull = Some false }
    }
    let localCfg = {
        Version = 1
        Sources = []
        Settings = Some { CommitOnPull = Some true; StateFile = None }
    }
    let result = Config.merge (Some globalCfg) (Some localCfg)
    Assert.True result.CommitOnPull

[<Fact>]
let ``merge uses custom StateFile from local settings`` () =
    let localCfg = {
        Version = 1
        Sources = []
        Settings = Some { CommitOnPull = None; StateFile = Some "custom.lock" }
    }
    let result = Config.merge None (Some localCfg)
    Assert.Equal("custom.lock", result.StateFile)

[<Fact>]
let ``LockFile parse roundtrips entries`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "main"; RemotePath = "templates/adr.md"; ContentHash = "sha256:abc123" }
        { LocalPath = "src/Utils.fs"; SourceName = "platform"; RemotePath = "dotnet/Utils.fs"; ContentHash = "sha256:def456" }
    ]
    let written = LockFile.write entries
    match LockFile.parse written with
    | Error e       -> Assert.Fail e
    | Ok parsed     ->
        Assert.Equal(2, parsed.Length)
        Assert.Equal("docs/adr.md",  parsed[0].LocalPath)
        Assert.Equal("main",         parsed[0].SourceName)
        Assert.Equal("templates/adr.md", parsed[0].RemotePath)
        Assert.Equal("src/Utils.fs", parsed[1].LocalPath)

[<Fact>]
let ``LockFile write sorts entries by LocalPath`` () =
    let entries = [
        { LocalPath = "z-last.md";  SourceName = "s"; RemotePath = "z.md"; ContentHash = "sha256:aaa" }
        { LocalPath = "a-first.md"; SourceName = "s"; RemotePath = "a.md"; ContentHash = "sha256:bbb" }
    ]
    let written = LockFile.write entries
    match LockFile.parse written with
    | Error e   -> Assert.Fail e
    | Ok parsed ->
        Assert.Equal("a-first.md", parsed[0].LocalPath)
        Assert.Equal("z-last.md",  parsed[1].LocalPath)
