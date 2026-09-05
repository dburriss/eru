# Parse OKF frontmatter for the site generator

## Context

`eru` shares markdown "knowledge" files between repos and can render a
browsable static site from them (`Eru.Site`). We want to support the [Open
Knowledge Format (OKF)](https://github.com/GoogleCloudPlatform/open-knowledge-format)
— a spec for markdown+YAML-frontmatter knowledge bundles — starting with the
smallest, most self-contained slice: making a document's own OKF frontmatter
(`type`, `title`, `status`, `generated`, `verified`, `stale_after`, `resource`)
actually reach the generated site (trust badges, staleness warnings, grouping
by `type`), instead of being silently discarded.

Investigation found this is *not* purely new territory: `Eru.Domain/Frontmatter.fs`
already hand-parses frontmatter (`Description`, `Tags` only) once per `eru
sync`, caching the result into `sources/<name>/index.json` via `IndexEntry`
(`Config.fs:84-90`), which `Eru.Site/IndexBuilder.fs` reads to build
`SiteDocument`. `Eru.Site/MarkdownRenderer.fs`'s `stripFrontmatter` is a
*second*, unrelated hand-rolled scanner that just deletes the block before
Markdig renders the body — it never extracts anything, and is untouched by
this plan.

Decisions already made with the user:
- Extend the **existing sync-time parser** (`Frontmatter.fs` → `IndexEntry` →
  cached in `index.json`), not a fresh parse in `Eru.Site` at every
  `site generate`. This keeps the YAML cost paid once per `eru sync`, not
  once per generation.
- Parse the richer, nested shapes (`generated: {by, at}`, `verified` as a
  bare mapping or a list) with a real YAML library instead of growing the
  hand-rolled scanner further — **but `Eru.Domain` stays dependency-free**
  (confirmed: `Eru.Domain.fsproj` has zero `PackageReference`s today, and the
  project convention is pure domain logic with effects injected via the
  `Deps` record). YamlDotNet is added only to `Eru.Adapters`, behind a small
  generic interface defined in `Eru.Domain`, wired at the composition root
  (`AdapterDeps.create`) — mirroring exactly how `FetchRemoteContent`,
  `HashContent`, etc. are already injected.
- `Frontmatter.parse` returns an **open map** (`Map<string, Yaml.Node>`), not
  a closed record — matching OKF's own permissiveness (producers may add any
  extra keys; consumers must tolerate/preserve them, §4.1/§11). A set of
  **lens functions** (`Frontmatter.type_`, `.title`, `.status`, `.generated`,
  `.verified`, `.staleAfter`, `.resource`, plus the existing `.description`/
  `.tags`) read well-known fields off that map. This stays contained to
  `Frontmatter.fs` — downstream (`IndexEntry`, `SiteDocument`, `index.json`,
  the site templates) keeps today's flat, named, JSON-friendly fields. The
  lenses are called once, at the `Sync.fs` merge boundary, to populate those
  concrete fields; unknown/extension keys are available at parse time (e.g.
  to a future validator) but intentionally don't propagate further in this
  slice.
- Rename the existing `SiteDocument.Status: FileStatus` (Pulled/Cached/
  IndexOnly — sync status) to `SyncStatus`, freeing up `Status` for OKF's
  lifecycle field (`draft`/`stable`/`deprecated`). (`IndexEntry` has no
  existing `Status` field, so no rename needed there — only additions.)
- The existing sidebar section literally labelled "Types" in `indexPage`
  (`HtmlTemplates.fs:132`) is actually file *extensions* (`AllExtensions`),
  not OKF concept types. Rename that label to "File Extensions" so the new
  OKF-driven grouping can use the name "Types" directly, matching the spec's
  own terminology instead of a hedge like "Concept Types".

Out of scope for this slice: `sources` (provenance/citations), the
`Attested Computation` concept type and its `runtime`/`parameters`/
`computation`/`executor`/`attester` protocol, and any change to
`manifest.json`/`eru.lock` formats (this only touches the `index.json` cache
shape, which is already source-side derived/rebuildable state).

## Approach

### 1. `Eru.Domain/Yaml.fs` (new file) — the injected parsing seam

