module Eru.Tests.IndexBuilderTests

open Xunit
open Eru
open Eru.Site

let private emptyIndexEntry : IndexEntry = {
    Contributions = Map.empty
    Tags         = []
    Description  = None
    LocalPath    = None
    CacheRelPath = None
    ContentHash  = None
    Type         = None
    Title        = None
    OkfStatus    = None
    Generated    = None
    Verified     = []
    StaleAfter   = None
    Resource     = None
}

let private makeDeps (index: Map<string, IndexEntry>) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok None
        ReadLocalConfig         = fun () -> Ok None
        WriteLocalConfig        = fun _ -> Ok ()
        WriteGlobalConfig       = fun _ -> Ok ()
        ReadLockEntries         = fun _ -> Ok []
        WriteLockEntries        = fun _ _ -> Ok ()
        FetchRemoteContent      = fun _ _ paths -> Ok (paths |> List.map (fun p -> (p, "")))
        ListRemoteTopLevel      = fun _ _ -> Ok []
        ListRemoteFiles         = fun _ _ _ -> Ok []
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
        ReadSourceIndex         = fun _ -> Ok (Some { Version = 1; SourceHeadSha = None; ConsecutiveShaCheckFailures = 0; Entries = index })
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
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

let private cfg (sourceName: string) : EffectiveConfig = {
    Sources = [ { Name = sourceName; Url = None; Branch = None; Bundles = [] } ]
    CommitOnPull = false
    StateFile = "eru.lock"
    Collections = []
    McpRefreshIntervalMinutes = 60
    BlockPatterns = []
    AllowPatterns = []
    AllowBinaries = false
    SiteIgnorePatterns = []
    SiteHideEmptyBundles = false
    OkfIgnorePatterns = []
    Inboxes = Map.empty
    DefaultInbox = None
    InboxWatchIntervalSeconds = 30
}

[<Fact>]
let ``title falls back to filename when frontmatter title is absent`` () =
    let index = Map.ofList [ "docs/orders.md", { emptyIndexEntry with LocalPath = Some "orders.md" } ]
    let deps = makeDeps index
    match IndexBuilder.buildModel deps (cfg "src") with
    | Ok model ->
        let doc = model.Documents |> List.exactlyOne
        Assert.Equal("orders.md", doc.Title)
    | Error e -> Assert.Fail e

[<Fact>]
let ``frontmatter title wins over filename`` () =
    let index = Map.ofList [ "docs/orders.md", { emptyIndexEntry with LocalPath = Some "orders.md"; Title = Some "Customer Orders" } ]
    let deps = makeDeps index
    match IndexBuilder.buildModel deps (cfg "src") with
    | Ok model ->
        let doc = model.Documents |> List.exactlyOne
        Assert.Equal("Customer Orders", doc.Title)
    | Error e -> Assert.Fail e

[<Fact>]
let ``documents group into Types by frontmatter type`` () =
    let index =
        Map.ofList [
            "a.md", { emptyIndexEntry with LocalPath = Some "a.md"; Type = Some "BigQuery Table" }
            "b.md", { emptyIndexEntry with LocalPath = Some "b.md"; Type = Some "BigQuery Table" }
            "c.md", { emptyIndexEntry with LocalPath = Some "c.md"; Type = None }
        ]
    let deps = makeDeps index
    match IndexBuilder.buildModel deps (cfg "src") with
    | Ok model ->
        Assert.Equal(1, model.Types.Length)
        Assert.Equal("BigQuery Table", model.Types.[0].Name)
        Assert.Equal(2, model.Types.[0].FileCount)
    | Error e -> Assert.Fail e

[<Fact>]
let ``OKF fields pass through from IndexEntry to SiteDocument`` () =
    let generated : Frontmatter.ActorAt = { By = "human:ahormati"; At = None }
    let index =
        Map.ofList [
            "a.md", { emptyIndexEntry with
                        LocalPath  = Some "a.md"
                        Type       = Some "Playbook"
                        OkfStatus  = Some "stable"
                        Generated  = Some generated
                        Verified   = [ generated ]
                        Resource   = Some "https://example.com/a" }
        ]
    let deps = makeDeps index
    match IndexBuilder.buildModel deps (cfg "src") with
    | Ok model ->
        let doc = model.Documents |> List.exactlyOne
        Assert.Equal(Some "Playbook", doc.Type)
        Assert.Equal(Some "stable", doc.Status)
        Assert.Equal(Some generated, doc.Generated)
        Assert.Equal<Frontmatter.ActorAt list>([ generated ], doc.Verified)
        Assert.Equal(Some "https://example.com/a", doc.Resource)
    | Error e -> Assert.Fail e

[<Fact>]
let ``files matching SiteIgnorePatterns are excluded from the model`` () =
    let index =
        Map.ofList [
            "index.md",   { emptyIndexEntry with LocalPath = Some "index.md" }
            "log.md",     { emptyIndexEntry with LocalPath = Some "log.md" }
            "content.md", { emptyIndexEntry with LocalPath = Some "content.md" }
        ]
    let deps = makeDeps index
    let cfgWithIgnore = { cfg "src" with SiteIgnorePatterns = [ "index.md"; "log.md" ] }
    match IndexBuilder.buildModel deps cfgWithIgnore with
    | Ok model ->
        Assert.Equal(1, model.Documents.Length)
        Assert.Equal("content.md", model.Documents.[0].RemotePath)
    | Error e -> Assert.Fail e

