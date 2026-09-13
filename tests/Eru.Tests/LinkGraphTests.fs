module Eru.Tests.LinkGraphTests

open Xunit
open Eru
open Eru.LinkGraph

// ── Helpers ───────────────────────────────────────────────────────────────────

let private makeSource name url : SourceConfig =
    { Name = name; Url = Some url; Branch = None; BasePath = None }

let private makeGlobal sources : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = []; Defaults = None }

let private makeIndexEntry cacheRelPath : IndexEntry =
    { Tags = []; Description = None; LocalPath = None; CacheRelPath = cacheRelPath
      ContentHash = None; Type = None; Title = None; OkfStatus = None
      Generated = None; Verified = []; StaleAfter = None; Resource = None }

// Fixed stand-in for the real Markdig-based adapter: pulls "(...)" targets out of
// "[text](target)" markdown link syntax, just enough to drive execute's tests.
let private fakeExtractLinks (content: string) : string list =
    System.Text.RegularExpressions.Regex.Matches(content, @"\]\(([^)]*)\)")
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> List.ofSeq

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
        ReadSourceIndex         = fun name -> Ok (sourceIndex |> Map.tryFind name)
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun sourceName cacheRelPath -> Ok (content |> Map.tryFind (sourceName, cacheRelPath))
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fakeExtractLinks
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
        Assert.Contains(result.Edges, fun e -> e.From = fromIndex && e.To = InternalNode { Source = "kb"; RemotePath = "docs/guide.md" })
        Assert.Contains(result.Edges, fun e -> e.From = fromIndex && e.To = ExternalNode "https://example.com")

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
