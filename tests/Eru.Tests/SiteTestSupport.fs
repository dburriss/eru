module Eru.Tests.SiteTestSupport

open System.IO
open Eru
open Eru.Adapters
open Eru.Site

let baseDoc : SiteDocument = {
    Id          = "src:a.md"
    Source      = "src"
    RemotePath  = "a.md"
    Title       = "A"
    Extension   = ".md"
    Tags        = []
    Description = None
    SyncStatus  = Pulled
    Body        = None
    PageUrl     = Some "files/src/a.md.html"
    Type        = None
    Status      = None
    Generated   = None
    Verified    = []
    StaleAfter  = None
    Resource    = None
    Bundle      = None
}

/// Parses `{ "site": <siteJson> }` through the real config reader, then merges and validates.
let effectiveFromSite (siteJson: string) : Result<EffectiveConfig, string> =
    let json = "{ \"version\": 3, \"site\": " + siteJson + " }"
    match Serialization.deserialize<LocalConfig> json with
    | Error e -> Error e
    | Ok local -> Config.merge None (Some local)

let siteSettings (siteJson: string) : SiteSettings =
    match effectiveFromSite siteJson with
    | Ok cfg -> cfg.Site
    | Error e -> failwith e

let siteError (siteJson: string) : string =
    match effectiveFromSite siteJson with
    | Ok _ -> failwith "expected the site config to be rejected"
    | Error e -> e

let private makeSource name : SourceConfig =
    { Name = name; Url = Some $"https://github.com/example/{name}.git"; Branch = Some "main"; Bundles = [] }

let makeEntry (cacheRelPath: string option) (title: string) : IndexEntry =
    { Contributions = Map.empty; Tags = []; Description = None; LocalPath = None; CacheRelPath = cacheRelPath
      ContentHash = None; Type = None; Title = Some title; OkfStatus = None
      Generated = None; Verified = []; StaleAfter = None; Resource = None }

let private fakeExtractLinks (content: string) : ExtractedLink list =
    System.Text.RegularExpressions.Regex.Matches(content, @"\[([^\]]*)\]\(([^)]*)\)")
    |> Seq.map (fun m ->
        let text = m.Groups.[1].Value.Trim()
        { Target = m.Groups.[2].Value; Description = (if text = "" then None else Some text); Kind = MarkdownLink })
    |> List.ofSeq

/// Deps over an in-memory source index (`src`), cached content keyed by cache path, and
/// local files keyed by their repo-relative path (cwd is `/repo`).
let makeDeps (sourceIndex: Map<string, IndexEntry>) (content: Map<string, string>) (localFiles: Map<string, string>) : Deps =
    {
        ReadGlobalConfig        = fun () -> Ok (Some { GlobalConfig.empty with Source = Some { SourceSection.empty with Items = Some [ makeSource "src" ] } })
        ReadLocalConfig         = fun () -> Ok None
        WriteLocalConfig        = fun _ -> Ok ()
        WriteGlobalConfig       = fun _ -> Ok ()
        ReadLockEntries         = fun _ -> Ok []
        WriteLockEntries        = fun _ _ -> Ok ()
        FetchRemoteContent      = fun _ _ paths -> Ok (paths |> List.map (fun p -> (p, "")))
        ListRemoteTopLevel      = fun _ _ -> Ok []
        ListRemoteFiles         = fun _ _ _ -> Ok []
        WriteLocalFile          = fun _ _ -> Ok ()
        ReadLocalFile           = fun path ->
            localFiles
            |> Map.tryPick (fun rel text -> if path = Path.Combine("/repo", rel) then Some text else None)
            |> Ok
        DeleteLocalFile         = fun _ -> Ok ()
        HashContent             = fun s -> $"hash:{s}"
        GetCwd                  = fun () -> "/repo"
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
        DirectoryExists         = fun _ -> true
        GetUtcNow               = fun () -> System.DateTimeOffset.UtcNow
        ListLocalFiles          = fun _ -> Ok []
        ListLocalDirectories    = fun _ -> Ok []
        MoveLocalFile           = fun _ _ -> Ok ()
        PushToRemote            = fun _ _ _ _ -> Ok "main"
        RunAgent                = fun _ _ _ _ -> Ok { Response = ""; Timings = { InitializeMs = 0.0; SessionNewMs = 0.0; PromptMs = 0.0 } }
    }

/// Effective config with one source `src` and the given `site` JSON.
let cfgWithSite (siteJson: string) : EffectiveConfig =
    { EffectiveConfig.empty with
        Sources = [ makeSource "src" ]
        BlockPatterns = []
        SiteIgnorePatterns = []
        OkfIgnorePatterns = []
        Site = siteSettings siteJson }

/// Runs `f` with a fresh output directory and removes it afterwards.
let withOutDir (f: string -> 'a) : 'a =
    let outDir = Path.Combine(Path.GetTempPath(), "eru-site-test-" + System.Guid.NewGuid().ToString("N"))
    try f outDir
    finally
        if Directory.Exists outDir then Directory.Delete(outDir, true)

let readOut (outDir: string) (rel: string) : string = File.ReadAllText(Path.Combine(outDir, rel))

let existsOut (outDir: string) (rel: string) : bool = File.Exists(Path.Combine(outDir, rel))
