module Eru.Tests.SourceBundleTests

open Xunit
open Eru

let private makeSource name bundles : SourceConfig =
    { Name = name; Url = Some $"https://example.com/{name}.git"; Branch = None; Bundles = bundles }

let private emptyLocal sources : LocalConfig =
    { Version = 1; Sources = sources; Collections = []; Inboxes = Map.empty; Settings = None }

let private emptyGlobal sources : GlobalConfig =
    { Version = 1; DefaultSources = sources; Collections = []; DefaultInboxes = Map.empty; Defaults = None }

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (fetchRemoteContent: string -> string -> string list -> Result<(string * string) list, string>)
    (listRemoteFiles: string -> string option -> string option -> Result<string list, string>)
    (capturedLocal: LocalConfig option ref)
    (capturedGlobal: GlobalConfig option ref)
    (indexWrites: System.Collections.Generic.List<string * SourceIndex>) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok globalCfg
        ReadLocalConfig         = fun () -> Ok localCfg
        WriteLocalConfig        = fun cfg -> capturedLocal.Value <- Some cfg; Ok ()
        WriteGlobalConfig       = fun cfg -> capturedGlobal.Value <- Some cfg; Ok ()
        ReadLockEntries         = fun _ -> Ok []
        WriteLockEntries        = fun _ _ -> Ok ()
        FetchRemoteContent      = fetchRemoteContent
        ListRemoteTopLevel      = fun _ _ -> Ok []
        ListRemoteFiles         = listRemoteFiles
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
        ReadSourceIndex         = fun _ -> Ok None
        WriteSourceIndex        = fun sourceName idx -> indexWrites.Add(sourceName, idx); Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = Eru.Adapters.YamlAdapter.parse
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fun _ -> []
        GetRemoteHeadSha        = fun _ _ -> Error "not implemented"
        DirectoryExists        = fun _ -> true
        GetUtcNow        = fun () -> System.DateTimeOffset.UtcNow
    }

let private noFetch _ _ (paths: string list) : Result<(string * string) list, string> = Ok []
let private noList _ _ _ : Result<string list, string> = Ok []

let private assertOk result = match result with Error e -> Assert.Fail(e) | Ok _ -> ()
let private assertError result = match result with Ok _ -> Assert.Fail("Expected Error result") | Error _ -> ()

// ── SourceBundleAdd ───────────────────────────────────────────────────────────

[<Fact>]
let ``add appends a bundle to a source in local config`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let capturedLocal = ref None
    let deps = makeDeps None (Some local) noFetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = Some "manifest"; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    match capturedLocal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Manifest } ], cfg.Sources[0].Bundles)

[<Fact>]
let ``add appends a bundle to a source in global config when the source lives there`` () =
    let g = emptyGlobal [ makeSource "kb" [] ]
    let capturedGlobal = ref None
    let deps = makeDeps (Some g) None noFetch noList (ref None) capturedGlobal (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = Some "manifest"; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    match capturedGlobal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Manifest } ], cfg.DefaultSources[0].Bundles)

[<Fact>]
let ``add normalizes '.' and '/' to the repo-root sentinel`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let capturedLocal = ref None
    let deps = makeDeps None (Some local) noFetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "."; Kind = Some "manifest"; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    match capturedLocal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = ""; Kind = Manifest } ], cfg.Sources[0].Bundles)

[<Fact>]
let ``add rejects an exact-path duplicate`` () =
    let local = emptyLocal [ makeSource "kb" [ { Path = "docs"; Kind = Manifest } ] ]
    let deps = makeDeps None (Some local) noFetch noList (ref None) (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = None; DryRun = false }
    assertError (SourceBundleAdd.execute deps cmd)

[<Fact>]
let ``add accepts a nested path alongside an existing root bundle`` () =
    let local = emptyLocal [ makeSource "kb" [ { Path = ""; Kind = Manifest } ] ]
    let capturedLocal = ref None
    let deps = makeDeps None (Some local) noFetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs/knowledge"; Kind = Some "okf"; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    match capturedLocal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg ->
        Assert.Equal<Bundle list>(
            [ { Path = ""; Kind = Manifest }; { Path = "docs/knowledge"; Kind = Okf } ],
            cfg.Sources[0].Bundles)

[<Fact>]
let ``add errors when the source does not exist`` () =
    let local = emptyLocal []
    let deps = makeDeps None (Some local) noFetch noList (ref None) (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "missing"; Path = "docs"; Kind = None; DryRun = false }
    assertError (SourceBundleAdd.execute deps cmd)

[<Fact>]
let ``add errors on an unknown explicit kind`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let deps = makeDeps None (Some local) noFetch noList (ref None) (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = Some "bogus"; DryRun = false }
    assertError (SourceBundleAdd.execute deps cmd)

[<Fact>]
let ``add dry-run does not write config`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let capturedLocal = ref None
    let deps = makeDeps None (Some local) noFetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = Some "manifest"; DryRun = true }
    assertOk (SourceBundleAdd.execute deps cmd)
    Assert.True(capturedLocal.Value.IsNone)

