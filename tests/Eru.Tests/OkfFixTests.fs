module Eru.Tests.OkfFixTests

open System.Collections.Generic
open Xunit
open Eru
open Eru.Adapters

let makeDeps (files: Map<string, string>) (writes: Dictionary<string, string>) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok None
        ReadLocalConfig    = fun () -> Ok None
        ReadEnvConfig    = fun () -> Ok None
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent  = fun _ _ _ -> Error "not implemented"
        ListRemoteTopLevel  = fun _ _ -> Ok []
        ListRemoteFiles     = fun _ _ _ -> Ok []
        WriteLocalFile      = fun path content ->
            writes.[path.Replace("/bundle/", "")] <- content
            Ok ()
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

let private runWith mode dryRun (files: (string * string) list) =
    let writes = Dictionary<string, string>()
    let deps = makeDeps (Map.ofList files) writes
    let cmd : OkfFix.Command =
        { Path = "/bundle"; IgnorePatterns = []; Mode = mode; DefaultType = "reference"; TypeOverride = None; FixTypes = []; DryRun = dryRun; Update = false; Force = false }
    match OkfFix.execute deps cmd with
    | Ok r -> r, writes
    | Error e -> failwith e

let private fix files = runWith OkfFix.Fix false files

/// Applies the fix writes on top of the original files and validates the result.
let private validateAfter (files: (string * string) list) (writes: Dictionary<string, string>) =
    let merged =
        writes |> Seq.fold (fun m kv -> Map.add kv.Key kv.Value m) (Map.ofList files)
    let deps = makeDeps merged (Dictionary())
    match OkfValidate.execute deps { Path = "/bundle"; IgnorePatterns = []; StrictLinks = false } with
    | Ok r -> r
    | Error e -> failwith e

let private concept = "---\ntype: table\n---\n# Orders\n"
let private rootIndex = "---\nokf_version: \"0.2\"\n---\n# Index\n"

[<Fact>]
let ``root index frontmatter with extra keys is reduced to okf_version`` () =
    let files = [ "index.md", "---\nokf_version: \"0.2\"\ntitle: x\n---\n# Hi\n"; "a.md", concept ]
    let r, writes = fix files
    Assert.Contains(r.Changes, fun c -> c.Rule = "index-frontmatter")
    Assert.Equal("---\nokf_version: \"0.2\"\n---\n# Hi\n", writes.["index.md"])

[<Fact>]
let ``root index without frontmatter gets okf_version`` () =
    let _, writes = fix [ "index.md", "# Hi\n"; "a.md", concept ]
    Assert.StartsWith("---\nokf_version: \"0.2\"\n---\n", writes.["index.md"])
    Assert.Contains("# Hi", writes.["index.md"])

[<Fact>]
let ``non-root index frontmatter is stripped`` () =
    let _, writes = fix [ "index.md", rootIndex; "t/index.md", "---\ntitle: x\n---\n# T\n"; "t/a.md", concept ]
    Assert.Equal("# T\n", writes.["t/index.md"])

[<Fact>]
let ``concept without frontmatter gets default type`` () =
    let _, writes = fix [ "index.md", rootIndex; "a.md", "# Orders\n" ]
    Assert.Equal("---\ntype: reference\n---\n# Orders\n", writes.["a.md"])

[<Fact>]
let ``missing type is inserted preserving other keys`` () =
    let _, writes = fix [ "index.md", rootIndex; "a.md", "---\ndescription: foo\ntags: [a]\n---\nbody\n" ]
    Assert.Equal("---\ntype: reference\ndescription: foo\ntags: [a]\n---\nbody\n", writes.["a.md"])

[<Fact>]
let ``empty type value is replaced`` () =
    let _, writes = fix [ "index.md", rootIndex; "a.md", "---\ntype: \"\"\ndescription: foo\n---\nbody\n" ]
    Assert.Equal("---\ntype: reference\ndescription: foo\n---\nbody\n", writes.["a.md"])

[<Fact>]
let ``malformed concept yaml is reported as manual and untouched`` () =
    let r, writes = fix [ "index.md", rootIndex; "a.md", "---\nfoo: [unclosed\n---\nbody\n" ]
    Assert.False(writes.ContainsKey "a.md")
    Assert.Contains(r.Manual, fun v -> v.Rule = "malformed-yaml")

