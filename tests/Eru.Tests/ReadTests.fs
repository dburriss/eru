module Eru.Tests.ReadTests

open Xunit
open Eru

let private source name url : SourceConfig = { Name = name; Url = url; Branch = None; Bundles = [] }

let private index (entries: (string * IndexEntry) list) : SourceIndex =
    { Version = 1; SourceHeadSha = None; ConsecutiveShaCheckFailures = 0; Entries = Map.ofList entries }

let private cached rel = { IndexEntry.empty with CacheRelPath = Some rel }

let private noRemote : string list -> Result<(string * string) list, string> = fun _ -> Error "no network"

let private makeDeps
    (sources: SourceConfig list)
    (lockEntries: LockEntry list)
    (indexes: Map<string, SourceIndex>)
    (cache: Map<string * string, string>)
    (localFiles: Map<string, string>)
    (remote: string list -> Result<(string * string) list, string>) : Deps =
    let globalCfg : GlobalConfig =
        { Version = 1; DefaultSources = sources; Collections = []; DefaultInboxes = Map.empty; Defaults = None }
    {
        ReadGlobalConfig   = fun () -> Ok (Some globalCfg)
        ReadLocalConfig    = fun () -> Ok None
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok lockEntries
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ paths -> remote paths
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
        ReadLocalFile       = fun p -> Ok (Map.tryFind p localFiles)
        DeleteLocalFile     = fun _ -> Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/repo"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok None
        WriteLocalManifest      = fun _ -> Ok ()
        ResolveLocalGlob        = fun _ -> []
        ReadSourceIndex         = fun s -> Ok (Map.tryFind s indexes)
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun s rel -> Ok (Map.tryFind (s, rel) cache)
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fun _ -> []
        GetRemoteHeadSha        = fun _ _ -> Error "not implemented"
        DirectoryExists        = fun _ -> true
        GetUtcNow        = fun () -> System.DateTimeOffset.UtcNow
        ListLocalFiles   = fun _ -> Ok []
        ListLocalDirectories = fun _ -> Ok []
        MoveLocalFile    = fun _ _ -> Ok ()
        PushToRemote         = fun _ _ _ _ -> Ok "main"
        RunAgent         = fun _ _ _ _ -> Ok { Response = ""; Timings = { InitializeMs = 0.0; SessionNewMs = 0.0; PromptMs = 0.0 } }
    }

let private docs = source "docs" (Some "https://example.com/docs.git")
let private other = source "other" (Some "https://example.com/other.git")

let private simpleDeps =
    makeDeps [docs] [] (Map.ofList ["docs", index ["a/guide.md", cached "files/aa"]])
        (Map.ofList [("docs", "files/aa"), "# Guide"]) Map.empty noRemote

let private contentOf result =
    match result with
    | Ok (d: Read.Document) -> d.Content
    | Error e -> failwith e

let private assertErrorContains (expected: string) result =
    match result with
    | Ok _ -> Assert.Fail("Expected Error result")
    | Error (e: string) -> Assert.Contains(expected, e)

[<Fact>]
let ``prints cached content by source and path`` () =
    Assert.Equal("# Guide", contentOf (Read.execute simpleDeps "docs:a/guide.md"))

[<Fact>]
let ``prints cached content by full hash`` () =
    let hash = Patterns.pathShortHash "a/guide.md"
    let result = Read.execute simpleDeps hash
    Assert.Equal("# Guide", contentOf result)
    match result with
    | Ok d -> Assert.Equal(hash, d.Hash); Assert.Equal("docs", d.Source)
    | Error e -> Assert.Fail e

[<Fact>]
let ``prints cached content by hash prefix`` () =
    let prefix = (Patterns.pathShortHash "a/guide.md").[..3]
    Assert.Equal("# Guide", contentOf (Read.execute simpleDeps prefix))

[<Fact>]
let ``prefers local file over cache when document was added`` () =
    let entry = { IndexEntry.empty with CacheRelPath = Some "files/aa"; LocalPath = Some "knowledge/guide.md" }
    let deps =
        makeDeps [docs] [] (Map.ofList ["docs", index ["a/guide.md", entry]])
            (Map.ofList [("docs", "files/aa"), "cached"]) (Map.ofList ["/repo/knowledge/guide.md", "local"]) noRemote
    Assert.Equal("local", contentOf (Read.execute deps "docs:a/guide.md"))

[<Fact>]
let ``falls back to live fetch when not in index`` () =
    let remote paths = Ok [ (List.head paths, "remote body") ]
    let deps = makeDeps [docs] [] Map.empty Map.empty Map.empty remote
    Assert.Equal("remote body", contentOf (Read.execute deps "docs:x/new.md"))

[<Fact>]
let ``errors when live fetched content is blocked`` () =
    let remote paths = Ok [ (List.head paths, "x") ]
    let deps = makeDeps [docs] [] Map.empty Map.empty Map.empty remote
    assertErrorContains "blocked" (Read.execute deps "docs:tool.exe")

[<Fact>]
let ``errors when source is unknown`` () =
    assertErrorContains "unknown source 'nope'" (Read.execute simpleDeps "nope:a/guide.md")

[<Fact>]
let ``errors when hash matches nothing`` () =
    assertErrorContains "no file found" (Read.execute simpleDeps "fffffff")

[<Fact>]
let ``errors with candidates when hash is ambiguous across sources`` () =
    let deps =
        makeDeps [docs; other]  []
            (Map.ofList ["docs", index ["a/guide.md", cached "files/aa"]; "other", index ["a/guide.md", cached "files/bb"]])
            Map.empty Map.empty noRemote
    let hash = Patterns.pathShortHash "a/guide.md"
    match Read.execute deps hash with
    | Ok _ -> Assert.Fail("Expected Error result")
    | Error e ->
        Assert.Contains("ambiguous", e)
        Assert.Contains("docs:a/guide.md", e)
        Assert.Contains("other:a/guide.md", e)

[<Fact>]
let ``errors when target is neither source path nor hash`` () =
    assertErrorContains "neither" (Read.execute simpleDeps "guide.md")

[<Fact>]
let ``path containing a colon is split on the first colon only`` () =
    let remote paths = Ok [ (List.head paths, "colon body") ]
    let deps = makeDeps [docs] [] Map.empty Map.empty Map.empty remote
    match Read.execute deps "docs:a:b.md" with
    | Ok d -> Assert.Equal("a:b.md", d.RemotePath)
    | Error e -> Assert.Fail e
