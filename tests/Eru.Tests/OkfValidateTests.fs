module Eru.Tests.OkfValidateTests

open Xunit
open Eru
open Eru.Adapters

let private makeDeps (files: Map<string, string>) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok None
        ReadLocalConfig    = fun () -> Ok None
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun _ _ -> Ok ()
        ReadLocalFile       = fun path ->
            let rel = path.Replace("/bundle/", "")
            Ok (Map.tryFind rel files)
        DeleteLocalFile     = fun _ -> Ok ()
        HashContent         = fun s -> $"sha256:{s}"
        GetCwd              = fun () -> "/tmp"
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
        ParseYamlBlock          = YamlAdapter.parse
        ListMarkdownFiles       = fun _ -> Ok (files |> Map.toList |> List.map fst)
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

let private run (files: (string * string) list) : OkfValidate.ValidateResult =
    let deps = makeDeps (Map.ofList files)
    match OkfValidate.execute deps { Path = "/bundle"; IgnorePatterns = []; StrictLinks = false } with
    | Error e -> Assert.Fail($"expected Ok, got Error {e}"); failwith "unreachable"
    | Ok result -> result

let private conceptWithType =
    "---\ntype: table\n---\n# Orders\n"

[<Fact>]
let ``clean bundle has zero violations`` () =
    let result = run [ "tables/orders.md", conceptWithType ]
    Assert.Empty(result.Violations)
    Assert.Equal(1, result.TotalConcepts)