[<Fact>]
let ``add auto-detects Okf kind from index_md okf_version`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let capturedLocal = ref None
    let fetch _ _ (paths: string list) =
        Ok (paths |> List.map (fun p -> p, "---\nokf_version: \"1.0\"\n---\n"))
    let deps = makeDeps None (Some local) fetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = None; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    match capturedLocal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Okf } ], cfg.Sources[0].Bundles)

[<Fact>]
let ``add auto-detects Manifest kind when index_md has no okf_version`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let capturedLocal = ref None
    let fetch _ _ (paths: string list) = Ok (paths |> List.map (fun p -> p, "# just a heading"))
    let deps = makeDeps None (Some local) fetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = None; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    match capturedLocal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = "docs"; Kind = Manifest } ], cfg.Sources[0].Bundles)

[<Fact>]
let ``add triggers a one-bundle discovery walk and merges into the index when Okf`` () =
    let local = emptyLocal [ makeSource "kb" [] ]
    let capturedLocal = ref None
    let indexWrites = System.Collections.Generic.List<string * SourceIndex>()
    let listFiles _ _ _ = Ok [ "adr.md" ]
    let fetch _ _ (paths: string list) =
        Ok (paths |> List.map (fun p ->
            if p.EndsWith "index.md" then p, "---\nokf_version: \"1.0\"\n---\n"
            else p, "---\ntype: ADR\ntags: [dotnet]\n---\n"))
    let deps = makeDeps None (Some local) fetch listFiles capturedLocal (ref None) indexWrites
    let cmd : SourceBundleAdd.Command = { SourceName = "kb"; Path = "docs"; Kind = None; DryRun = false }
    assertOk (SourceBundleAdd.execute deps cmd)
    Assert.Equal(1, indexWrites.Count)
    let (sourceName, idx) = indexWrites[0]
    Assert.Equal("kb", sourceName)
    match Map.tryFind "docs/adr.md" idx.Entries with
    | Some e -> Assert.Equal<string list>(["dotnet"], e.Tags)
    | None   -> Assert.Fail "expected docs/adr.md to be indexed"

// ── SourceBundleList ──────────────────────────────────────────────────────────

[<Fact>]
let ``list returns a source's bundles`` () =
    let bundles = [ { Path = ""; Kind = Manifest }; { Path = "docs"; Kind = Okf } ]
    let local = emptyLocal [ makeSource "kb" bundles ]
    let deps = makeDeps None (Some local) noFetch noList (ref None) (ref None) (System.Collections.Generic.List())
    match SourceBundleList.execute deps "kb" with
    | Error e -> Assert.Fail e
    | Ok result -> Assert.Equal<Bundle list>(bundles, result)

[<Fact>]
let ``list errors when the source does not exist`` () =
    let deps = makeDeps None (Some (emptyLocal [])) noFetch noList (ref None) (ref None) (System.Collections.Generic.List())
    assertError (SourceBundleList.execute deps "missing")

// ── SourceBundleRemove ────────────────────────────────────────────────────────

[<Fact>]
let ``remove drops the matching bundle from local config`` () =
    let bundles = [ { Path = ""; Kind = Manifest }; { Path = "docs"; Kind = Okf } ]
    let local = emptyLocal [ makeSource "kb" bundles ]
    let capturedLocal = ref None
    let deps = makeDeps None (Some local) noFetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleRemove.Command = { SourceName = "kb"; Path = "docs"; DryRun = false }
    assertOk (SourceBundleRemove.execute deps cmd)
    match capturedLocal.Value with
    | None -> Assert.Fail "nothing written"
    | Some cfg -> Assert.Equal<Bundle list>([ { Path = ""; Kind = Manifest } ], cfg.Sources[0].Bundles)

[<Fact>]
let ``remove errors when the bundle path is not registered`` () =
    let local = emptyLocal [ makeSource "kb" [ { Path = ""; Kind = Manifest } ] ]
    let deps = makeDeps None (Some local) noFetch noList (ref None) (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleRemove.Command = { SourceName = "kb"; Path = "docs"; DryRun = false }
    assertError (SourceBundleRemove.execute deps cmd)

[<Fact>]
let ``remove dry-run does not write config`` () =
    let local = emptyLocal [ makeSource "kb" [ { Path = "docs"; Kind = Manifest } ] ]
    let capturedLocal = ref None
    let deps = makeDeps None (Some local) noFetch noList capturedLocal (ref None) (System.Collections.Generic.List())
    let cmd : SourceBundleRemove.Command = { SourceName = "kb"; Path = "docs"; DryRun = true }
    assertOk (SourceBundleRemove.execute deps cmd)
    Assert.True(capturedLocal.Value.IsNone)
