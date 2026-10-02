module Eru.Tests.BundleDiscoveryTests

open Xunit
open Eru

let private makeDeps
    (listRemoteFiles: string -> string option -> string option -> Result<string list, string>)
    (fetchRemoteContent: string -> string -> string list -> Result<(string * string) list, string>) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok None
        ReadLocalConfig         = fun () -> Ok None
        WriteLocalConfig        = fun _ -> Ok ()
        WriteGlobalConfig       = fun _ -> Ok ()
        ReadLockEntries         = fun _ -> Ok []
        WriteLockEntries        = fun _ _ -> Ok ()
        FetchRemoteContent      = fetchRemoteContent
        ListRemoteTopLevel      = fun _ _ -> Ok []
        ListRemoteFiles         = listRemoteFiles
        WriteLocalFile          = fun _ _ -> Ok ()
        ReadLocalFile           = fun _ -> Ok None
        DeleteLocalFile         = fun _ -> Ok ()
        HashContent             = fun s -> $"hash:{s}"
        GetCwd                  = fun () -> "/tmp"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok None
        WriteLocalManifest      = fun _ -> Ok ()
        ResolveLocalGlob        = fun _ -> []
        ReadSourceIndex         = fun _ -> Ok None
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = Eru.Adapters.YamlAdapter.parse
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

let private rootBundle : Bundle = { Path = ""; Kind = Okf }
let private nestedBundle : Bundle = { Path = "docs/knowledge"; Kind = Okf }

[<Fact>]
let ``walkBundle classifies index and log files with empty metadata`` () =
    let listFiles _ _ _ = Ok [ "index.md"; "log.md" ]
    let fetch _ _ paths =
        Ok (paths |> List.map (fun p ->
            match p with
            | "index.md" -> p, "---\nokf_version: \"1.0\"\n---\n"
            | "log.md"   -> p, "## 2026-01-01\n\nDid stuff."
            | other      -> other, ""))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok files ->
        Assert.Equal(2, files.Length)
        for f in files do
            Assert.Empty(f.Contribution.Tags)
            Assert.Equal(None, f.Contribution.Description)
            Assert.Equal(None, f.Type)

[<Fact>]
let ``walkBundle extracts frontmatter from concept files`` () =
    let listFiles _ _ _ = Ok [ "adr-001.md" ]
    let fetch _ _ paths =
        Ok (paths |> List.map (fun p -> p, "---\ntype: ADR\ntitle: Use F#\ntags: [dotnet, architecture]\ndescription: Why we chose F#\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok [ file ] ->
        Assert.Equal("adr-001.md", file.RemotePath)
        Assert.Equal<string list>(["dotnet"; "architecture"], file.Contribution.Tags)
        Assert.Equal(Some "Why we chose F#", file.Contribution.Description)
        Assert.Equal(Some "ADR", file.Type)
        Assert.Equal(Some "Use F#", file.Title)
    | Ok other -> Assert.Fail $"expected exactly one file, got {other.Length}"

[<Fact>]
let ``walkBundle skips concept files without a type`` () =
    let listFiles _ _ _ = Ok [ "typed.md"; "untyped.md"; "index.md" ]
    let fetch _ _ paths =
        Ok (paths |> List.map (fun p -> p, if p = "untyped.md" then "---\ntitle: No type\n---\n" else "---\ntype: ADR\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok files ->
        Assert.Equal<string list>([ "typed.md"; "index.md" ], files |> List.map (fun f -> f.RemotePath))

[<Fact>]
let ``walkBundle re-prefixes bundle-relative paths with the bundle's own path`` () =
    // ListRemoteFiles returns paths relative to the bundle root; RemotePath on the
    // returned DiscoveredFile must be repo-root-relative.
    let listFiles _ _ basePath =
        Assert.Equal(Some "docs/knowledge", basePath)
        Ok [ "adr-001.md" ]
    let fetchedPaths = ref []
    let fetch _ _ paths =
        fetchedPaths.Value <- paths
        Ok (paths |> List.map (fun p -> p, "---\ntype: ADR\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" nestedBundle with
    | Error e -> Assert.Fail e
    | Ok [ file ] ->
        Assert.Equal("docs/knowledge/adr-001.md", file.RemotePath)
        Assert.Equal<string list>([ "docs/knowledge/adr-001.md" ], fetchedPaths.Value)
    | Ok other -> Assert.Fail $"expected exactly one file, got {other.Length}"

[<Fact>]
let ``walkBundle only considers markdown candidate paths`` () =
    let seenPaths = ref []
    let listFiles _ _ _ = Ok [ "adr-001.md"; "diagram.png"; "notes.txt" ]
    let fetch _ _ paths =
        seenPaths.Value <- paths
        Ok (paths |> List.map (fun p -> p, "---\ntype: ADR\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok files ->
        Assert.Equal(1, files.Length)
        Assert.Equal<string list>([ "adr-001.md" ], seenPaths.Value)

[<Fact>]
let ``walkBundle returns empty list when the bundle has no markdown files`` () =
    let listFiles _ _ _ = Ok []
    let fetch _ _ _ = Error "should not be called"
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok files -> Assert.Empty files

[<Fact>]
let ``walkBundle propagates a ListRemoteFiles error`` () =
    let listFiles _ _ _ = Error "network down"
    let fetch _ _ _ = Error "should not be called"
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Equal("network down", e)
    | Ok _ -> Assert.Fail "expected Error"

[<Fact>]
let ``walkBundle treats README.md as non-concept with empty metadata`` () =
    let listFiles _ _ _ = Ok [ "README.md" ]
    let fetch _ _ paths = Ok (paths |> List.map (fun p -> p, "---\ntype: ADR\ntags: [x]\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [] "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok [ f ] ->
        Assert.Equal(None, f.Type)
        Assert.Empty(f.Contribution.Tags)
    | Ok other -> Assert.Fail $"expected one file, got {other.Length}"

[<Fact>]
let ``walkBundle skips dot-directories and ignore-pattern matches`` () =
    let listFiles _ _ _ = Ok [ ".github/x.md"; ".claude/skills/y.md"; "inbox/n.md"; "apm_modules/p/z.md"; "adr.md" ]
    let mutable fetched : string list = []
    let fetch _ _ (paths: string list) =
        fetched <- paths
        Ok (paths |> List.map (fun p -> p, "---\ntype: ADR\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps Config.defaultOkfIgnorePatterns "kb" "https://x.com" "main" rootBundle with
    | Error e -> Assert.Fail e
    | Ok files ->
        Assert.Equal<string list>([ "adr.md" ], files |> List.map (fun f -> f.RemotePath))
        Assert.Equal<string list>([ "adr.md" ], fetched)

[<Fact>]
let ``walkBundle applies ignore patterns to bundle-relative paths`` () =
    let listFiles _ _ _ = Ok [ "inbox/n.md"; "adr.md" ]
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "---\ntype: ADR\n---\n"))
    let deps = makeDeps listFiles fetch
    match BundleDiscovery.walkBundle deps [ "inbox/**" ] "kb" "https://x.com" "main" nestedBundle with
    | Error e -> Assert.Fail e
    | Ok files -> Assert.Equal<string list>([ "docs/knowledge/adr.md" ], files |> List.map (fun f -> f.RemotePath))
