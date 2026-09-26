module Eru.Tests.LinkGraphTests

open Xunit
open Eru
open Eru.LinkGraph

// ── Helpers ───────────────────────────────────────────────────────────────────

let private makeSource name url : SourceConfig =
    { Name = name; Url = Some url; Branch = None; Bundles = [] }

let private makeGlobal sources : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = []; Defaults = None }

let private makeIndexEntry cacheRelPath : IndexEntry =
    { Contributions = Map.empty; Tags = []; Description = None; LocalPath = None; CacheRelPath = cacheRelPath
      ContentHash = None; Type = None; Title = None; OkfStatus = None
      Generated = None; Verified = []; StaleAfter = None; Resource = None }

let private makeIndexEntryWithTitle cacheRelPath title : IndexEntry =
    { makeIndexEntry cacheRelPath with Title = Some title }

// Fixed stand-in for the real Markdig-based adapter: pulls "[text](target)" markdown
// links and "[[target]]" / "[[target|text]]" wikilinks out of raw content, just
// enough to drive execute's tests.
let private fakeExtractLinks (content: string) : ExtractedLink list =
    let markdownLinks =
        System.Text.RegularExpressions.Regex.Matches(content, @"\[([^\]]*)\]\(([^)]*)\)")
        |> Seq.map (fun m ->
            let text = m.Groups.[1].Value.Trim()
            { Target = m.Groups.[2].Value; Description = (if text = "" then None else Some text); Kind = MarkdownLink })
        |> List.ofSeq
    let wikilinks =
        System.Text.RegularExpressions.Regex.Matches(content, @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]")
        |> Seq.map (fun m ->
            let target = m.Groups.[1].Value.Trim()
            let description = if m.Groups.[2].Success then Some (m.Groups.[2].Value.Trim()) else None
            { Target = target; Description = description; Kind = Wikilink })
        |> List.ofSeq
    markdownLinks @ wikilinks

let private makeDeps
    (sourceIndex: Map<string, Map<string, IndexEntry>>)
    (content: Map<string * string, string>) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok (Some (makeGlobal (sourceIndex |> Map.toList |> List.map (fun (n, _) -> makeSource n $"https://example.com/{n}.git"))))
        ReadLocalConfig         = fun () -> Ok None
        WriteLocalConfig        = fun _ -> Ok ()
        WriteGlobalConfig       = fun _ -> Ok ()
        ReadLockEntries         = fun _ -> Ok []
        WriteLockEntries        = fun _ _ -> Ok ()
        FetchRemoteContent      = fun _ _ paths -> Ok (paths |> List.map (fun p -> (p, $"content:{p}")))
        ListRemoteTopLevel      = fun _ _ -> Ok []
        ListRemoteFiles         = fun _ _ _ -> Ok []
        WriteLocalFile          = fun _ _ -> Ok ()
        ReadLocalFile           = fun _ -> Ok None
        DeleteLocalFile         = fun _ -> Ok ()
        HashContent             = fun s -> $"sha256:{s}"
        GetCwd                  = fun () -> "/tmp"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok None
        WriteLocalManifest      = fun _ -> Ok ()
        ResolveLocalGlob        = fun _ -> []
        ReadSourceIndex         = fun name ->
            Ok (sourceIndex |> Map.tryFind name |> Option.map (fun entries ->
                { Version = 1; SourceHeadSha = None; ConsecutiveShaCheckFailures = 0; Entries = entries }))
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun sourceName cacheRelPath -> Ok (content |> Map.tryFind (sourceName, cacheRelPath))
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fakeExtractLinks
        GetRemoteHeadSha        = fun _ _ -> Error "not implemented"
    }

// ── resolveLink ───────────────────────────────────────────────────────────────

[<Fact>]
let ``http url resolves to external node`` () =
    match resolveLink "kb" "docs/index.md" "https://example.com/page" with
    | Some (ExternalNode url) -> Assert.Equal("https://example.com/page", url)
    | other -> Assert.Fail($"expected ExternalNode, got %A{other}")

