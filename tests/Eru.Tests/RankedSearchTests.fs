module Eru.Tests.RankedSearchTests

open System
open System.IO
open System.Threading
open Xunit
open Eru
open Eru.Search
open Eru.Tests.SiteTestSupport

// A repo whose `knowledge/` directory holds `files`, plus a throwaway Ferret cache. The files
// echo the search feedback: generic health READMEs must not outrank the Redis one, retries must
// find the resilience README, and a generated index.md must not duplicate its README.
let private corpus : (string * string) list = [
    "stopwatch/README.md",     "# Stopwatch\n\nMock time in unit tests with an injectable stopwatch. Mocked clocks keep timing assertions deterministic."
    "resilience/README.md",    "# Resilience\n\nRetry failed AWS calls with exponential backoff. Configure retries per client."
    "health/README.md",        "# ApplicationHealth\n\nHealth check tests and checks for services. A health check reports the health of a service and its checks."
    "health-redis/README.md",  "# Redis health check\n\nA health check for Redis connections: ping the Redis server and report the result."
    "health-redis/index.md",   "# Redis health check\n\nA health check for Redis connections: ping the Redis server and report the result."
    "health-sqs/README.md",    "# SQS health check\n\nA health check for SQS queues."
    "notes/log.md",            "Redis health check log entry."
]

let private withRepo (files: (string * string) list) (f: Deps -> EffectiveConfig -> RankedSearch.Searcher -> 'a) : 'a =
    let root = Path.Combine(Path.GetTempPath(), "eru-ranked-" + Guid.NewGuid().ToString("N"))
    let cache = Path.Combine(root, "cache")
    try
        for rel, text in files do
            let full = Path.Combine(root, "knowledge", rel)
            Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore
            File.WriteAllText(full, text)
        let deps = { makeDeps Map.empty Map.empty Map.empty with GetCwd = fun () -> root }
        use searcher = new RankedSearch.Searcher(cache)
        f deps EffectiveConfig.empty searcher
    finally
        try Directory.Delete(root, true) with _ -> ()

let private search (deps: Deps) (eff: EffectiveConfig) (searcher: RankedSearch.Searcher) query tags limit mode =
    let request : RankedSearch.Request = { Query = query; Tags = tags; Mode = mode; Limit = limit }
    match RankedSearch.execute searcher deps eff (deps.GetCwd()) request CancellationToken.None with
    | Ok r -> r
    | Error e -> failwith e

let private paths (r: RankedSearch.RankedResult) =
    r.Hits |> List.map (fun h -> h.Candidate.RelPath.Replace('\\', '/'))

[<Fact>]
let ``a file with the rare terms outranks generic files sharing common ones`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "health check redis" [] 10 None
        Assert.EndsWith("health-redis/README.md", List.head (paths r)))

[<Fact>]
let ``retries and aws find the resilience readme first`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "retries for AWS calls" [] 10 None
        Assert.EndsWith("resilience/README.md", List.head (paths r)))

[<Fact>]
let ``a word form matches its stem`` () =
    withRepo corpus (fun deps eff searcher ->
        // The stopwatch README says "Mock" and "Mocked"; the query uses neither exact form of "mock time".
        let r = search deps eff searcher "mocked time unit tests" [] 10 None
        Assert.EndsWith("stopwatch/README.md", List.head (paths r)))

[<Fact>]
let ``generated index and log files are ignored by default`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "health check redis" [] 20 None
        Assert.DoesNotContain(paths r, fun p -> p.EndsWith "index.md" || p.EndsWith "log.md")
        Assert.Contains(paths r, fun p -> p.EndsWith "health-redis/README.md"))

[<Fact>]
let ``an empty ignore list lets index files through`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps { eff with SearchIgnorePatterns = [] } searcher "health check redis" [] 20 None
        Assert.Contains(paths r, fun p -> p.EndsWith "index.md"))

[<Fact>]
let ``limit bounds the hits and sets Truncated`` () =
    withRepo corpus (fun deps eff searcher ->
        let small = search deps eff searcher "health" [] 1 None
        Assert.Equal(1, small.Hits.Length)
        Assert.True small.Truncated
        let big = search deps eff searcher "health" [] 50 None
        Assert.True(big.Hits.Length > 1)
        Assert.False big.Truncated)