A minimal generic YAML tree, no third-party types, compiled early (added to
`Eru.Domain.fsproj` right before `Deps.fs`, since `Deps` needs the type):

```fsharp
namespace Eru

module Yaml =
    type Node =
        | Scalar of string
        | Seq    of Node list
        | Map    of (string * Node) list
        | Null

    /// Parses a raw YAML mapping block's text into a generic tree.
    /// Injected — the real implementation (YamlDotNet-backed) lives in Eru.Adapters.
    type Parse = string -> Result<Node, string>
```

### 2. `Eru.Domain/Deps.fs` — add the injection point

Add one field: `ParseYamlBlock : Yaml.Parse` (i.e.
`string -> Result<Yaml.Node, string>`), alongside the other injected effects.

### 3. `Eru.Adapters` — real implementation

Add `PackageReference Include="YamlDotNet"` to `Eru.Adapters.fsproj` (the only
project that gets the new dependency).

New file `Eru.Adapters/YamlAdapter.fs` (added to the fsproj compile list):

```fsharp
namespace Eru.Adapters

module YamlAdapter =
    let parse (yamlText: string) : Result<Eru.Yaml.Node, string> =
        // Use YamlDotNet's parser/deserializer to load yamlText into its own
        // node model, then convert to Eru.Yaml.Node (Scalar/Seq/Map/Null).
        // Malformed YAML -> Error, not an exception.
```

Wire it up in `AdapterDeps.create` (`Eru.Adapters/AdapterDeps.fs`):
`ParseYamlBlock = YamlAdapter.parse`.

### 4. `Eru.Domain/Frontmatter.fs` — map + lenses