[<Fact>]
let ``log date headings are rewritten to ISO and unparseable ones reported`` () =
    let r, writes = fix [ "index.md", rootIndex; "a.md", concept; "log.md", "## 2026-01-02\nok\n## March 4, 2026\nx\n## sometime\ny\n" ]
    Assert.Equal("## 2026-01-02\nok\n## 2026-03-04\nx\n## sometime\ny\n", writes.["log.md"])
    Assert.Single(r.Manual) |> ignore

[<Fact>]
let ``missing log is never created`` () =
    let _, writes = fix [ "a.md", concept ]
    Assert.False(writes.ContainsKey "log.md")

[<Fact>]
let ``missing indexes are generated with catalog rows`` () =
    let files = [ "tables/orders.md", "---\ntype: table\ntitle: Orders\ntags: [a, b]\nstale_after: 2027-01-01\n---\n" ]
    let r, writes = fix files
    Assert.All(r.Changes, fun c -> Assert.True c.Created)
    Assert.StartsWith("---\nokf_version:", writes.["index.md"])
    Assert.Contains("[tables/](tables/index.md)", writes.["index.md"])
    Assert.StartsWith("# tables", writes.["tables/index.md"])
    Assert.Contains("| [Orders](orders.md) | table | a, b | 2027-01-01 |", writes.["tables/index.md"])

[<Fact>]
let ``init creates indexes but does not repair existing files`` () =
    let r, writes = runWith OkfFix.CreateOnly false [ "a.md", "# no frontmatter\n" ]
    Assert.True(writes.ContainsKey "index.md")
    Assert.False(writes.ContainsKey "a.md")
    Assert.Single(r.Changes) |> ignore

[<Fact>]
let ``init never overwrites an existing index`` () =
    let _, writes = runWith OkfFix.CreateOnly false [ "index.md", "custom"; "a.md", concept ]
    Assert.Empty(writes)

[<Fact>]
let ``dry run reports changes but writes nothing`` () =
    let r, writes = runWith OkfFix.Fix true [ "a.md", "# x\n" ]
    Assert.NotEmpty(r.Changes)
    Assert.Empty(writes)

[<Fact>]
let ``readme and ignored paths are untouched`` () =
    let writes = Dictionary<string, string>()
    let deps = makeDeps (Map.ofList [ "index.md", rootIndex; "README.md", "# hi"; "inbox/x.md", "raw" ]) writes
    let cmd : OkfFix.Command =
        { Path = "/bundle"; IgnorePatterns = [ "inbox/**" ]; Mode = OkfFix.Fix; DefaultType = "reference"; TypeOverride = None; FixTypes = []; DryRun = false; Update = false; Force = false }
    OkfFix.execute deps cmd |> ignore
    Assert.Empty(writes)

[<Fact>]
let ``fix is idempotent and result validates`` () =
    let files =
        [ "index.md", "---\ntitle: x\n---\n"
          "t/index.md", "---\nk: v\n---\n"
          "t/a.md", "# a\n"
          "t/b.md", "---\ndescription: d\n---\n"
          "log.md", "## 4 March 2026\nx\n" ]
    let _, writes = fix files
    let v = validateAfter files writes
    Assert.Empty(v.Violations)
    let merged = writes |> Seq.fold (fun m kv -> Map.add kv.Key kv.Value m) (Map.ofList files) |> Map.toList
    let r2, _ = fix merged
    Assert.Empty(r2.Changes)

let private withVerified (v: string) = $"---\ntype: table\n{v}---\nbody\n"

[<Fact>]
let ``scalar placeholder verified is stripped`` () =
    let r, writes = fix [ "index.md", rootIndex; "a.md", withVerified "verified: unknown\n" ]
    Assert.Contains(r.Changes, fun c -> c.Rule = "verified-shape")
    Assert.Equal("---\ntype: table\n---\nbody\n", writes.["a.md"])

[<Fact>]
let ``boolean verified is stripped`` () =
    let _, writes = fix [ "index.md", rootIndex; "a.md", withVerified "verified: false\n" ]
    Assert.Equal("---\ntype: table\n---\nbody\n", writes.["a.md"])

[<Fact>]
let ``bad verified entries are dropped and valid ones kept`` () =
    let v = "verified:\n  - { by: human:me, at: 2026-06-21T00:00:00Z }\n  - { by: unknown, at: 2026-06-21T00:00:00Z }\n  - { by: bot/1 }\n"
    let _, writes = fix [ "index.md", rootIndex; "a.md", withVerified v ]
    Assert.Equal(withVerified "verified:\n  - { by: \"human:me\", at: \"2026-06-21T00:00:00Z\" }\n", writes.["a.md"])

