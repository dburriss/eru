---
status: done
---
# Plan: Replace built-in search with the Ferret library

## Context

Feedback on eru search: the MCP `search_knowledge` ORs terms with no ranking and no cap ("mock time unit tests" returned 68 results / 80 KB; "retries for AWS calls" 114 / 180 KB), the CLI uses different rules from the MCP, and generated `index.md` files appear next to their README. Eru has three hand-rolled backends (`SimpleScan`, `IndexedSearch`, `CkSearch` shelling out to `ck`) and none of them score or bound output.

`dburriss/ferret` (NuGet `Ferret` 0.3.0) now provides what the feedback needs as an embeddable library: BM25 lexical ranking with stemming and field boosts, optional semantic and hybrid (RRF) search, a persisted incremental index, `SearchResult { Docs; Truncated }`, `IgnorePatterns`, `Offline`, `IsModelAvailable`, `ModelUrl`, and cancellation. This plan replaces eru's search engines with `FerretClient`, defaulting to lexical.

Verified against Ferret 0.3.0 (scratch scripts, not eru): Truncated flag, `IsModelAvailable`, offline `ModelUnavailable`, mirror failure leaves no partial files, cancelled builds leave indexes untouched, hybrid 584 ms first call / 40 ms warm on one client.

## Current state (what changes)

| Surface | Code today | Behaviour |
|---|---|---|
| MCP `search_knowledge` | `src/Eru.Mcp/McpTools.fs` (backend picked from `ERU_SEARCH_BACKEND`), candidates from `src/Eru.Search/CandidateBuilder.fs` | OR, unranked, unbounded, all matching lines as excerpts; metadata-only candidates matched on path/description substring |
| `eru serve` `/api/search` | `src/Eru.Serve/SiteServeServer.fs:68-93` | same backends and candidates, copy-pasted backend switch |
| `eru search` CLI, TUI browse filter | `src/Eru.Domain/Search.fs` (`LocalSearch.execute`), `src/Eru.Cli/SearchCli.fs`, `src/Eru.Tui/Browse/FilesPane.fs:80-93` | metadata only (path, description, tags), OR + substring, no content |
| Index warm-up | `src/Eru.Mcp/IndexService.fs` (BackgroundService, timer) | runs `ck --index` or builds the per-file word index depending on `ERU_SEARCH_BACKEND` |
| Word index | `src/Eru.Adapters/SearchIndexAdapter.fs`, `Paths.searchIndexDir`, `AdapterDeps.fs:79`, `CacheClearCli.fs:46` | only used by `IndexedSearch` and `IndexService` |

Candidate sources (`CandidateBuilder.build`): source-index entries resolved to a local file or the per-source cache (`~/.cache/eru/sources/<name>/files`), lock entries not in any index, local `knowledge/` directories. Plus metadata-only entries (index entry, no file) built inline in `McpTools.fs`. Tags and descriptions come from index.json, collections and lock entries; `BlockPatterns`/`AllowPatterns` filter remote paths.

## Design

### 1. One shared ranked-search module in `Eru.Search`
New `src/Eru.Search/RankedSearch.fs` replaces the `SearchFn` backends. `Eru.Domain` stays pure and must not reference Ferret (it pulls ONNX Runtime). Input: `Deps`, `EffectiveConfig`, cwd, query terms, tags, mode, limit. Output: `RankedResult = { Hits: RankedHit list; Truncated: bool; Warnings: string list }` where `RankedHit` carries the existing candidate metadata plus `Score` and excerpt lines. MCP, `eru serve` and CLI all call it, so rules cannot diverge again.

Flow: build candidates (reuse `CandidateBuilder.build`) → tag filter (AND, eru-owned, Ferret has none) → ignore patterns → Ferret rank → map `ScoredDoc.File` back to candidates → apply `limit`/`Truncated` → append metadata-only hits.