[<Fact>]
let ``concept with no frontmatter block is a violation`` () =
    let result = run [ "tables/orders.md", "# Orders\nno frontmatter here\n" ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("no-frontmatter", result.Violations.[0].Rule)
    Assert.Equal(0, result.TotalConcepts)

[<Fact>]
let ``concept with missing type field is a violation`` () =
    let result = run [ "tables/orders.md", "---\ndescription: foo\n---\nbody\n" ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("missing-type", result.Violations.[0].Rule)

[<Fact>]
let ``concept with empty type field is a violation`` () =
    let result = run [ "tables/orders.md", "---\ntype: \"\"\n---\nbody\n" ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("missing-type", result.Violations.[0].Rule)

[<Fact>]
let ``concept with malformed yaml frontmatter is a violation`` () =
    let result = run [ "tables/orders.md", "---\ntype: [unterminated\n---\nbody\n" ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("malformed-yaml", result.Violations.[0].Rule)

[<Fact>]
let ``bundle-root index.md with only okf_version is conformant`` () =
    let result = run [ "index.md", "---\nokf_version: \"0.2\"\n---\n# Bundle\n" ]
    Assert.Empty(result.Violations)

[<Fact>]
let ``non-root index.md with any frontmatter is a violation`` () =
    let result = run [ "tables/index.md", "---\nokf_version: \"0.2\"\n---\n# Tables\n" ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("index-frontmatter", result.Violations.[0].Rule)

[<Fact>]
let ``root index.md with a key other than okf_version is a violation`` () =
    let result = run [ "index.md", "---\ntype: foo\n---\n# Bundle\n" ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("index-frontmatter", result.Violations.[0].Rule)

[<Fact>]
let ``index.md with no frontmatter at all is fine`` () =
    let result = run [ "tables/index.md", "# Tables\n* [Orders](orders.md) - order records\n" ]
    Assert.Empty(result.Violations)

[<Fact>]
let ``log.md with a bad date heading is a violation`` () =
    let log = "# Directory Update Log\n\n## 2026-5-22\n* **Update**: did a thing\n"
    let result = run [ "log.md", log ]
    Assert.Equal(1, result.Violations.Length)
    Assert.Equal("log-date-heading", result.Violations.[0].Rule)

[<Fact>]
let ``log.md with a good date heading has no violations`` () =
    let log = "# Directory Update Log\n\n## 2026-05-22\n* **Update**: did a thing\n"
    let result = run [ "log.md", log ]
    Assert.Empty(result.Violations)

[<Fact>]
let ``bare verified mapping is tolerated, not a violation`` () =
    let content = "---\ntype: table\nverified:\n  by: human:devon\n  at: \"2026-01-01T00:00:00Z\"\n---\nbody\n"
    let result = run [ "tables/orders.md", content ]
    Assert.Empty(result.Violations)
    Assert.Equal(1, result.TotalConcepts)

[<Fact>]
let ``unknown type value is not a violation`` () =
    let result = run [ "tables/orders.md", "---\ntype: totally-made-up-type\n---\nbody\n" ]
    Assert.Empty(result.Violations)

[<Fact>]
let ``unknown extra frontmatter key is not a violation`` () =
    let result = run [ "tables/orders.md", "---\ntype: table\nsome_extra_key: whatever\n---\nbody\n" ]
    Assert.Empty(result.Violations)

[<Fact>]
let ``broken cross-link is not a violation`` () =
    let result = run [ "tables/orders.md", "---\ntype: table\n---\nSee [missing](./does-not-exist.md)\n" ]
    Assert.Empty(result.Violations)

[<Fact>]
let ``root index frontmatter message tells the user what is expected`` () =
    let result = run [ "index.md", "---\ntype: index\n---\n" ]
    Assert.Equal(1, result.Violations.Length)
    let msg = result.Violations.[0].Message
    Assert.Contains("should contain only `okf_version`", msg)
    Assert.Contains("detect the directory as an OKF bundle", msg)

[<Fact>]
let ``README.md is not a concept and raises no violation`` () =
    let result = run [ "README.md", "# Plain prose\n"; "docs/readme.md", "# More prose\n"; "tables/orders.md", conceptWithType ]
    Assert.Empty(result.Violations)
    Assert.Equal(1, result.TotalConcepts)

[<Fact>]
let ``files under dot-directories are skipped`` () =
    let result = run [ ".claude/skills/x.md", "no frontmatter"; ".github/a/b.md", "no frontmatter"; "tables/orders.md", conceptWithType ]
    Assert.Empty(result.Violations)
    Assert.Equal(1, result.TotalConcepts)

[<Fact>]
let ``files matching ignore patterns are skipped`` () =
    let deps = makeDeps (Map.ofList [ "apm_modules/p/x.md", "no frontmatter"; "inbox/note.md", "no frontmatter"; "tables/orders.md", conceptWithType ])
    match OkfValidate.execute deps { Path = "/bundle"; IgnorePatterns = Config.defaultOkfIgnorePatterns; StrictLinks = false } with
    | Error e -> Assert.Fail e
    | Ok result ->
        Assert.Empty(result.Violations)
        Assert.Equal(1, result.TotalConcepts)

[<Fact>]
let ``files are validated when not covered by ignore patterns`` () =
    let result = run [ "inbox/note.md", "no frontmatter" ]
    Assert.Equal(1, result.Violations.Length)

[<Fact>]
let ``v0.2 shaped trust fields produce no warnings`` () =
    let c = "---\ntype: t\ngenerated: { by: a/1, at: 2026-06-20T22:53:05Z }\nverified: { by: human:me, at: 2026-06-21T00:00:00Z }\nsources:\n  - resource: https://x.test\n---\n"
    let result = run [ "a.md", c ]
    Assert.Empty(result.Violations)
    Assert.Empty(result.Warnings)

[<Fact>]
let ``v0.1 shaped fields warn without failing validation`` () =
    let c = "---\ntype: t\ngenerated: 2026-10-01\nverified: false\nsources: [a.md]\n---\n"
    let result = run [ "a.md", c ]
    Assert.Empty(result.Violations)
    Assert.Equal(1, result.TotalConcepts)
    let rules = result.Warnings |> List.map (fun w -> w.Rule)
    Assert.Contains("generated-shape", rules)
    Assert.Contains("verified-shape", rules)
    Assert.Contains("sources-shape", rules)

[<Fact>]
let ``non-0.2 okf_version in root index warns`` () =
    let result = run [ "index.md", "---\nokf_version: \"0.1\"\n---\n# B\n" ]
    Assert.Empty(result.Violations)
    Assert.Equal("okf-version", result.Warnings.Head.Rule)

[<Fact>]
let ``log.md oldest-first headings warn`` () =
    let result = run [ "log.md", "# Log\n\n## 2026-01-01\n* a\n\n## 2026-02-01\n* b\n" ]
    Assert.Empty(result.Violations)
    Assert.Equal("log-order", result.Warnings.Head.Rule)

// --- link checks ----------------------------------------------------------------------------

let private note (body: string) = "---\ntype: note\n---\n" + body

/// `others` are non-markdown files that exist in the bundle (e.g. images); `dirs` are folders.
let private runLinks (strict: bool) (files: (string * string) list) (others: string list) (dirs: string list) : OkfValidate.ValidateResult =
    let baseDeps = makeDeps (Map.ofList files)
    let deps =
        { baseDeps with
            DirectoryExists = fun p -> dirs |> List.exists (fun d -> p = "/bundle/" + d)
            ListLocalFiles  = fun dir ->
                others
                |> List.map (fun f -> "/bundle/" + f)
                |> List.filter (fun full -> PathUtil.dirName full = dir)
                |> Ok }
    match OkfValidate.execute deps { Path = "/bundle"; IgnorePatterns = []; StrictLinks = strict } with
    | Error e -> Assert.Fail($"expected Ok, got Error {e}"); failwith "unreachable"
    | Ok result -> result

let private linkWarnings files = (runLinks false files [] []).Warnings

[<Fact>]
let ``valid relative links produce no warnings`` () =
    let result = runLinks false [ "a/x.md", note "[b](../b/y.md) [c](c.md) [root](/b/y.md)"; "b/y.md", note "ok"; "a/c.md", note "ok" ] [] []
    Assert.Empty(result.Warnings)
    Assert.Empty(result.Violations)

[<Fact>]
let ``missing page link warns with line and text`` () =
    let w = linkWarnings [ "a.md", note "intro\nsee [gone](gone.md) now\n" ] |> List.exactlyOne
    Assert.Equal("broken-link", w.Rule)
    Assert.Equal("a.md", w.Path)
    Assert.Contains("line 5:", w.Message)
    Assert.Contains("[gone](gone.md)", w.Message)

[<Fact>]
let ``broken links never fail validation by default`` () =
    let result = runLinks false [ "a.md", note "[gone](gone.md)" ] [] []
    Assert.Empty(result.Violations)
    Assert.Equal(1, result.Warnings.Length)

[<Fact>]
let ``strict links turns broken links into violations`` () =
    let result = runLinks true [ "a.md", note "[gone](gone.md) ![i](i.png) [[Nope]]"; "b.md", note "[x](a.md#nope)" ] [] []
    Assert.Empty(result.Warnings)
    let rules = result.Violations |> List.map (fun v -> v.Rule) |> List.sort
    Assert.Equal<string list>([ "broken-anchor"; "broken-image"; "broken-link"; "broken-wikilink" ], rules)

[<Fact>]
let ``link escaping the bundle root warns`` () =
    let w = linkWarnings [ "a.md", note "[up](../../outside.md)" ] |> List.exactlyOne
    Assert.Equal("broken-link", w.Rule)

[<Fact>]
let ``image that exists passes and a missing one warns`` () =
    let result = runLinks false [ "docs/a.md", note "![ok](img/p.png) ![bad](img/q.png)" ] [ "docs/img/p.png" ] []
    let w = result.Warnings |> List.exactlyOne
    Assert.Equal("broken-image", w.Rule)
    Assert.Contains("img/q.png", w.Message)

[<Fact>]
let ``link to a non-markdown file or folder that exists passes`` () =
    let result = runLinks false [ "a.md", note "[pdf](spec.pdf) [dir](assets/) [missing](other.pdf)" ] [ "spec.pdf" ] [ "assets" ]
    let w = result.Warnings |> List.exactlyOne
    Assert.Contains("other.pdf", w.Message)

[<Fact>]
let ``wikilinks resolve by file stem and by title`` () =
    let files =
        [ "a.md", note "[[Orders]] [[order records]] [[Orders|the orders]] [[Missing note]]"
          "tables/Orders.md", "---\ntype: table\ntitle: Order Records\n---\nbody" ]
    let w = linkWarnings files |> List.exactlyOne
    Assert.Equal("broken-wikilink", w.Rule)
    Assert.Contains("[[Missing note]]", w.Message)

[<Fact>]
let ``anchors match github or markdig slugs and missing ones warn separately from missing files`` () =
    let files =
        [ "a.md", note "[ok1](t.md#hello-world) [ok2](t.md#v1.2-notes) [bad](t.md#nope) [nofile](zzz.md#top)"
          "t.md", note "# Hello, World!\n## v1.2 Notes\n" ]
    let ws = linkWarnings files |> List.sortBy (fun w -> w.Rule)
    Assert.Equal<string list>([ "broken-anchor"; "broken-link" ], ws |> List.map (fun w -> w.Rule))
    Assert.Contains("heading 'nope' not found in t.md", ws.[0].Message)

[<Fact>]
let ``external, mailto, anchor-only links and code samples are not checked`` () =
    let body = "[a](https://x.test) [b](mailto:m@x.test) [c](#here)\n`[d](nope.md)`\n```\n[e](nope.md)\n```\n"
    Assert.Empty(linkWarnings [ "a.md", note body ])

[<Fact>]
let ``index files are link-checked too`` () =
    let w = linkWarnings [ "tables/index.md", "# Tables\n* [Orders](orders.md) - gone\n" ] |> List.exactlyOne
    Assert.Equal("tables/index.md", w.Path)
    Assert.Equal("broken-link", w.Rule)

[<Fact>]
let ``links in log and README files are not checked`` () =
    Assert.Empty(linkWarnings [ "log.md", "# Directory Update Log\n\n## 2026-05-22\n* [x](gone.md)\n"; "README.md", "[x](gone.md)" ])
