module Eru.Tests.ConfigTests

open Xunit
open Eru

let private unwrapOk label = function
    | Ok x -> x
    | Error e -> failwith $"{label}: expected Ok but got Error: {e}"

let private assertError (expected: string) = function
    | Ok _ -> failwith $"Expected Error \"{expected}\" but got Ok"
    | Error e -> Assert.Contains(expected, e)

let private makeGlobal sources collections =
    { Version = 1; DefaultSources = sources; Collections = collections; Defaults = None }

let private makeSource name url =
    { Name = name; Url = url; Branch = None; BasePath = None }

// ── merge: basic cases ──────────────────────────────────────────────────────

[<Fact>]
let ``merge returns empty sources when both configs absent`` () =
    let result = Config.merge None None |> unwrapOk "both absent"
    Assert.Empty result.Sources

[<Fact>]
let ``merge uses global sources when no local config`` () =
    let g = makeGlobal [ makeSource "main" (Some "https://example.com/repo.git") ] []
    let result = Config.merge (Some g) None |> unwrapOk "global only"
    Assert.Equal(1, result.Sources.Length)
    Assert.Equal("main", result.Sources[0].Name)

[<Fact>]
let ``merge prefers local CommitOnPull over global default`` () =
    let g = { makeGlobal [] [] with Defaults = Some { Branch = None; CommitOnPull = Some false; McpRefreshIntervalMinutes = None; BlockPatterns = None; AllowPatterns = None; AllowBinaries = None } }
    let l = { Version = 1; Sources = []; Collections = []; Settings = Some { CommitOnPull = Some true; StateFile = None; BlockPatterns = None; AllowPatterns = None; AllowBinaries = None } }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "commitOnPull"
    Assert.True result.CommitOnPull

[<Fact>]
let ``merge uses custom StateFile from local settings`` () =
    let l = { Version = 1; Sources = []; Collections = []; Settings = Some { CommitOnPull = None; StateFile = Some "custom.lock"; BlockPatterns = None; AllowPatterns = None; AllowBinaries = None } }
    let result = Config.merge None (Some l) |> unwrapOk "stateFile"
    Assert.Equal("custom.lock", result.StateFile)

[<Fact>]
let ``merge defaults McpRefreshIntervalMinutes to 60`` () =
    let result = Config.merge None None |> unwrapOk "mcp refresh default"
    Assert.Equal(60, result.McpRefreshIntervalMinutes)

[<Fact>]
let ``merge uses McpRefreshIntervalMinutes from global defaults`` () =
    let g = { makeGlobal [] [] with Defaults = Some { Branch = None; CommitOnPull = None; McpRefreshIntervalMinutes = Some 30; BlockPatterns = None; AllowPatterns = None; AllowBinaries = None } }
    let result = Config.merge (Some g) None |> unwrapOk "mcp refresh custom"
    Assert.Equal(30, result.McpRefreshIntervalMinutes)

// ── merge: source ordering ───────────────────────────────────────────────────

[<Fact>]
let ``merge preserves local source declaration order`` () =
    let g = makeGlobal [ makeSource "b" (Some "https://b.com"); makeSource "a" (Some "https://a.com") ] []
    let l = {
        Version = 1
        Sources = [ makeSource "b" None; makeSource "a" (Some "https://a-override.com") ]
        Collections = []
        Settings = None
    }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "ordering"
    Assert.Equal(2, result.Sources.Length)
    Assert.Equal("b", result.Sources[0].Name)
    Assert.Equal("a", result.Sources[1].Name)

[<Fact>]
let ``merge appends global-only sources after local sources`` () =
    let g = makeGlobal [ makeSource "local-one" (Some "https://l.com"); makeSource "global-only" (Some "https://g.com") ] []
    let l = { Version = 1; Sources = [ makeSource "local-one" None ]; Collections = []; Settings = None }
    let result = Config.merge (Some g) (Some l) |> unwrapOk "global-only appended"
    Assert.Equal(2, result.Sources.Length)
    Assert.Equal("local-one", result.Sources[0].Name)
    Assert.Equal("global-only", result.Sources[1].Name)

// ── merge: error cases ───────────────────────────────────────────────────────

[<Fact>]
let ``merge errors when inherited local source not found in global config`` () =
    let g = makeGlobal [ makeSource "other" (Some "https://other.com") ] []
    let l = { Version = 1; Sources = [ makeSource "missing" None ]; Collections = []; Settings = None }
    Config.merge (Some g) (Some l)
    |> assertError "missing"

