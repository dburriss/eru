module Eru.Tests.OkfFixTests

open System.Collections.Generic
open Xunit
open Eru
open Eru.Adapters

let private makeDeps (files: Map<string, string>) (writes: Dictionary<string, string>) : Deps =
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

let private runWith mode dryRun (files: (string * string) list) =
    let writes = Dictionary<string, string>()
    let deps = makeDeps (Map.ofList files) writes
    let cmd : OkfFix.Command =
        { Path = "/bundle"; IgnorePatterns = []; Mode = mode; DefaultType = "reference"; DryRun = dryRun }
    match OkfFix.execute deps cmd with
    | Ok r -> r, writes
    | Error e -> failwith e

let private fix files = runWith OkfFix.Fix false files

/// Applies the fix writes on top of the original files and validates the result.
let private validateAfter (files: (string * string) list) (writes: Dictionary<string, string>) =
    let merged =
        writes |> Seq.fold (fun m kv -> Map.add kv.Key kv.Value m) (Map.ofList files)
    let deps = makeDeps merged (Dictionary())
    match OkfValidate.execute deps { Path = "/bundle"; IgnorePatterns = [] } with
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
        { Path = "/bundle"; IgnorePatterns = [ "inbox/**" ]; Mode = OkfFix.Fix; DefaultType = "reference"; DryRun = false }
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