[<Fact>]
let ``valid verified is untouched`` () =
    let v = "verified:\n  - { by: human:me, at: 2026-06-21T00:00:00Z }\n"
    let r, writes = fix [ "index.md", rootIndex; "a.md", withVerified v ]
    Assert.False(writes.ContainsKey "a.md")
    Assert.DoesNotContain(r.Changes, fun c -> c.Rule = "verified-shape")

[<Fact>]
let ``stripping verified preserves keys that follow it`` () =
    let _, writes = fix [ "index.md", rootIndex; "a.md", "---\ntype: table\nverified: unknown\nstatus: draft\n---\nbody\n" ]
    Assert.Equal("---\ntype: table\nstatus: draft\n---\nbody\n", writes.["a.md"])

[<Fact>]
let ``missing type and bad verified are repaired together`` () =
    let _, writes = fix [ "index.md", rootIndex; "a.md", "---\nverified: unknown\n---\nbody\n" ]
    Assert.Equal("---\ntype: reference\n---\nbody\n", writes.["a.md"])

[<Fact>]
let ``dry run does not strip verified`` () =
    let r, writes = runWith OkfFix.Fix true [ "index.md", rootIndex; "a.md", withVerified "verified: unknown\n" ]
    Assert.Contains(r.Changes, fun c -> c.Rule = "verified-shape")
    Assert.Empty writes

[<Fact>]
let ``init never touches verified`` () =
    let r, writes = runWith OkfFix.CreateOnly false [ "a.md", withVerified "verified: unknown\n" ]
    Assert.DoesNotContain(r.Changes, fun c -> c.Rule = "verified-shape")
    Assert.False(writes.ContainsKey "a.md")

// --- okf.fixTypes / --default-type / typed READMEs ---

let private fixWith (fixTypes: OkfFixTypeRule list) (typeOverride: string option) (files: (string * string) list) =
    let writes = Dictionary<string, string>()
    let deps = makeDeps (Map.ofList files) writes
    let cmd : OkfFix.Command =
        { Path = "/bundle"; IgnorePatterns = []; Mode = OkfFix.Fix; DefaultType = "reference"
          TypeOverride = typeOverride; FixTypes = fixTypes; DryRun = false; Update = false; Force = false }
    match OkfFix.execute deps cmd with
    | Ok r -> r, writes
    | Error e -> failwith e

let private rule pattern typ : OkfFixTypeRule = { Pattern = pattern; Type = typ }

[<Fact>]
let ``untyped README matching a fixTypes rule gets the type and is cataloged`` () =
    let files = [ "index.md", rootIndex; "docs/README.md", "# Docs\n" ]
    let _, writes = fixWith [ rule "README.md" (Some "guide") ] None files
    Assert.Equal("---\ntype: guide\n---\n# Docs\n", writes.["docs/README.md"])
    Assert.Contains("[README](README.md) | guide", writes.["docs/index.md"])

[<Fact>]
let ``untyped README with no matching rule is untouched`` () =
    let r, writes = fixWith [ rule "docs/**" (Some "guide") ] None [ "index.md", rootIndex; "README.md", "# hi\n" ]
    Assert.DoesNotContain(r.Changes, fun c -> c.Path = "README.md")
    Assert.False(writes.ContainsKey "README.md")

[<Fact>]
let ``typed README is repaired without any rule`` () =
    let files = [ "index.md", rootIndex; "README.md", "---\ntype: guide\nverified: unknown\n---\n" ]
    let r, writes = fixWith [] None files
    Assert.Contains(r.Changes, fun c -> c.Path = "README.md" && c.Rule = "verified-shape")
    Assert.Equal<string list>([ "README.md" ], writes.Keys |> List.ofSeq)

[<Fact>]
let ``fixTypes keeps an existing type`` () =
    let r, _ = fixWith [ rule "*.md" (Some "guide") ] None [ "index.md", rootIndex; "a.md", "---\ntype: adr\n---\n" ]
    Assert.DoesNotContain(r.Changes, fun c -> c.Path = "a.md")

[<Fact>]
let ``first matching fixTypes rule wins and null leaves the file alone`` () =
    let files = [ "index.md", rootIndex; "docs/a.md", "# a\n"; "docs/internal/b.md", "# b\n" ]
    let rules = [ rule "docs/internal/**" None; rule "docs/**" (Some "guide") ]
    let _, writes = fixWith rules None files
    Assert.Equal("---\ntype: guide\n---\n# a\n", writes.["docs/a.md"])
    Assert.False(writes.ContainsKey "docs/internal/b.md")

