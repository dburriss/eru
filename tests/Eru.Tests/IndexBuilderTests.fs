module Eru.Tests.IndexBuilderTests

open Xunit
open Eru
open Eru.Site

let private emptyIndexEntry : IndexEntry = {
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
        ReadSourceIndex         = fun _ -> Ok (Some index)
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fun _ -> []
    }

let private cfg (sourceName: string) : EffectiveConfig = {
    Sources = [ { Name = sourceName; Url = None; Branch = None; BasePath = None } ]
    CommitOnPull = false
    StateFile = "eru.lock"
    Collections = []
    McpRefreshIntervalMinutes = 60
    BlockPatterns = []
    AllowPatterns = []
    AllowBinaries = false
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