[<Fact>]
let ``default SiteIgnorePatterns exclude README.md at any depth`` () =
    let index =
        Map.ofList [
            "README.md",      { emptyIndexEntry with LocalPath = Some "README.md" }
            "docs/README.md", { emptyIndexEntry with LocalPath = Some "docs/README.md" }
            "content.md",     { emptyIndexEntry with LocalPath = Some "content.md" }
        ]
    let deps = makeDeps index
    let cfgDefault = { cfg "src" with SiteIgnorePatterns = Config.defaultSiteIgnorePatterns }
    match IndexBuilder.buildModel deps cfgDefault with
    | Ok model -> Assert.Equal<string list>([ "content.md" ], model.Documents |> List.map (fun d -> d.RemotePath))
    | Error e -> Assert.Fail e

// ── Bundles ──────────────────────────────────────────────────────────────────

let private cfgWithBundles (bundles: Bundle list) : EffectiveConfig =
    let c = cfg "kb"
    { c with Sources = [ { c.Sources.[0] with Bundles = bundles } ] }

let private bundleIndex =
    Map.ofList [
        "top.md",           { emptyIndexEntry with LocalPath = Some "top.md" }
        "sub/inner.md",     { emptyIndexEntry with LocalPath = Some "inner.md" }
        "sub/deep/leaf.md", { emptyIndexEntry with LocalPath = Some "leaf.md" }
    ]

let private bundleOf (path: string) (model: SiteModel) =
    (model.Documents |> List.find (fun d -> d.RemotePath = path)).Bundle

[<Fact>]
let ``a file belongs to its most specific bundle only`` () =
    let bundles = [ { Path = ""; Kind = Okf }; { Path = "sub"; Kind = Okf }; { Path = "sub/deep"; Kind = Okf } ]
    match IndexBuilder.buildModel (makeDeps bundleIndex) (cfgWithBundles bundles) with
    | Ok model ->
        Assert.Equal(Some "kb", bundleOf "top.md" model)
        Assert.Equal(Some "kb/sub", bundleOf "sub/inner.md" model)
        Assert.Equal(Some "kb/sub/deep", bundleOf "sub/deep/leaf.md" model)
        let counts = model.Bundles |> List.map (fun b -> b.Name, b.FileCount)
        Assert.Equal<(string * int) list>([ "kb", 1; "kb/sub", 1; "kb/sub/deep", 1 ], counts)
    | Error e -> Assert.Fail e

[<Fact>]
let ``a nested okf bundle wins over a manifest root bundle`` () =
    let bundles = [ { Path = ""; Kind = Manifest }; { Path = "sub"; Kind = Okf } ]
    match IndexBuilder.buildModel (makeDeps bundleIndex) (cfgWithBundles bundles) with
    | Ok model ->
        Assert.Equal(Some "kb/sub", bundleOf "sub/inner.md" model)
        Assert.Equal("manifest", (model.Bundles |> List.find (fun b -> b.Name = "kb")).Kind)
        Assert.Equal("okf", (model.Bundles |> List.find (fun b -> b.Name = "kb/sub")).Kind)
    | Error e -> Assert.Fail e

[<Fact>]
let ``a file outside every bundle has no bundle and an empty bundle is still listed`` () =
    let bundles = [ { Path = "sub"; Kind = Okf }; { Path = "empty"; Kind = Okf } ]
    match IndexBuilder.buildModel (makeDeps bundleIndex) (cfgWithBundles bundles) with
    | Ok model ->
        Assert.Equal(None, bundleOf "top.md" model)
        Assert.Equal(0, (model.Bundles |> List.find (fun b -> b.Name = "kb/empty")).FileCount)
        Assert.Equal(2, (model.Bundles |> List.find (fun b -> b.Name = "kb/sub")).FileCount)
    | Error e -> Assert.Fail e

[<Fact>]
let ``bundleDisplayName namespaces by source and path`` () =
    Assert.Equal("kb", IndexBuilder.bundleDisplayName "kb" "")
    Assert.Equal("kb/knowledge/software", IndexBuilder.bundleDisplayName "kb" "knowledge/software/")

[<Fact>]
let ``siteHideEmptyBundles drops bundles with no listed files`` () =
    let bundles = [ { Path = "sub"; Kind = Okf }; { Path = "empty"; Kind = Okf } ]
    let hiding = { cfgWithBundles bundles with SiteHideEmptyBundles = true }
    match IndexBuilder.buildModel (makeDeps bundleIndex) hiding with
    | Ok model -> Assert.Equal<string list>([ "kb/sub" ], model.Bundles |> List.map (fun b -> b.Name))
    | Error e -> Assert.Fail e

[<Fact>]
let ``siteHideEmptyBundles also hides a bundle whose files are all site-ignored`` () =
    let bundles = [ { Path = "sub"; Kind = Okf } ]
    let hiding = { cfgWithBundles bundles with SiteHideEmptyBundles = true; SiteIgnorePatterns = [ "sub/**" ] }
    match IndexBuilder.buildModel (makeDeps bundleIndex) hiding with
    | Ok model -> Assert.Empty model.Bundles
    | Error e -> Assert.Fail e
