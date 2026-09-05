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
    }

let private run (files: (string * string) list) : OkfValidate.ValidateResult =
    let deps = makeDeps (Map.ofList files)
    match OkfValidate.execute deps { Path = "/bundle" } with
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
