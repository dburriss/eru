# Plan: attach a plain local folder as an OKF source (no git, no cache copy)

## Context
`eru source add <name> <path>` already works for a local path **if it is a git repo** (everything goes
through `git clone` in `GitAdapter.fs`). A plain folder (e.g. `~/second-brain`) fails. Goal: point a
source at such a folder and read it **in place** — no clone, no copy into `sources/<name>/files/`,
never deleted by `eru cache prune`/`clear`, and changes detected incrementally.

**Scope (v1): OKF bundles only.** A folder source is indexed only through its registered `okf`
bundles (markdown with `okf_version` index). No manifest/collection/`eru add` support for folders, no
non-markdown files. This reuses the existing OKF discovery path (`BundleDiscovery.walkBundle`,
Sync step 1d) rather than adding a new indexing mode.

Config shape: keep `url`; a `url` that is an existing directory without `.git` is a *folder source*.
No new config field. `--branch`, `--base-path` (manifest bundle) and manifest bundles are rejected for
folders; `--scan` (or an explicit okf bundle) is required, otherwise add warns "no bundles registered".

## Design
**Detection** — new `SourceKind` helper in domain (`src/Eru.Domain/SourceKind.fs`, + `.fsproj`):
`Folder` iff `Url` is an existing directory with no `.git`; else git. New `Deps` fns:
- `IsPlainFolder : string -> bool`
- `ScanLocalFolder : string -> string -> string list -> Result<(string * string) list, string>` —
  (root, bundlePath, ignorePatterns) → recursive, stat-only, `.md` files only, returns
  `(relPath '/'-separated, "mtimeTicks:size")`. Prunes whole directories matching `okf.ignorePatterns`
  (defaults `node_modules/**`, `inbox/**`, `apm_modules/**`) and always skips dot-directories. Only
  descends into registered okf bundle paths.
Implemented in a new `src/Eru.Adapters/FolderAdapter.fs`, wired in `AdapterDeps.fs`.

**Index** (`Config.fs` `IndexEntry`, ~line 379) — add `SourcePath: string option` (absolute file path)
and `Fingerprint: string option`; add to `emptyIndexEntry` and `Serialization.fs`. Old indexes read `None`
(index is disposable). OKF fields (Type/Title/Status/…) are filled as today.

**Sync** (`Sync.fs` step 1d) — for folder sources, replace the git `GetRemoteHeadSha`-gated walk with:
1. `ScanLocalFolder` per okf bundle; diff against existing entries' `Fingerprint`.
2. Only new/changed files are read and parsed (reuse the frontmatter→entry mapping in step 1d); write
   entry with `SourcePath` + `Fingerprint`, no `CacheRelPath`/`ContentHash`.
3. Unchanged sync = one stat-walk, zero file reads.
Steps 1a (manifest fetch), 2, 3 skip folder sources.

**Removed / renamed / newly-ignored files** — the scan is the source of truth each sync:
- Entries for files absent from the scan are deleted (deletes, renames = delete + add, files now matching
  an ignore pattern or moved into a dot-dir). Nothing cached, so nothing to prune.
- Between syncs, readers check the file exists and treat a missing `SourcePath` as not found / skip the
  candidate rather than erroring.
- If the scan fails or the folder is missing/unmounted, keep the old index and report a non-fatal error
  (an unmounted drive must not empty the index). An existing but genuinely empty folder clears entries.

**Readers prefer `SourcePath`** (fall back to `LocalPath`/`CacheRelPath` as today):
- `Read.fs readContent` (~line 52): add `trySourcePath` before `tryCache`; `fetchRemote` gives a clear error for folders.
- `CandidateBuilder.fs resolveAbsPath` (line 9): return `SourcePath` if it exists (search reads in place).
- `Eru.Site/IndexBuilder.fs` (~line 56), `SiteGenerator.fs` (~line 1652): read `SourcePath` when no `CacheRelPath`;
  `Views.sourceFileUrl` (~line 1668) returns no git link for folders.
- `McpServer.fs:30` startup `checkRemoteAccess`: for folders check the directory exists instead.

**`source add`** (`SourceAdd.fs`) — expand `~`, store absolute path, make `deriveNameFromUrl` handle `\`
and trailing separators; `scanBundles` uses `ScanLocalFolder`/`ReadLocalFile` for folders (finds
`index.md` with `okf_version`); reject `--branch`/`--base-path`. `source list/view` show the path with a
`(folder)` marker (`SourceList.fs`, `SourceView.fs`). `eru source bundle add` (`SourceBundleAdd.fs`)
works for Okf kind only on folders.

**Prune / clear safety** — `CachePruneCli.fs` only touches `sources/*/files/`; `CacheClearCli.fs` only
deletes the cache tree (index rebuilds next sync). Nothing is stored in `files/`, and the folder path is
never an input to either command. Tests assert this.

## Files
`src/Eru.Domain/{Config,Deps,Sync,Read,SourceAdd,SourceBundleAdd,SourceList,SourceView}.fs`, new
`SourceKind.fs`; `src/Eru.Adapters/{AdapterDeps,Serialization}.fs` + new `FolderAdapter.fs`;
`src/Eru.Search/CandidateBuilder.fs`; `src/Eru.Site/{IndexBuilder,SiteGenerator}.fs`; `src/Eru.Mcp/McpServer.fs`.
New `Deps` fields require updating each test's `makeDeps` (~10 files in `tests/Eru.Tests`) — use the
enable/disable commit (c6c2b4a) as the checklist.
Docs: `docs/reference/cli.md`, `config-file.md` (note `okf.ignorePatterns` also governs folder scans),
`docs/how-to/sync-and-maintain-cache.md`, `docs/tutorials/getting-started.md`, `skills/eru/SKILL.md` +
`references/commands.md`, `CHANGELOG.md` (Unreleased → Added), new `plans/local-folder-source.md`.
Per AGENTS.md: no Co-Authored-By/"Generated with" trailers; Conventional Commits; don't implement until
told "implement"; no new branch without asking.

## Verification
- Unit tests: `SourceKind` (git repo vs plain dir vs missing); incremental Sync (first sync indexes N
  files; unchanged re-sync reads 0; touch one → exactly one re-read; delete one → entry gone; folder
  missing → entries retained + error); ignore-pattern directory pruning; Read/CandidateBuilder resolve
  `SourcePath`; SourceAdd name/abs-path/flag rejection; prune leaves folder and index-less cache alone.
- `dotnet build && dotnet test`.
- Manual: `eru source add second-brain ~/second-brain --scan` → `eru sync` → `eru source view`,
  `eru print second-brain:<file>`, MCP `search_knowledge`, `eru site generate`; edit/delete a file and
  re-sync; run `eru cache prune` and `eru cache clear`, confirm the folder is intact.