[<Fact>]
let ``type precedence is flag then rule then default`` () =
    let files = [ "index.md", rootIndex; "docs/a.md", "# a\n"; "b.md", "# b\n" ]
    let rules = [ rule "docs/**" (Some "guide") ]
    let _, ruled = fixWith rules None files
    Assert.StartsWith("---\ntype: guide\n", ruled.["docs/a.md"])
    Assert.StartsWith("---\ntype: reference\n", ruled.["b.md"])
    let _, flagged = fixWith rules (Some "note") files
    Assert.StartsWith("---\ntype: note\n", flagged.["docs/a.md"])
    Assert.StartsWith("---\ntype: note\n", flagged.["b.md"])

[<Fact>]
let ``flag does not type an unmatched README or override a null rule`` () =
    let files = [ "index.md", rootIndex; "README.md", "# hi\n"; "docs/a.md", "# a\n" ]
    let _, writes = fixWith [ rule "docs/**" None ] (Some "note") files
    Assert.False(writes.ContainsKey "README.md")
    Assert.False(writes.ContainsKey "docs/a.md")


// ---- catalog refresh (`init --update [--force]`) ----

let private initWith update force dryRun (files: (string * string) list) =
    let writes = Dictionary<string, string>()
    let deps = makeDeps (Map.ofList files) writes
    let cmd : OkfFix.Command =
        { Path = "/bundle"; IgnorePatterns = []; Mode = OkfFix.CreateOnly; DefaultType = "reference"; TypeOverride = None
          FixTypes = []; DryRun = dryRun; Update = update; Force = force }
    match OkfFix.execute deps cmd with
    | Ok r -> r, writes
    | Error e -> failwith e

let private markedRoot (rows: string) =
    $"---\nokf_version: \"0.2\"\n---\n# Index\n\nIntro prose.\n\n<!-- eru:catalog -->\n\n| Concept | Type | Tags | Stale after |\n|---|---|---|---|\n{rows}\n<!-- /eru:catalog -->\n\nOutro prose.\n"

[<Fact>]
let ``new indexes carry catalog markers`` () =
    let _, writes = initWith false false false [ "a.md", concept ]
    Assert.Contains("<!-- eru:catalog -->", writes.["index.md"])
    Assert.Contains("<!-- /eru:catalog -->", writes.["index.md"])
    Assert.Contains("| [a](a.md) |", writes.["index.md"])

[<Fact>]
let ``init without update never touches an existing index`` () =
    let r, writes = initWith false false false [ "index.md", markedRoot ""; "a.md", concept ]
    Assert.Empty(writes)
    Assert.Empty(r.Skipped)

[<Fact>]
let ``update regenerates only the marked block`` () =
    let r, writes = initWith true false false [ "index.md", markedRoot "| [old](old.md) | x | | |\n"; "a.md", concept ]
    let updated = writes.["index.md"]
    Assert.Contains(r.Changes, fun c -> c.Rule = "index-refreshed")
    Assert.StartsWith("---\nokf_version: \"0.2\"\n---\n# Index\n\nIntro prose.\n\n<!-- eru:catalog -->", updated)
    Assert.EndsWith("<!-- /eru:catalog -->\n\nOutro prose.\n", updated)
    Assert.Contains("[a](a.md)", updated)
    Assert.DoesNotContain("old.md", updated)

[<Fact>]
let ``update is idempotent`` () =
    let _, first = initWith true false false [ "index.md", markedRoot ""; "a.md", concept ]
    let r, second = initWith true false false [ "index.md", first.["index.md"]; "a.md", concept ]
    Assert.Empty(second)
    Assert.Empty(r.Changes)

[<Fact>]
let ``update skips an unmarked index and says why`` () =
    let r, writes = initWith true false false [ "index.md", rootIndex; "a.md", concept ]
    Assert.Empty(writes)
    Assert.Contains(r.Skipped, fun (p, reason) -> p = "index.md" && reason.Contains "no eru:catalog markers")

[<Fact>]
let ``force replaces an eru-shaped table and keeps prose`` () =
    let index =
        "---\nokf_version: \"0.2\"\n---\n# Index\n\nIntro.\n\n| Concept | Type | Tags | Stale after |\n|---|---|---|---|\n| [old](old.md) | x | | |\n\nOutro.\n"
    let r, writes = initWith true true false [ "index.md", index; "a.md", concept ]
    let updated = writes.["index.md"]
    Assert.Contains(r.Changes, fun c -> c.Rule = "index-migrated")
    Assert.Contains("Intro.", updated)
    Assert.Contains("Outro.", updated)
    Assert.Contains("<!-- eru:catalog -->", updated)
    Assert.DoesNotContain("old.md", updated)