[<Fact>]
let ``https url resolves to external node`` () =
    match resolveLink "kb" "docs/index.md" "http://example.com/page" with
    | Some (ExternalNode _) -> ()
    | other -> Assert.Fail($"expected ExternalNode, got %A{other}")

[<Fact>]
let ``anchor only link is dropped`` () =
    Assert.Equal(None, resolveLink "kb" "docs/index.md" "#section")

[<Fact>]
let ``mailto link is dropped`` () =
    Assert.Equal(None, resolveLink "kb" "docs/index.md" "mailto:someone@example.com")

[<Fact>]
let ``empty target is dropped`` () =
    Assert.Equal(None, resolveLink "kb" "docs/index.md" "")

[<Fact>]
let ``relative sibling path resolves within same directory`` () =
    match resolveLink "kb" "docs/index.md" "guide.md" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/guide.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

[<Fact>]
let ``dotdot path normalizes up a directory`` () =
    match resolveLink "kb" "docs/sub/page.md" "../guide.md" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/guide.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

[<Fact>]
let ``path escaping above root is dropped`` () =
    Assert.Equal(None, resolveLink "kb" "docs/index.md" "../../outside.md")

[<Fact>]
let ``anchor suffix on relative path is stripped before resolving`` () =
    match resolveLink "kb" "docs/index.md" "guide.md#section" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/guide.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

// ── execute ───────────────────────────────────────────────────────────────────

[<Fact>]
let ``execute builds nodes and edges from cached content`` () =
    let sourceIndex =
        Map.ofList [
            "kb", Map.ofList [
                "docs/index.md", makeIndexEntry (Some "files/hash1")
                "docs/guide.md", makeIndexEntry (Some "files/hash2")
            ]
        ]
    let content =
        Map.ofList [
            ("kb", "files/hash1"), "see [guide](guide.md) and [site](https://example.com)"
            ("kb", "files/hash2"), "no links here"
        ]
    let deps = makeDeps sourceIndex content

    match LinkGraph.execute deps { SourceFilter = None } with
    | Error e -> Assert.Fail(e)
    | Ok result ->
        Assert.Equal(3, result.Nodes.Length)
        Assert.Equal(2, result.Edges.Length)
        let fromIndex = InternalNode { Source = "kb"; RemotePath = "docs/index.md" }
        Assert.Contains(result.Edges, fun e -> e.From = fromIndex && e.To = InternalNode { Source = "kb"; RemotePath = "docs/guide.md" } && e.Description = Some "guide")
        Assert.Contains(result.Edges, fun e -> e.From = fromIndex && e.To = ExternalNode "https://example.com" && e.Description = Some "site")

[<Fact>]
let ``execute filters by source`` () =
    let sourceIndex =
        Map.ofList [
            "kb", Map.ofList [ "index.md", makeIndexEntry (Some "files/hash1") ]
            "other", Map.ofList [ "index.md", makeIndexEntry (Some "files/hash2") ]
        ]
    let content =
        Map.ofList [
            ("kb", "files/hash1"), "no links"
            ("other", "files/hash2"), "no links"
        ]
    let deps = makeDeps sourceIndex content

    match LinkGraph.execute deps { SourceFilter = Some "kb" } with
    | Error e -> Assert.Fail(e)
    | Ok result ->
        Assert.Equal(1, result.Nodes.Length)
        Assert.Equal(InternalNode { Source = "kb"; RemotePath = "index.md" }, result.Nodes.[0])

// ── resolveWikilink ───────────────────────────────────────────────────────────

[<Fact>]
let ``wikilink with path-shaped target resolves via step 1`` () =
    let titleIndex = { KnownPaths = Set.ofList [ "docs/guide.md" ]; ByKey = Map.empty }
    match resolveWikilink "kb" "docs/index.md" titleIndex "guide.md" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/guide.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

[<Fact>]
let ``wikilink with stem-only target falls back to filename stem match`` () =
    let titleIndex = { KnownPaths = Set.ofList [ "docs/guide.md" ]; ByKey = Map.ofList [ "guide", "docs/guide.md" ] }
    match resolveWikilink "kb" "docs/index.md" titleIndex "guide" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/guide.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

