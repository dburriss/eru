module Eru.Tests.SiteGeneratorGraphTests

open System.IO
open System.Text.Json
open Xunit
open Eru
open Eru.Site
open Eru.Site.SiteGenerator

let private makeSource name : SourceConfig =
    { Name = name; Url = Some $"https://example.com/{name}.git"; Branch = None; Bundles = [] }

let private makeGlobal sources : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = []; DefaultInboxes = Map.empty; Defaults = None }

let private makeIndexEntry cacheRelPath title : IndexEntry =
    { Contributions = Map.empty; Tags = []; Description = None; LocalPath = Some "unused"; CacheRelPath = cacheRelPath
      ContentHash = None; Type = None; Title = title; OkfStatus = None
      Generated = None; Verified = []; StaleAfter = None; Resource = None }

// Fixed stand-in for the real Markdig-based adapter, same shape as LinkGraphTests.
let private fakeExtractLinks (content: string) : ExtractedLink list =
    System.Text.RegularExpressions.Regex.Matches(content, @"\[([^\]]*)\]\(([^)]*)\)")
    |> Seq.map (fun m ->
        let text = m.Groups.[1].Value.Trim()
        { Target = m.Groups.[2].Value; Description = (if text = "" then None else Some text); Kind = MarkdownLink })
    |> List.ofSeq

let private makeDeps
    (sourceIndex: Map<string, IndexEntry>)
    (content: Map<string, string>) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok (Some (makeGlobal [ makeSource "src" ]))
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
        ReadSourceIndex         = fun _ -> Ok (Some { Version = 1; SourceHeadSha = None; ConsecutiveShaCheckFailures = 0; Entries = sourceIndex })
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ relPath -> Ok (content |> Map.tryFind relPath)
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fakeExtractLinks
        GetRemoteHeadSha        = fun _ _ -> Error "not implemented"
        DirectoryExists        = fun _ -> true
        GetUtcNow        = fun () -> System.DateTimeOffset.UtcNow
        ListLocalFiles   = fun _ -> Ok []
        ListLocalDirectories = fun _ -> Ok []
        MoveLocalFile    = fun _ _ -> Ok ()
        RunAgent         = fun _ _ _ _ -> Ok { Response = ""; Timings = { InitializeMs = 0.0; SessionNewMs = 0.0; PromptMs = 0.0 } }
    }

let private cfg : EffectiveConfig = {
    Sources = [ makeSource "src" ]
    CommitOnPull = false
    StateFile = "eru.lock"
    Collections = []
    McpRefreshIntervalMinutes = 60
    BlockPatterns = []
    AllowPatterns = []
    AllowBinaries = false
    SiteIgnorePatterns = []
    Inboxes = Map.empty
    DefaultInbox = None
    InboxWatchIntervalSeconds = 30
}

[<Fact>]
let ``generate writes graph json and renders linked documents on file pages`` () =
    let sourceIndex =
        Map.ofList [
            "docs/index.md", makeIndexEntry (Some "files/hash1") (Some "Index")
            "docs/guide.md", makeIndexEntry (Some "files/hash2") (Some "Guide")
        ]
    let content =
        Map.ofList [
            "files/hash1", "see [the guide](guide.md)"
            "files/hash2", "no links here"
        ]
    let deps = makeDeps sourceIndex content

    let outDir = Path.Combine(Path.GetTempPath(), "eru-graph-test-" + System.Guid.NewGuid().ToString("N"))
    try
        let opts = { GenerateOptions.defaults with OutputDir = outDir }
        match SiteGenerator.generate deps cfg opts with
        | Error e -> Assert.Fail e
        | Ok () ->
            let graphJson = File.ReadAllText(Path.Combine(outDir, "data/graph.json"))
            let graph = JsonSerializer.Deserialize<JsonElement>(graphJson)
            let nodeIds =
                graph.GetProperty("nodes").EnumerateArray()
                |> Seq.map (fun n -> n.GetProperty("id").GetString())
                |> Set.ofSeq
            Assert.Contains("src:docs/index.md", nodeIds)
            Assert.Contains("src:docs/guide.md", nodeIds)

            let edges = graph.GetProperty("edges").EnumerateArray() |> List.ofSeq
            Assert.Contains(edges, fun e ->
                e.GetProperty("from").GetString() = "src:docs/index.md"
                && e.GetProperty("to").GetString() = "src:docs/guide.md"
                && e.GetProperty("description").GetString() = "the guide")

            let indexHtml = File.ReadAllText(Path.Combine(outDir, "files/src/docs_index.md.html"))
            Assert.Contains("Links from this document", indexHtml)
            Assert.Contains("files/src/docs_guide.md.html", indexHtml)

            let guideHtml = File.ReadAllText(Path.Combine(outDir, "files/src/docs_guide.md.html"))
            Assert.Contains("Links to this document", guideHtml)
            Assert.Contains("files/src/docs_index.md.html", guideHtml)
    finally
        if Directory.Exists outDir then Directory.Delete(outDir, true)