Keep the existing pure, dependency-free logic that locates the `---`...`---`
delimited block within raw file content (plain text scanning, not YAML
parsing — doesn't need YamlDotNet). Once the block's inner text is isolated,
call the injected parser on it and expose the result as a map plus lenses:

```fsharp
type FrontmatterMap = Map<string, Yaml.Node>
type ActorAt = { By: string; At: System.DateTimeOffset option }

let empty : FrontmatterMap = Map.empty

let parse (parseYaml: Yaml.Parse) (content: string) : FrontmatterMap =
    // 1. locate the frontmatter block (existing delimiter logic, kept as-is)
    // 2. parseYaml blockText -> match Ok (Yaml.Map kvs) -> Map.ofList kvs
    //    | _ -> Map.empty   (no block, or malformed YAML -> empty, never throws)

// --- Lenses over well-known fields ---
let private scalar key fm = match Map.tryFind key fm with
                             | Some (Yaml.Scalar s) when s <> "" -> Some s
                             | _ -> None

let description fm = scalar "description" fm
let type_       fm = scalar "type" fm
let title       fm = scalar "title" fm
let status      fm = scalar "status" fm
let resource    fm = scalar "resource" fm

let tags (fm: FrontmatterMap) : string list =
    match Map.tryFind "tags" fm with
    | Some (Yaml.Seq items) -> items |> List.choose (function Yaml.Scalar s -> Some s | _ -> None)
    | _ -> []

let private actorAt = function
    | Yaml.Map kvs ->
        let m = Map.ofList kvs
        match Map.tryFind "by" m with
        | Some (Yaml.Scalar by) ->
            let at = Map.tryFind "at" m |> Option.bind (function
                | Yaml.Scalar s -> match System.DateTimeOffset.TryParse s with true, d -> Some d | _ -> None
                | _ -> None)
            Some { By = by; At = at }
        | _ -> None
    | _ -> None

let generated fm = Map.tryFind "generated" fm |> Option.bind actorAt

let verified (fm: FrontmatterMap) : ActorAt list =
    match Map.tryFind "verified" fm with
    | Some (Yaml.Seq items) -> items |> List.choose actorAt
    | Some (Yaml.Map _ as m) -> actorAt m |> Option.toList     // bare mapping -> 1-element list, OKF §5.2
    | _ -> []

let staleAfter fm =
    scalar "stale_after" fm
    |> Option.bind (fun s -> match System.DateTimeOffset.TryParse s with true, d -> Some d | _ -> None)
```

Tag lowercasing/dedup and the description-precedence (manifest wins,
frontmatter is fallback) stay the caller's responsibility in `Sync.fs`,
exactly as today — the `tags`/`description` lenses just return the raw
parsed values, unchanged in behavior from the current `Parsed.Tags`/
`.Description`.

Tests: update `tests/Eru.Tests/FrontmatterTests.fs` to call
`Eru.Adapters.YamlAdapter.parse` as the injected parser (the test project
already references `Eru.Adapters.fsproj`) and assert via the lenses (e.g.
`Frontmatter.description fm`) instead of record-field access — all existing
description/tags cases should keep passing unchanged. Add cases per new lens
(present/absent, `verified` as bare mapping vs. list, malformed/missing
dates, unknown extra keys ignored/don't break other lenses).

### 5. Update the two existing call sites

- `Eru.Domain/Sync.fs` (lines ~125, ~176): `Frontmatter.parse content` →
  `Frontmatter.parse deps.ParseYamlBlock content`, then read fields via
  lenses instead of record access (`Frontmatter.tags fm`,
  `Frontmatter.description fm`, etc.) — see §7 below.
- `Eru.Search/CandidateBuilder.fs:90`: this module already `open`s
  `Eru.Adapters` directly (doesn't go through `Deps`), so:
  `Frontmatter.parse content` → `Frontmatter.parse YamlAdapter.parse content`,
  and update its `fm.Description`/`fm.Tags`-style accesses to the
  corresponding lenses.

### 6. `Eru.Domain/Config.fs` — extend `IndexEntry` (purely additive)

```fsharp
type IndexEntry = {
    Tags         : string list
    Description  : string option
    LocalPath    : string option
    CacheRelPath : string option
    ContentHash  : string option
    Type         : string option
    Title        : string option
    OkfStatus    : string option      // draft|stable|deprecated
    Generated    : Frontmatter.ActorAt option
    Verified     : Frontmatter.ActorAt list
    StaleAfter   : System.DateTimeOffset option
    Resource     : string option
}
```

(Named `OkfStatus` here — matches the `SiteDocument.Status` rename below and
keeps intent unambiguous even though `IndexEntry` has no existing `Status` to
collide with.)

### 7. `Eru.Domain/Sync.fs` — merge new fields into `IndexEntry`

Update `emptyIndexEntry` (line 42-48) with `None`/`[]` defaults for the new
fields.

In both merge blocks (Step 2, lines ~125-136; Step 3, lines ~176-184): none of
the new fields have a manifest-side equivalent (unlike `Tags`/`Description`,
which merge with `ManifestFileRef`), so take them straight from the lenses on
every (re-)parse:

```fsharp
let fm = Frontmatter.parse deps.ParseYamlBlock content
...
Tags        = (existing.Tags @ (Frontmatter.tags fm |> List.map (fun t -> t.ToLowerInvariant()))) |> List.distinct
Description = existing.Description |> Option.orElse (Frontmatter.description fm)
Type        = Frontmatter.type_ fm
Title       = Frontmatter.title fm
OkfStatus   = Frontmatter.status fm
Generated   = Frontmatter.generated fm
Verified    = Frontmatter.verified fm
StaleAfter  = Frontmatter.staleAfter fm
Resource    = Frontmatter.resource fm
```

Also update the one place that constructs a *full* `IndexEntry` record
literal (Step 4, lines ~205-211 — the "no existing entry" branch when setting
`LocalPath` from lock entries, which has no frontmatter content available) —
set the new fields to `None`/`[]` there too.

### 8. `Eru.Site/SiteModel.fs` — extend `SiteDocument`

Rename `Status: FileStatus` → `SyncStatus: FileStatus`. Add:

```fsharp
type SiteDocument = {
    ...
    SyncStatus  : FileStatus     // renamed from Status
    Type        : string option
    Status      : string option  // OKF lifecycle
    Generated   : Frontmatter.ActorAt option
    Verified    : Frontmatter.ActorAt list
    StaleAfter  : System.DateTimeOffset option
    Resource    : string option
}
```

Add a `SiteType` aggregate mirroring `SiteTag`, and `Types: SiteType list` on
`SiteModel`:

```fsharp
type SiteType = { Name: string; FileCount: int; Files: SiteDocument list }
```

### 9. `Eru.Site/IndexBuilder.fs` — populate the new fields

- Rename the `Status = status` record field to `SyncStatus = status`
  (line ~67).
- `Title = fileTitle remotePath` (line 63) becomes
  `entry.Title |> Option.defaultValue (fileTitle remotePath)` — frontmatter
  title wins, filename is the fallback, per OKF §4.1.
- Add passthroughs: `Type = entry.Type`, `Status = entry.OkfStatus`,
  `Generated = entry.Generated`, `Verified = entry.Verified`,
  `StaleAfter = entry.StaleAfter`, `Resource = entry.Resource`.
- After building `tags` (lines 83-90), build `types` the same way, grouping
  `allDocs` by `d.Type` (skipping `None`), and add `Types = types` to the
  final `SiteModel` record.

### 10. `Eru.Site/HtmlTemplates.fs` — render the new fields

- Update the sync-status reference in `fileCard` (line 71) from `doc.Status`
  to `doc.SyncStatus`.
- In `fileCard`: add a trust-tier badge derived from `doc.Verified` (§5.3 —
  no entries ⇒ unverified; any `By` starting with `"human:"` ⇒
  human-reviewed; else machine-confirmed), rendered like the existing status
  badge.
- In `filePage`'s `metaBox` (lines 239-248): extend with `Type` (if present),
  the trust badge, and a staleness warning when
  `doc.StaleAfter |> Option.exists (fun d -> DateTimeOffset.UtcNow >= d)`,
  following the existing "compute optional HTML fragment, wrap only if
  non-empty" pattern used for `descHtml`/`tagsHtml`.
- Rename the existing extensions sidebar section label (`indexPage`, line
  132, currently `<h3>Types</h3>` over `extLinks`/`AllExtensions`) to
  "File Extensions" — behavior/ids (`ext-filters`, `data-filter-ext`)
  unchanged, label text only.
- Add a "Types" sidebar section (now free to use that name) + listing/
  per-type pages mirroring the existing Tags implementation exactly
  (`typesPage`/`typeFilesPage` alongside `tagsPage`/`tagFilesPage`), reading
  from `model.Types`.

### 11. `Eru.Site/SiteGenerator.fs` — wire up new pages

Generate the type index + per-type pages the same way tag pages are
generated today (mirror the existing tag-page generation loop for
`model.Types`). Rename the other `doc.Status` reference in the file-page loop
(lines 709-733, matching on `Pulled | Cached`) to `doc.SyncStatus`.

### 12. Test project wiring

`tests/Eru.Tests/Eru.Tests.fsproj` has no `ProjectReference` to
`Eru.Site.fsproj` and no existing test files for it — add the reference, then
add focused tests for:
- `IndexBuilder.buildModel`: title fallback precedence, type grouping, new
  fields flowing from a fake `IndexEntry` into `SiteDocument`.
- `HtmlTemplates`: trust-tier derivation, staleness-warning rendering
  (present/absent/expired/not-yet-expired).

Follow the existing xUnit v3 + plain-English `Fact` naming convention seen in
`FrontmatterTests.fs`.

## Verification

- `dotnet build` — confirms `Eru.Domain` still has zero third-party package
  references, `Eru.Adapters` picks up `YamlDotNet` cleanly, and the
  `IndexEntry`/`SiteDocument` field rename/additions compile at their single
  exhaustive construction sites (`Sync.fs` Step 4 literal, `IndexBuilder.fs`
  record literal — the compiler will catch any missed field).
- `dotnet test` — updated `FrontmatterTests.fs`, new `Eru.Site` tests, and
  confirm no existing test (`SyncTests.fs`, `ConfigTests.fs`, `SearchTests.fs`)
  regresses from the parser swap or the `Status`→`SyncStatus` rename.
- Manually: `dotnet run --project src/Eru -- sync` against a source
  containing an OKF-style markdown file (`type`, `status`, `generated`,
  `verified`, `stale_after` in frontmatter), then
  `dotnet run --project src/Eru -- site generate` and open the generated
  `index.html`/file page/tag page in a browser to confirm the trust badge,
  staleness warning, and new Types section render as expected, and
  that a plain markdown file with no matching frontmatter still renders
  exactly as it does today (no regressions to the non-OKF path).