[<Fact>]
let ``force appends a block and leaves an unrelated table alone`` () =
    let index = "---\nokf_version: \"0.2\"\n---\n# Index\n\n| Name | Owner |\n|---|---|\n| x | y |\n"
    let _, writes = initWith true true false [ "index.md", index; "a.md", concept ]
    let updated = writes.["index.md"]
    Assert.Contains("| Name | Owner |", updated)
    Assert.EndsWith("<!-- /eru:catalog -->\n", updated)
    Assert.Contains("[a](a.md)", updated)

[<Fact>]
let ``force is idempotent`` () =
    let _, first = initWith true true false [ "index.md", rootIndex; "a.md", concept ]
    let r, second = initWith true true false [ "index.md", first.["index.md"]; "a.md", concept ]
    Assert.Empty(second)
    Assert.Empty(r.Changes)

[<Fact>]
let ``force never touches a foreign index with its own frontmatter`` () =
    let foreign = "---\nlayout: page\ntitle: Docs\n---\n# Docs\n"
    let files = [ "index.md", rootIndex; "t/index.md", foreign; "t/a.md", concept ]
    let r, writes = initWith true true false files
    Assert.False(writes.ContainsKey "t/index.md")
    Assert.Contains(r.Skipped, fun (p, reason) -> p = "t/index.md" && reason.Contains "frontmatter")

[<Fact>]
let ``force never touches indexes outside a bundle`` () =
    // The root index.md has no okf_version, so the directory is not an OKF bundle.
    let files = [ "index.md", "# Site\n"; "t/index.md", "# T\n"; "t/a.md", concept ]
    let r, writes = initWith true true false files
    Assert.False(writes.ContainsKey "index.md")
    Assert.False(writes.ContainsKey "t/index.md")
    Assert.Contains(r.Skipped, fun (p, reason) -> p = "t/index.md" && reason.Contains "outside a bundle")

[<Fact>]
let ``force never touches an index in a folder with nothing to catalog`` () =
    // README-only folders are not catalogued, so their index.md is not ours.
    let files = [ "index.md", rootIndex; "a.md", concept; "docs/index.md", "# Docs\n"; "docs/README.md", "# r" ]
    let _, writes = initWith true true false files
    Assert.False(writes.ContainsKey "docs/index.md")

[<Fact>]
let ``malformed markers are skipped even with force`` () =
    let index = "---\nokf_version: \"0.2\"\n---\n# Index\n\n<!-- eru:catalog -->\nno close\n"
    let r, writes = initWith true true false [ "index.md", index; "a.md", concept ]
    Assert.Empty(writes)
    Assert.Contains(r.Skipped, fun (_, reason) -> reason.Contains "markers")

[<Fact>]
let ``dry-run update writes nothing but reports the change`` () =
    let r, writes = initWith true true true [ "index.md", rootIndex; "a.md", concept ]
    Assert.Empty(writes)
    Assert.Contains(r.Changes, fun c -> c.Rule = "index-migrated")

[<Fact>]
let ``fix refreshes marked indexes and hints for unmarked ones`` () =
    let r, writes = fix [ "index.md", markedRoot ""; "t/index.md", "# T\n"; "t/a.md", concept ]
    Assert.Contains("[t/](t/index.md)", writes.["index.md"])
    Assert.False(writes.ContainsKey "t/index.md")
    Assert.Contains(r.Skipped, fun (p, _) -> p = "t/index.md")

[<Fact>]
let ``validate warns about unmarked and stale catalog indexes`` () =
    let warnFor files =
        let deps = makeDeps (Map.ofList files) (Dictionary())
        match OkfValidate.execute deps { Path = "/bundle"; IgnorePatterns = []; StrictLinks = false } with
        | Ok r -> r.Warnings |> List.map (fun w -> w.Rule)
        | Error e -> failwith e
    Assert.Contains("missing-catalog-markers", warnFor [ "index.md", rootIndex; "a.md", concept ])
    Assert.Contains("stale-index", warnFor [ "index.md", markedRoot ""; "a.md", concept ])
    let _, adopted = initWith true true false [ "index.md", rootIndex; "a.md", concept ]
    let clean = warnFor [ "index.md", adopted.["index.md"]; "a.md", concept ]
    Assert.DoesNotContain("stale-index", clean)
    Assert.DoesNotContain("missing-catalog-markers", clean)