[<Fact>]
let ``hits carry a score and excerpts, best first`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "health check redis" [] 10 None
        let scores = r.Hits |> List.map (fun h -> h.Score)
        Assert.True(scores |> List.forall (fun s -> s > 0.0))
        Assert.Equal<float list>(scores |> List.sortDescending, scores)
        let top = List.head r.Hits
        Assert.NotEmpty top.Excerpts
        Assert.True(top.Excerpts.Length <= 3))

[<Fact>]
let ``tags narrow the ranked files and Truncated reflects the narrowed set`` () =
    let tagged = [
        "a.md", "---\ntags: [aws]\n---\nRetry the call.\n"
        "b.md", "---\ntags: [aws]\n---\nRetry with backoff.\n"
        "c.md", "---\ntags: [azure]\n---\nRetry the request.\n"
    ]
    withRepo tagged (fun deps eff searcher ->
        let r = search deps eff searcher "retry" [ "aws" ] 10 None
        Assert.Equal(2, r.Hits.Length)
        Assert.All(r.Hits, fun h -> Assert.Contains("aws", h.Candidate.Tags))
        Assert.False r.Truncated
        let one = search deps eff searcher "retry" [ "aws" ] 1 None
        Assert.Equal(1, one.Hits.Length)
        Assert.True one.Truncated)

[<Fact>]
let ``an empty query lists files in path order without scores`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "" [] 3 None
        Assert.Equal(3, r.Hits.Length)
        Assert.True r.Truncated
        Assert.All(r.Hits, fun h -> Assert.Equal(0.0, h.Score); Assert.Empty h.Excerpts)
        let p = paths r
        Assert.Equal<string list>(p |> List.sortWith (fun a b -> String.CompareOrdinal(a, b)), p))

[<Fact>]
let ``a semantic mode falls back to lexical with a warning when the model is missing`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "retries for AWS calls" [] 5 (Some Hybrid)
        if searcher.IsModelAvailable then
            Assert.Equal(Hybrid, r.Mode)
        else
            Assert.Equal(Lex, r.Mode)
            Assert.Single r.Warnings |> ignore
            Assert.Contains("embedding model", List.head r.Warnings)
            Assert.EndsWith("resilience/README.md", List.head (paths r)))

[<Fact>]
let ``a query of only stop words matches nothing instead of everything`` () =
    withRepo corpus (fun deps eff searcher ->
        let r = search deps eff searcher "the of and" [] 10 None
        Assert.Empty r.Hits)

// ── pure helpers ──────────────────────────────────────────────────────────────

let private candidate rel remote : CandidateFile =
    { AbsPath = "/x/" + rel; RelPath = rel; RemotePath = remote; Source = Cache
      SourceName = Some "src"; Tags = []; Description = None }

[<Fact>]
let ``ignore patterns without a slash match the file name at any depth`` () =
    let cs = [ candidate "src/a/index.md" (Some "a/index.md"); candidate "src/a/README.md" (Some "a/README.md"); candidate "knowledge/log.md" None ]
    let kept = RankedSearch.applyIgnore [ "index.md"; "log.md" ] cs |> List.map (fun c -> c.RelPath)
    Assert.Equal<string list>([ "src/a/README.md" ], kept)

[<Fact>]
let ``ignore patterns with a slash match the full path`` () =
    let cs = [ candidate "src/drafts/a.md" (Some "drafts/a.md"); candidate "src/docs/a.md" (Some "docs/a.md") ]
    let kept = RankedSearch.applyIgnore [ "drafts/**" ] cs |> List.map (fun c -> c.RelPath)
    Assert.Equal<string list>([ "src/docs/a.md" ], kept)

[<Fact>]
let ``metadata-only candidates match on path or description, any term`` () =
    let c = { candidate "src/docs/redis.md" (Some "docs/redis.md") with Description = Some "Cache health probes" }
    Assert.True(RankedSearch.matchesMetadata [ "redis" ] c)
    Assert.True(RankedSearch.matchesMetadata [ "nomatch"; "PROBES".ToLowerInvariant() ] c)
    Assert.False(RankedSearch.matchesMetadata [ "kafka" ] c)
    Assert.True(RankedSearch.matchesMetadata [] c)

[<Fact>]
let ``tags are matched case-insensitively and all must be present`` () =
    Assert.True(RankedSearch.hasTags [ "aws"; "retry" ] [ "AWS"; "Retry"; "other" ])
    Assert.False(RankedSearch.hasTags [ "aws"; "retry" ] [ "aws" ])
    Assert.True(RankedSearch.hasTags [] [])

[<Fact>]
let ``the configured mode applies when nothing overrides it`` () =
    // ERU_SEARCH_MODE and ERU_SEARCH_BACKEND are unset in the test environment.
    Assert.Equal(Ok Hybrid, RankedSearch.resolveMode None { EffectiveConfig.empty with SearchMode = Hybrid })
    Assert.Equal(Ok Sem, RankedSearch.resolveMode (Some Sem) { EffectiveConfig.empty with SearchMode = Hybrid })