[<Fact>]
let ``merge errors when global config version too high`` () =
    let g = { makeGlobal [] [] with Version = 99 }
    Config.merge (Some g) None
    |> assertError "please upgrade eru"

[<Fact>]
let ``merge errors when local config version too high`` () =
    let l = { Version = 99; Sources = []; Collections = []; Settings = None }
    Config.merge None (Some l)
    |> assertError "please upgrade eru"

[<Fact>]
let ``merge errors on duplicate source name in global config`` () =
    let g = makeGlobal [ makeSource "dup" (Some "https://a.com"); makeSource "dup" (Some "https://b.com") ] []
    Config.merge (Some g) None
    |> assertError "Duplicate source name 'dup'"

[<Fact>]
let ``merge errors on duplicate source name in local config`` () =
    let l = { Version = 1; Sources = [ makeSource "dup" None; makeSource "dup" None ]; Collections = []; Settings = None }
    Config.merge None (Some l)
    |> assertError "Duplicate source name 'dup'"

[<Fact>]
let ``merge errors when global source has no URL`` () =
    let g = makeGlobal [ makeSource "no-url" None ] []
    Config.merge (Some g) None
    |> assertError "no URL"

[<Fact>]
let ``merge errors when collection references unknown source`` () =
    let file = { Source = "unknown"; RemotePath = "foo.md"; Tags = []; Description = None }
    let col  = { Name = "my-col"; Tags = []; Files = [ file ]; Description = None }
    let g    = makeGlobal [ makeSource "known" (Some "https://known.com") ] [ col ]
    Config.merge (Some g) None
    |> assertError "unknown source 'unknown'"

// ── resolveByTags ────────────────────────────────────────────────────────────

let private makeCollection name tags files =
    { Name = name; Tags = tags; Files = files; Description = None }

let private makeFileRef source path tags =
    { Source = source; RemotePath = path; Tags = tags; Description = None }

[<Fact>]
let ``resolveByTags returns files from matching collection`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [ makeCollection "col" ["backend"] files ]
    let result = Config.resolveByTags ["backend"] g
    Assert.Equal(1, result.Length)
    Assert.Equal(("src", "foo.md"), result[0])

[<Fact>]
let ``resolveByTags AND semantics - all tags must match`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [
        makeCollection "partial" ["backend"] files
        makeCollection "full"    ["backend"; "dotnet"] files
    ]
    let result = Config.resolveByTags ["backend"; "dotnet"] g
    Assert.Equal(1, result.Length)

[<Fact>]
let ``resolveByTags is case insensitive`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [ makeCollection "col" ["Backend"] files ]
    let result = Config.resolveByTags ["backend"] g
    Assert.Equal(1, result.Length)

[<Fact>]
let ``resolveByTags matches file-level tags even when collection does not`` () =
    let files = [
        makeFileRef "src" "matches.md" ["dotnet"]
        makeFileRef "src" "no-match.md" []
    ]
    let g = makeGlobal [] [ makeCollection "col" ["other"] files ]
    let result = Config.resolveByTags ["dotnet"] g
    Assert.Equal(1, result.Length)
    Assert.Equal("matches.md", snd result[0])

[<Fact>]
let ``resolveByTags returns empty when no collections match`` () =
    let files = [ makeFileRef "src" "foo.md" [] ]
    let g = makeGlobal [] [ makeCollection "col" ["frontend"] files ]
    let result = Config.resolveByTags ["backend"] g
    Assert.Empty result

[<Fact>]
let ``resolveByTags deduplicates files appearing in multiple collections`` () =
    let file = makeFileRef "src" "shared.md" ["common"]
    let g = makeGlobal [] [
        makeCollection "col1" ["common"] [ file ]
        makeCollection "col2" ["common"] [ file ]
    ]
    let result = Config.resolveByTags ["common"] g
    Assert.Equal(1, result.Length)

// ── LockFile ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``LockFile parse roundtrips entries`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "main"; RemotePath = "templates/adr.md"; ContentHash = "sha256:abc123" }
        { LocalPath = "src/Utils.fs"; SourceName = "platform"; RemotePath = "dotnet/Utils.fs"; ContentHash = "sha256:def456" }
    ]
    let written = LockFile.write entries
    match LockFile.parse written with
    | Error e   -> Assert.Fail e
    | Ok parsed ->
        Assert.Equal(2, parsed.Length)
        Assert.Equal("docs/adr.md",      parsed[0].LocalPath)
        Assert.Equal("main",             parsed[0].SourceName)
        Assert.Equal("templates/adr.md", parsed[0].RemotePath)
        Assert.Equal("src/Utils.fs",     parsed[1].LocalPath)

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