### 2. Feeding Ferret eru's candidates
Eru's files are not under one directory (lock entries can live anywhere in the repo). Two options, decided by the spike (phase 0):
- **Preferred: custom `IFileSource`** passed to `FerretClient(options, files, index)`. It enumerates exactly the candidate absolute paths, so corpus stats cover only files eru would return and tag/ignore filtering happens before scoring (no over-fetch). Caveat: `IgnorePatterns` is ignored with a custom source (apply eru's patterns in the source), and the file contract says paths keep the root as prefix, so it needs a virtual root and confirmation that absolute paths are returned as-is.
- **Fallback: directory roots** (per-source `files/` dirs, `knowledge/` dirs, parent dirs of lock files outside those) with `IgnorePatterns` (root-relative!) and post-filtering by the candidate map. Needs over-fetch when tags are given, and our own `Truncated`.

### 3. Mode, defaults, model
- Default `lex`. New config setting `search.mode` (`lex | sem | hybrid`) plus env `ERU_SEARCH_MODE`, MCP parameter `mode`, CLI flag `--mode`. Follows the config v3 sections pattern (`src/Eru.Domain/Config.fs`, `ConfigCmd.fs` key list, `docs/reference/config-file.md`).
- Semantic and hybrid only run when `FerretClient.IsModelAvailable`; otherwise fall back to lex and report it in `Warnings` (MCP structured output and CLI stderr). Never download inside a search call: it risks the MCP request timeout eru already hit (`-32001`). The model is fetched by the background warm-up (below) or explicit CLI request.
- `ERU_SEARCH_BACKEND` is deprecated: `ck` maps to `hybrid`, `indexed`/`simple` to `lex`, with a one-time warning to the log (never stdout in the MCP stdio server).

### 4. Output contract
- MCP `search_knowledge`: add optional `limit` (default 15) and `mode`; structured result adds `truncated`, per-hit `score`, `excerpts` stay strings (backward compatible), hits ordered by rank. Descriptions drop the "OR semantics" wording. Ferret caps excerpts at 3 lines per file. No `totalMatches` (Ferret only exposes `Truncated`).
- `eru search`: terms now match file content and are ranked; `--limit`, `--mode`, score in table/json, text keeps `source:path [hash]`. Tag-only or empty-term queries still list artifacts (keep `LocalSearch.execute` for that and for the TUI filter, or route through the new module; see open question).
- `eru serve /api/search`: same module, same JSON shape (`Hits` + `truncated`).

### 5. Ignore patterns (`index.md`, `log.md`, etc.)
- New `search.ignorePatterns` config (config v3 `search` section, same shape and merge rules as `site.ignorePatterns` / `okf.ignorePatterns`), default `["index.md"; "log.md"]`. Local overrides global; `eru config set search.ignorePatterns '[...]'` works like the other keys.
- **Separate from `site.ignorePatterns` on purpose.** The site default is `["index.md"; "log.md"; "README.md"]` (`Config.defaultSiteIgnorePatterns`), so reusing it would hide READMEs from search, and READMEs are exactly what the feedback wants returned (one hit per solution, not README plus `index.md`). A README with a frontmatter `type` is also a concept now (commit b667dd3).
- **Matching reuses `Patterns.matchesAny`** (`src/Eru.Domain/Patterns.fs`): a pattern without `/` matches the file name at any depth (`index.md` hits `a/b/index.md`); a pattern with `/` matches the full path. Applied to the candidate's remote path (source files) or relative path (lock and local files), so it behaves the same for all three candidate origins.
- **Applied before scoring**, as a candidate filter (custom `IFileSource` approach) or as Ferret `IgnorePatterns` on each root (fallback approach). Ignored files never inflate document frequency or average length, so ranking of the remaining files is unaffected. Ferret's own `.gitignore`/`.ignore`/`.ferretignore` handling still applies under directory roots, which is why eru's patterns are passed explicitly instead of relying on a `.ferretignore` file in cache dirs.
- Not ignored by default: READMEs, and anything else not listed. Other noise (for example `apm_modules/**`, `node_modules/**`, `inbox/**` as in `okf.ignorePatterns`) is left to the user to add; Ferret already skips hidden directories, symlinks and binary files.

### 6. Index, cache and lifecycle
- Register one `FerretClient` as a singleton in `src/Eru.Mcp/McpServer.fs` (it holds the loaded model: 584 ms cold vs 40 ms warm); CLI creates one per invocation. Cache dir under eru's cache (`~/.cache/eru/ferret`) via `Paths`, so it is disposable and discoverable.
- Rewrite `IndexService` to warm Ferret: `BuildIndexAsync(mode, ct)` at start and on the existing timer, passing the host cancellation token; if mode is sem/hybrid and download is allowed, `EnsureModelAsync` first (off the request path).
- `eru cache clear` (`CacheClearCli.fs`) replaces `Paths.searchIndexDir()` with the Ferret cache dir.

### 7. Removal
Delete `SimpleScan.fs`, `IndexedSearch.fs`, `CkSearch.fs`, `SearchTypes.fs` (`SearchFn`), `src/Eru.Adapters/CkAdapter.fs`, `SearchIndexAdapter.fs`, `Paths.searchIndexDir`, the `AdapterDeps.fs:79` indexing call, and the backend switches in `McpTools.fs` and `SiteServeServer.fs`. Old `~/.cache/eru/index` is cleaned by `cache clear`. `skills/semantic-search` (shells out to `ck` for repo-wide search) is a separate skill and stays out of scope.

## Risks (verify in the spike before committing to the plan)
1. **Trimming and single-file publish.** `publish-gh.yml` publishes `PublishTrimmed=true` single-file binaries (linux-x64, win-x64, osx-x64). Ferret declares no `IsTrimmable`/AOT support, and ONNX Runtime ships a native library that must sit beside the exe (Ferret's own 0.2.0 fixed exactly this). Needs a trimmed publish smoke test per RID, size delta (~45 MB native per platform) accepted.
2. **Package floor.** `Ferret` requires FSharp.Core >= 10.1.403; confirm eru's SDK/projects resolve it without NU1605 breaking the build.
3. **Custom `IFileSource` semantics** (virtual root, absolute paths outside root, semantic index keyed by root hash) are untested. Fallback in design section 2.
4. **CI.** `build.yml` runs Ubuntu only; ORT loading and model-less lexical tests must not need a download. Tests use lexical or a stub; no network.
5. **Ferret gaps to track upstream** (non-blocking): sync `Search` wraps `ModelUnavailable` in `AggregateException`; `IgnorePatterns` are root-relative and undocumented; hybrid excerpts can land on frontmatter lines; no total match count.

## Delivery (decided: everything ships together)

Decisions from the user: the CLI switches to ranked content search; `ERU_SEARCH_BACKEND` is deprecated with a mapping (not removed); lex, sem and hybrid ship in one change (no separate lex-only release).

0. **Spike first** (scratch, no repo changes): custom `IFileSource` over a candidate list; trimmed single-file publish on this machine with ORT; FSharp.Core resolution; run the three feedback queries on the reporter's corpus if available. Because everything ships at once, a failed trim/ORT check blocks the whole change, so this runs before any repo work. If trimming cannot work, fall back to shipping semantic/hybrid behind an optional package or untrimmed archive and say so in the plan.
1. **One change, committed in logical steps** (conventional commits, one PR): `RankedSearch` + candidate mapping + tags + `search.ignorePatterns` + `limit`/`Truncated` → MCP/Serve/CLI wired → `search.mode`, `IsModelAvailable` gating and fallback warnings, background model/index warm-up, CLI `--mode` → `IndexService` rewrite, deletions, cache clear → docs and CHANGELOG.

Per `AGENTS.md`, no code until the word "implement"; this plan lives in `plans/ferret-search.md`.

## Files

- New: `src/Eru.Search/RankedSearch.fs`, `src/Eru.Search/FerretFileSource.fs` (if custom source), tests `tests/Eru.Tests/RankedSearchTests.fs`, `plans/ferret-search.md`
- Modify: `src/Eru.Search/Eru.Search.fsproj` (add `Ferret`, update compile list), `src/Eru.Mcp/McpTools.fs`, `McpServer.fs`, `IndexService.fs`, `src/Eru.Serve/SiteServeServer.fs`, `src/Eru.Cli/SearchCli.fs`, `Args.fs` (flags), `CacheClearCli.fs`, `src/Eru.Domain/Config.fs`, `ConfigCmd.fs`, `src/Eru.Adapters/Paths.fs`, `AdapterDeps.fs`, docs (`docs/reference/mcp-server.md`, `inspecting-state-and-search.md`, `config-file.md`, `docs/how-to/set-up-the-mcp-server.md`), `CHANGELOG.md`
- Reuse as-is: `CandidateBuilder.build`, `Patterns.isPathBlocked`, `SourceIndexAdapter.readIndex`, collection-tag join in `LocalSearch.execute`, `KnowledgeSyncService.CurrentEff`

## Verification

- Unit: `RankedSearchTests` on temp-dir fixtures reproducing the feedback (README with "retries" + AWS, health-check READMEs, generated `index.md`): rare-term file outranks common-term files; `mock` finds "mocked"; `index.md` excluded by default; `limit` and `Truncated`; tag filter with ranking; metadata-only hits appended; mode fallback warning when model missing (`Offline`, empty cache); same query gives same order via MCP, serve and CLI entry points.
- Existing `SearchTests.fs` keeps passing for `LocalSearch` listing/tag behaviour.
- `dotnet build`, `dotnet test --solution eru.slnx`.
- Manual: `mise run mcp` and call `search_knowledge` with the three feedback queries (response size, order, `truncated`); `eru search --limit 5 "retries aws"`; `eru serve` `/api/search?q=`; `eru cache clear --dryrun` lists the Ferret cache; trimmed publish for the host RID starts and searches.

## Resolved decisions

- CLI `eru search` switches to ranked content search (shared engine with MCP and serve). Tag-only and empty-term queries still list artifacts. Behaviour change goes in CHANGELOG.
- `ERU_SEARCH_BACKEND` deprecated with mapping (`ck` → `hybrid`, `indexed`/`simple` → `lex`) and a logged warning.
- Lex, sem and hybrid ship together.

- CLI `--mode sem|hybrid` (or `search.mode` set to either) with the model missing: start the model download and fall back to lex for this run. Concretely: print the lex results with a stderr warning, then await `EnsureModelAsync(ct)` with progress on stderr (Ctrl+C cancels cleanly, leaving no partial file) so the next run is semantic. `--offline`/`FERRET_OFFLINE=1` skips the download and only warns. The MCP never downloads inside a search call: it falls back to lex with a warning and the background warm-up fetches the model.
- `simple` is not a real `ERU_SEARCH_BACKEND` value today: unset or any unknown value selects `SimpleScan`, which is the current default. It is deleted; the default becomes Ferret lex. Explicit `simple`/`indexed` map to `lex` with a deprecation warning.

- `skills/semantic-search` (ck-based) is out of scope; noted as a follow-up. The existing `ferret` skill covers repo-wide search.

## Outcome

Implemented as planned, with these results and deviations:

- **Spike: custom `IFileSource` works** (virtual root, absolute paths returned as-is, lexical and hybrid), but it must be given the **full, stable candidate set** on every call. Ferret drops index entries for files missing from the enumerated set, so giving it a tag-filtered subset per query re-embedded the dropped files each time (3.4 s instead of 140 ms). Tags are therefore filtered after ranking, with Ferret asked for every ranked file when tags narrow the result. Ignore patterns are applied to the candidate list (stable, config-driven), not via Ferret's root-relative `IgnorePatterns`.
- **Spike: trimmed single-file publish.** A toy app using Ferret publishes trimmed with no IL warnings and runs lexical and hybrid. ONNX Runtime does not travel inside a single-file exe unless `-p:IncludeNativeLibrariesForSelfExtract=true` is set; that flag is now in `publish-gh.yml` (a 62.6 MB single file instead of 17 MB plus a 45 MB library next to it). The real `eru` binary published **trimmed crashes at startup in Argu** (`MissingMethodException ... ShapeArgumentTemplate<InitArgs>`), and so does a trimmed build of the untouched `HEAD`: the trimmed release binaries were already broken before this change. The untrimmed single-file build (177 MB) was verified for `version`, `config get`, lexical and hybrid search.
- **FSharp.Core.** `Ferret` needs FSharp.Core >= 10.1.403; a root `Directory.Build.props` sets `FSharpCoreImplicitPackageVersion` as Ferret's own repo does.
- **`Deps.BuildSearchIndex` removed** (and its four calls in sync and bundle add, plus the no-op field in every test `Deps`): search indexes are now built on first use and by the MCP `IndexService`, not at sync time.
- **MCP structured output** is serialised with `Serialization.options` so the content is camelCase (`hits`) as the advertised output schema already said; it was PascalCase before.
- **Bug found and fixed:** `CandidateBuilder` probed `knowledge` and `KNOWLEDGE` and listed every local file twice on case-insensitive file systems; it now enumerates the real directory names.
- **Offline.** There is no `--offline` flag; `FERRET_OFFLINE=1` (read by Ferret) is the switch, as for `FERRET_MODEL_DIR` and `FERRET_MODEL_URL`.
- **Not changed:** the TUI browse filter and `eru search` with no terms still use `LocalSearch` (metadata and tags only); the model is downloaded into eru's own cache (`~/.cache/eru/ferret`), not shared with a `ferret` CLI install; `skills/semantic-search` still uses `ck`.
