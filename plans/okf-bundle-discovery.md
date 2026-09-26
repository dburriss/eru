---
status: planned
---

# Plan: OKF bundle discovery

> Supersedes the design discussion archived at
> `plans/archive/okf-bundle-discovery.md`. All decisions made there stand;
> this doc turns them into a concrete, sequenced implementation plan and
> resolves the remaining open questions.

## Context

`eru` currently has exactly one way for a source repo to advertise "here are
the files I publish": a hand-written `.eru/manifest.json`, referenced via
`SourceConfig.BasePath: string option`. This plan introduces a second,
self-describing mechanism: an **OKF bundle** — a directory whose `index.md`
carries `okf_version`, where membership/metadata is *discovered* by walking
the tree and reading frontmatter rather than declared in a side-car file.

This requires replacing the single `BasePath` with a `Bundle list` per
source (a source can have zero or more bundles, possibly nested), reshaping
`sources/<name>/index.json` so a file's `Tags`/`Description` are tracked as
per-bundle **contributions** (fixing a latent staleness bug where frontmatter
tags only ever accumulate, never shrink), adding a discovery walk that reuses
the existing batched-fetch machinery (`plans/sync-batch-fetch.md`), and a new
`eru source bundle add/list/remove` CLI group. `.eru/eru.lock` and
`.eru/manifest.json`'s own schema are unchanged; `config.json` gets a
lossless v1→v2 migration.

Implementing the full plan in one pass. Cache invalidation is source-level
only (one `git ls-remote` per source, not per-bundle). `eru source
view`/`eru source files` will group output by bundle and support a
`--bundle <path>` filter.

### Resolved design decisions (beyond the archived discussion doc)

1. **Manifest scoping across multiple bundles.** Today `eru sync` fetches
   exactly one `.eru/manifest.json` from the repo root per source. For each
   Manifest-kind bundle at a non-root path, first try fetching
   `<bundle.Path>/.eru/manifest.json` (added as an extra candidate path in
   the same batched `FetchRemoteContent` call). If present, it's
   authoritative for files under that path. If absent, fall back to
   partitioning the shared root `.eru/manifest.json`'s entries by
   longest-matching path prefix. Root-path bundles just use the root
   manifest directly (no change from today).
2. **`eru source bundle add` config scope.** No new `--global` flag; the
   bundle is appended to whichever config (local or global) already owns
   that source's record, mirroring how the source itself was created.
3. **HEAD-SHA check failure policy.** Track
   `ConsecutiveShaCheckFailures: int` on `SourceIndex` (default 0). On a
   `GetRemoteHeadSha` failure: increment and fail open (skip re-discovery,
   serve cached data) unless the counter reaches 3, at which point add a
   hard error for *that source* to `Sync`'s existing per-source error list
   (other sources still sync normally). Any successful SHA check resets the
   counter to 0.

## Execution order (keep `dotnet build` green throughout)

### Step 0 — Serialization groundwork
Add a `JsonConverter<BundleKind>` (lowercase string `"manifest"`/`"okf"`) to
`src/Eru.Adapters/Serialization.fs`, next to the existing
`OptionConverterFactory`, registered in `Serialization.options.Converters`.

### Step 1 — `Bundle` type, `SourceConfig.Bundles`, config v1→v2 migration
- `src/Eru.Domain/Config.fs`: add `type BundleKind = Manifest | Okf` and
  `type Bundle = { Path: string; Kind: BundleKind }` (`Path = ""` means repo
  root). Change `SourceConfig` to `Bundles: Bundle list` (was
  `BasePath: string option`; `None` → `[]`, `Some p` → `[{Path=p; Kind=Manifest}]`).
- Add a `Bundle` helper module (pure, no I/O) in `Config.fs`:
  `covers`, `coveringBundles`, `mostSpecificManifestBundle`,
  `owningBundleForDisplay` (falls back to most-specific covering bundle of
  any kind when no Manifest bundle covers a path) — used by `Add.fs`,
  discovery, the blend function, and view/list rendering.
- Add `ContributionKey` helpers: `manifest (bundlePath) = $"manifest:{bundlePath}"`,
  `frontmatter = "frontmatter"`.