[<Fact>]
let ``LockFile findByLocalPath returns entry when found`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "src"; RemotePath = "adr.md"; ContentHash = "sha256:aaa" }
        { LocalPath = "src/Utils.fs"; SourceName = "src"; RemotePath = "Utils.fs"; ContentHash = "sha256:bbb" }
    ]
    match LockFile.findByLocalPath "docs/adr.md" entries with
    | None   -> Assert.Fail "Expected Some but got None"
    | Some e -> Assert.Equal("sha256:aaa", e.ContentHash)

[<Fact>]
let ``LockFile findByLocalPath returns None when not found`` () =
    let entries = [
        { LocalPath = "docs/adr.md"; SourceName = "src"; RemotePath = "adr.md"; ContentHash = "sha256:aaa" }
    ]
    Assert.Equal(None, LockFile.findByLocalPath "does-not-exist.md" entries)

// ── merge: Collections ───────────────────────────────────────────────────────

[<Fact>]
let ``merge populates Collections from GlobalConfig`` () =
    let file = makeFileRef "src" "docs/guide.md" ["docs"]
    let col  = { Name = "guides"; Tags = []; Files = [ file ]; Description = None }
    let g    = makeGlobal [ makeSource "src" (Some "https://src.com") ] [ col ]
    let result = Config.merge (Some g) None |> unwrapOk "collections"
    Assert.Equal(1, result.Collections.Length)
    Assert.Equal("src",          result.Collections[0].Source)
    Assert.Equal("docs/guide.md", result.Collections[0].RemotePath)

[<Fact>]
let ``merge returns empty Collections when no global config`` () =
    let result = Config.merge None None |> unwrapOk "no global"
    Assert.Empty result.Collections

// ── withManifests ────────────────────────────────────────────────────────────

[<Fact>]
let ``withManifests appends manifest files not in user config`` () =
    let src = makeSource "kb" (Some "https://kb.com")
    let eff = Config.merge (Some (makeGlobal [ src ] [])) None |> unwrapOk "base"
    let manifest = { Version = 1; Files = [ { Path = "guide.md"; Tags = ["docs"]; Description = None } ] }
    let result = Config.withManifests (fun _ -> Ok (Some manifest)) eff
    Assert.Equal(1, result.Collections.Length)
    Assert.Equal("kb",       result.Collections[0].Source)
    Assert.Equal("guide.md", result.Collections[0].RemotePath)
    Assert.Equal<string list>(["docs"],   result.Collections[0].Tags)

[<Fact>]
let ``withManifests user-explicit entry wins on collision`` () =
    let src      = makeSource "kb" (Some "https://kb.com")
    let explicit = { Source = "kb"; RemotePath = "guide.md"; Tags = ["explicit"]; Description = Some "user-defined" }
    let col      = { Name = "c"; Tags = []; Files = [ explicit ]; Description = None }
    let g        = makeGlobal [ src ] [ col ]
    let eff      = Config.merge (Some g) None |> unwrapOk "base"
    let manifest = { Version = 1; Files = [ { Path = "guide.md"; Tags = ["manifest"]; Description = None } ] }
    let result   = Config.withManifests (fun _ -> Ok (Some manifest)) eff
    // Should still have exactly one entry (no duplicate), and it should be the user-explicit one
    Assert.Equal(1, result.Collections.Length)
    Assert.Equal<string list>(["explicit"], result.Collections[0].Tags)

[<Fact>]
let ``withManifests silently ignores missing manifest`` () =
    let src = makeSource "kb" (Some "https://kb.com")
    let eff = Config.merge (Some (makeGlobal [ src ] [])) None |> unwrapOk "base"
    let result = Config.withManifests (fun _ -> Ok None) eff
    Assert.Empty result.Collections

[<Fact>]
let ``withManifests silently ignores manifest read error`` () =
    let src = makeSource "kb" (Some "https://kb.com")
    let eff = Config.merge (Some (makeGlobal [ src ] [])) None |> unwrapOk "base"
    let result = Config.withManifests (fun _ -> Error "disk error") eff
    Assert.Empty result.Collections