[<Fact>]
let ``wikilink target falls back to title match`` () =
    let titleIndex = { KnownPaths = Set.ofList [ "docs/guide.md" ]; ByKey = Map.ofList [ "the guide", "docs/guide.md" ] }
    match resolveWikilink "kb" "docs/index.md" titleIndex "The Guide" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/guide.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

[<Fact>]
let ``wikilink title match wins over stem match on collision`` () =
    // "other.md" has title "guide", and "docs/guide.md" is a different file with filename stem "guide".
    // Title-keyed entries are inserted after stem-keyed entries, so the title entry wins.
    let titleIndex = { KnownPaths = Set.ofList [ "docs/guide.md"; "docs/other.md" ]; ByKey = Map.ofList [ "guide", "docs/other.md" ] }
    match resolveWikilink "kb" "docs/index.md" titleIndex "guide" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/other.md" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

[<Fact>]
let ``wikilink with no path or title match falls back permissively to path resolution`` () =
    let titleIndex = { KnownPaths = Set.empty; ByKey = Map.empty }
    match resolveWikilink "kb" "docs/index.md" titleIndex "missing" with
    | Some (InternalNode id) -> Assert.Equal({ Source = "kb"; RemotePath = "docs/missing" }, id)
    | other -> Assert.Fail($"expected InternalNode, got %A{other}")

// ── wikilinks via fake adapter / execute ──────────────────────────────────────

[<Fact>]
let ``fakeExtractLinks parses wikilinks with and without description`` () =
    let links = fakeExtractLinks "see [[guide]] and [[guide|See the guide]]"
    Assert.Equal(2, links.Length)
    Assert.Contains(links, fun l -> l.Kind = Wikilink && l.Target = "guide" && l.Description = None)
    Assert.Contains(links, fun l -> l.Kind = Wikilink && l.Target = "guide" && l.Description = Some "See the guide")

[<Fact>]
let ``execute resolves stem-only wikilink through title index built from IndexEntry`` () =
    let sourceIndex =
        Map.ofList [
            "kb", Map.ofList [
                "docs/index.md", makeIndexEntry (Some "files/hash1")
                "docs/guide.md", makeIndexEntry (Some "files/hash2")
            ]
        ]
    let content =
        Map.ofList [
            ("kb", "files/hash1"), "see [[guide]]"
            ("kb", "files/hash2"), "no links here"
        ]
    let deps = makeDeps sourceIndex content

    match LinkGraph.execute deps { SourceFilter = None } with
    | Error e -> Assert.Fail(e)
    | Ok result ->
        let fromIndex = InternalNode { Source = "kb"; RemotePath = "docs/index.md" }
        Assert.Contains(result.Edges, fun e ->
            e.From = fromIndex
            && e.To = InternalNode { Source = "kb"; RemotePath = "docs/guide.md" }
            && e.Description = None)

[<Fact>]
let ``execute resolves wikilink via title from frontmatter`` () =
    let sourceIndex =
        Map.ofList [
            "kb", Map.ofList [
                "docs/index.md", makeIndexEntry (Some "files/hash1")
                "docs/other.md", makeIndexEntryWithTitle (Some "files/hash2") "The Guide"
            ]
        ]
    let content =
        Map.ofList [
            ("kb", "files/hash1"), "see [[The Guide|Read this]]"
            ("kb", "files/hash2"), "no links here"
        ]
    let deps = makeDeps sourceIndex content

    match LinkGraph.execute deps { SourceFilter = None } with
    | Error e -> Assert.Fail(e)
    | Ok result ->
        let fromIndex = InternalNode { Source = "kb"; RemotePath = "docs/index.md" }
        Assert.Contains(result.Edges, fun e ->
            e.From = fromIndex
            && e.To = InternalNode { Source = "kb"; RemotePath = "docs/other.md" }
            && e.Description = Some "Read this")