- Add `BundleDetect` helper: `candidatePath` (existing `KNOWLEDGE`/`knowledge`
  top-level match, unchanged) + `detectKind (indexMdHasOkfVersion: bool) : BundleKind`
  (`Okf` if true, else `Manifest` — matches today's "assume manifest" default,
  no new existence check for `.eru/manifest.json` itself, to avoid scope
  creep beyond what's needed).
- Add `Frontmatter.okfVersion` lens (`scalar "okf_version"`) next to `resource`,
  for symmetry, used by `BundleDetect`'s caller.
- **Migration**: move to the adapter boundary, not `Config.merge`. Add
  `Config.readAndMigrateGlobalJson` / `readAndMigrateLocalJson` in `Config.fs`:
  try deserializing the current shape; on failure (old JSON has `basePath`,
  no `bundles` — F# record deserialization of a missing required field
  throws), fall back to a private `SourceConfigV1`/`GlobalConfigV1`/`LocalConfigV1`
  shape and map `BasePath` → `Bundles` as above, setting `Version = 2` on the
  in-memory result (not written back until the next explicit save — "refresh
  in place"). Bump `checkVersion`'s `supportedVersion` to `2`; it remains a
  pure upper-bound guard, migration happens upstream in
  `ConfigAdapter.readGlobalConfig`/`readLocalConfig`.
- **Every `BasePath` call site** (compiler-forced, fix in this same step):
  `Add.fs` (`resolveRemotePath`, `deriveLocalPath`'s caller, `resolveShortHash`,
  `detectBasePath`/`ensureSource`), `SourceAdd.fs` (`Command`, `execute`),
  `SourceView.fs`, `SourceList.fs`, and their CLI files. For `Add.fs`'s bare
  (non-prefixed) path convenience feature under a multi-bundle source: use
  the **first bundle in declaration order** as the resolution root — a new
  case not previously covered; single-bundle behavior is unchanged.
- Extend `SourceAdd.fs execute` and `Add.fs ensureSource`'s auto-detect: after
  finding the candidate path (`KNOWLEDGE`/`knowledge` dir or root), fetch
  `<candidate>/index.md` once via `Deps.FetchRemoteContent`, check
  `Frontmatter.okfVersion`, decide `Kind` via `BundleDetect.detectKind`. If no
  candidate directory and no OKF signal, preserve today's exact behavior:
  `Bundles = []` (no bundle forced into existence at root).

### Step 2 — `sources/<name>/index.json` reshape + versioning + SHA cache
- `Config.fs`: add `Contribution = { Tags: string list; Description: string option }`;
  reshape `IndexEntry` to hold `Contributions: Map<string, Contribution>` plus
  the existing frontmatter-exclusive fields, plus **computed** `Tags`/`Description`
  fields that every existing consumer (Search, SourceFiles, McpTools, LinkGraph,
  SourceView) keeps reading unchanged. Add
  `SourceIndex = { Version: int; SourceHeadSha: string option; ConsecutiveShaCheckFailures: int; Entries: Map<string, IndexEntry> }`.
- `src/Eru.Adapters/SourceIndexAdapter.fs`: `readIndex`/`writeIndex` operate on
  `SourceIndex`; on deserialize failure or `Version` mismatch, return `Ok None`
  (forces full rebuild — index.json is fully disposable/reconstructible).
  `normalize` extended to null-guard `Contributions`.
- `src/Eru.Domain/Deps.fs`: `ReadSourceIndex`/`WriteSourceIndex` signatures
  updated to `SourceIndex`; add `GetRemoteHeadSha: string -> string option -> Result<string, string>`.
  New adapter fn `GitAdapter.getRemoteHeadSha` (`git ls-remote <url> <branch-or-HEAD>`,
  parse first token), alongside `checkRemoteAccess`/`listRemoteTopLevel`.
- **Every** `ReadSourceIndex`/`WriteSourceIndex` call site (`Sync.fs` ×5,
  `LinkGraph.fs`, `SourceFiles.fs`, `Search.fs`, `src/Eru.Mcp/*`, site modules
  if any) updated: unwrap `.Entries`, and on write, **read-and-preserve** the
  existing `SourceHeadSha`/`ConsecutiveShaCheckFailures` rather than clobbering
  them with defaults — this is the single most important correctness detail
  (a manifest/collection-file sync must not blow away the discovery SHA cache).

### Step 3 — Discovery walk: `src/Eru.Domain/BundleDiscovery.fs`
- Extract `Frontmatter.classifyFile : relPath -> IndexFile | LogFile | ConceptFile`
  into `Frontmatter.fs`; refactor `OkfValidate.fs` to use it (pure refactor,
  `OkfValidateTests.fs` should pass unchanged).
- `BundleDiscovery.walkBundle deps sourceName sourceUrl branch bundle`:
  - List candidate `.md` paths via `Deps.ListRemoteFiles` scoped to
    `bundle.Path` (`None` if root) — **re-prefix results with `bundle.Path + "/"`**
    before treating them as `RemotePath`/`EntryId` (today's `ListRemoteFiles`
    returns bundle-relative paths; `RemotePath` must stay repo-root-relative
    — this needs its own regression test).
  - One `Deps.FetchRemoteContent` call for all candidate paths (reuses the
    already-implemented batched sparse-checkout from `sync-batch-fetch.md`).
  - Per file: `Frontmatter.classifyFile`, then either empty metadata
    (index.md/log.md) or full `Frontmatter.*` lens extraction (concept files).
  - Not batched *across* bundles in this pass (each bundle's own walk is
    independent) — acceptable since bundle registration is an on-demand,
    opt-in cost; flagged as a possible future optimization.

### Step 4 — `Sync.fs populateIndex` restructure
- New pure `IndexBlend.blend bundles remotePath contributions` in `Config.fs`:
  Tags = union of all `Contribution.Tags` across every key (always additive,
  never lossy — fixes the staleness bug since contributions are stored, not
  pre-blended); Description = most-specific covering Manifest bundle's own
  contribution if present, else the bundle's own manifest lookup falls back
  per decision #1 above, else the `"frontmatter"` contribution.
- Manifest rebuild step (today's "wipe and reseed"): for each
  `ManifestFileRef`, resolve its owning bundle (try `<bundle.Path>/.eru/manifest.json`
  first per decision #1, else partition the shared root manifest by
  longest-prefix match via `Bundle.mostSpecificManifestBundle`), write into
  `Contributions[ContributionKey.manifest bundlePath]` — never a flat field.
- Collection-file / lock-only-orphan fetch steps: replace
  `existing.Tags @ Frontmatter.tags fm |> distinct` (accumulating, buggy) with
  `Map.add ContributionKey.frontmatter { Tags = ...; Description = ... }`
  (full overwrite of that one key every sync — this is the actual fix), then
  recompute blended `Tags`/`Description` via `IndexBlend.blend`.
- New step (before the collection-file fetch): for each source with
  registered `Okf`-kind bundles, check `GetRemoteHeadSha` against the cached
  `SourceHeadSha`; if unchanged, skip the walk entirely; if changed (or no
  cache), run `BundleDiscovery.walkBundle` per Okf bundle, merge results into
  `Contributions["frontmatter"]` + blend, update `SourceHeadSha`. Apply the
  fail-open/3-strikes failure policy (decision #3) around the SHA check itself.

### Step 5 — CLI: `eru source bundle add/list/remove`
- `src/Eru.Cli/Args.fs`: new `SourceBundleAddArgs` (`MainCommand` tuple
  `source: string * path: string`, optional `--kind`/`-k`, `Dryrun`, `-o`/`Output`),
  `SourceBundleListArgs` (`MainCommand` single `source`, `-o`/`Output`),
  `SourceBundleRemoveArgs` (tuple `source * path`, `Dryrun`, `-o`/`Output`),
  grouped under `SourceBundleArgs` (mirrors `ManifestArgs`'s
  `[<SubCommand>] Add/List/Remove` shape), wired as a fourth
  `[<SubCommand>] Bundle of ParseResults<SourceBundleArgs>` case on `SourceArgs`.
  Accept `"."` or `"/"` as a repo-root sentinel for `path`, normalized to `""`
  in the domain command.
- New domain modules (mirror `ManifestAdd.fs`/`ManifestRemove.fs` shape):
  `SourceBundleAdd.fs` (locate source in whichever config owns it per
  decision #2 → reject exact-path duplicates → resolve `Kind` explicitly or
  via `BundleDetect` → append `Bundle` → write config → if `Okf` and not
  dry-run, trigger a one-bundle `BundleDiscovery.walkBundle` and merge into
  that source's index rather than a full resync), `SourceBundleList.fs`
  (return `src.Bundles`), `SourceBundleRemove.fs` (mirrors `SourceRemove.fs`;
  no immediate index cleanup needed — next `eru sync`'s full contribution
  rebuild naturally drops the removed bundle's contributions).
- New CLI files `SourceBundleAddCli.fs`/`SourceBundleListCli.fs`/`SourceBundleRemoveCli.fs`
  mirroring `ManifestAddCli.fs`'s active-pattern dispatch (one extra
  `TryGetSubCommand()` nesting level vs `Manifest`/`Collection`). Wire into
  `src/Eru.Cli/Program.fs` next to the existing `Source*Cmd` arms.

### Step 6 — `SourceView`/`SourceList`/`SourceFiles` rendering
- `SourceDetail.BasePath` → `SourceDetail.Bundles: Bundle list`; compute each
  file's owning bundle via `Bundle.owningBundleForDisplay`; group rendering by
  bundle path (plus an "(unassigned)" group for anything uncovered).
- `SourceRow.BasePath` → `SourceRow.Bundles: Bundle list` (raw list kept on
  the domain type for `--output json`; CLI renderers format the display
  summary string, consistent with existing convention).
- `Args.fs`: add `--bundle`/`-b` to `SourceViewArgs` and `SourceFilesArgs`;
  plumb an optional `bundleFilter` through `SourceView.execute`/`SourceFiles.execute`
  to scope output to one bundle's group.

### Step 7 — Tests (throughout, not just at the end)
- Fixture updates (compile-breaking `BasePath`/`IndexEntry` literals):
  `ConfigTests.fs`, `SourceTests.fs`, `AddTests.fs`, `SyncTests.fs`,
  `LinkGraphTests.fs`, `IndexBuilderTests.fs`, `SiteGeneratorGraphTests.fs`,
  `SearchTests.fs`.
- New/expanded coverage: `ConfigTests.fs` (v1→v2 migration round-trip,
  `checkVersion` rejects v3); `SyncTests.fs` (the staleness-bug regression —
  a dropped frontmatter tag must not survive a sync; `SourceHeadSha`
  preserved across non-discovery writes; discovery skipped when SHA
  unchanged; 3-strikes failure escalation); `FrontmatterTests.fs`
  (`classifyFile`, `okfVersion`); new `BundleDiscoveryTests.fs` (classification,
  frontmatter extraction, path re-prefixing regression); new
  `SourceBundleTests.fs` (add/list/remove, duplicate-path rejection,
  nested-path acceptance, auto-detect); new `IndexBlendTests.fs` (pure unit
  tests for tag union, longest-path-wins, frontmatter fallback, zero-bundle
  default — the crux of the merge precedence rule, deserves the most direct
  coverage).

## Critical files
- `src/Eru.Domain/Config.fs` — `Bundle`/`BundleKind`/`Contribution`/`SourceIndex`
  types, `Bundle`/`ContributionKey`/`BundleDetect`/`IndexBlend` helper modules,
  migration functions.
- `src/Eru.Domain/Sync.fs` — `populateIndex` restructure (contributions +
  discovery step + blend).
- `src/Eru.Adapters/SourceIndexAdapter.fs`, `src/Eru.Domain/Deps.fs` — index
  I/O reshape, `GetRemoteHeadSha`.
- `src/Eru.Domain/BundleDiscovery.fs` (new) — the discovery walk.
- `src/Eru.Domain/SourceAdd.fs`, `src/Eru.Domain/Add.fs` — auto-detect Kind
  extension, `BasePath` → `Bundles` call sites.
- `src/Eru.Cli/Args.fs` — new `SourceBundle*Args` CLI group, `--bundle` flags.
- `src/Eru.Domain/SourceView.fs`, `SourceList.fs`, `SourceFiles.fs` — grouped
  rendering.
- `src/Eru.Domain/Frontmatter.fs` — `classifyFile`, `okfVersion` lens.

## Verification
- `dotnet build` after each numbered step to catch compiler-forced call
  sites early (the `BasePath`→`Bundles` and `IndexEntry` reshapes are
  designed to be caught this way).
- `dotnet test` (xUnit v3, `tests/Eru.Tests`) after each step; all listed
  fixture updates and new test files must pass.
- Manual smoke test: `eru source add <a real OKF-bundled repo>` → confirm
  auto-detected `Kind = Okf`; `eru sync` → confirm `sources/<name>/index.json`
  gains discovered entries with correct `Tags`/`Type`/`Title` and a
  `SourceHeadSha`; re-run `eru sync` immediately → confirm no re-walk
  (add temporary logging or a debug flag if needed to observe this); edit a
  concept file's frontmatter to drop a tag, re-sync, confirm the tag is
  actually gone from `index.json` (the staleness-bug fix); run
  `eru source bundle add/list/remove` end-to-end; run
  `eru source view <name>`/`eru source files <name>` with and without
  `--bundle` to confirm grouped output.
