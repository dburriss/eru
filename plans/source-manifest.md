---
status: done
---
# Plan: .eru/ directory, source manifest

---
status: done
---

## Context

`eru.json` and `eru.lock` live in the repo root, cluttering it. More importantly,
there is currently no way for a _source_ repo to declare what files it serves —
consumers must hardcode `CollectionFileRef` lists themselves. This plan:

1. Moves local consumer files into `.eru/` (clean break, no backward compat)
2. Introduces `.eru/manifest.json` as the standard way for a source repo to
   declare its knowledge artifacts
3. Caches fetched manifests locally and merges them into `EffectiveConfig` at
   runtime so sync/search/MCP see the full picture

No migration path. The project is not in use yet.

---

## 1. Path changes — `src/Eru.Adapters/Paths.fs`

| Function | Before | After |
|---|---|---|
| `localConfigPath cwd` | `<cwd>/eru.json` | `<cwd>/.eru/config.json` |
| `lockFilePath cwd stateFile` | `<cwd>/<stateFile\|eru.lock>` | `<cwd>/.eru/<stateFile\|eru.lock>` |
| `sourceCacheManifestPath name` | _(new)_ | `~/.cache/eru/sources/<name>/manifest.json` (XDG-aware, mirrors existing `globalConfigPath` platform logic) |

`lockFilePath` changes only the prefix — the filename (`eru.lock` or whatever
`StateFile` is set to) is unchanged. The default in `Config.merge`
(`Option.defaultValue "eru.lock"`) stays as-is.

---

## 2. New domain types — `src/Eru.Domain/Config.fs`

Add above the existing types:

```fsharp
// Declared by a source repo at .eru/manifest.json
// Path supports glob patterns (same gitignore-style semantics as CollectionFileRef.RemotePath):
//   "docs/*.md"      — all .md files in docs/
//   "dotnet/**/*.md" — recursive match under dotnet/
//   "README.md"      — exact path
type ManifestFileRef = {
    Path        : string
    Tags        : string list
    Description : string option
}

type SourceManifest = {
    Version : int      // kept for future format evolution
    Files   : ManifestFileRef list
}
```

Extend `EffectiveConfig` with:

```fsharp
type EffectiveConfig = {
    Sources      : SourceConfig list
    CommitOnPull : bool
    StateFile    : string
    Collections  : CollectionFileRef list   // NEW — merged from user config + cached manifests
}
```

In `Config.merge`, populate `Collections` by flattening `GlobalConfig.Collections`:

```fsharp
Collections =
    globalCfg
    |> Option.map (fun g -> g.Collections |> List.collect (fun col -> col.Files))
    |> Option.defaultValue []
```

Add `Config.withManifests` (called after `Config.merge`, enriches collections
from on-disk cached manifests):

```fsharp
let withManifests
    (readCachedManifest: string -> Result<SourceManifest option, string>)
    (cfg: EffectiveConfig) : EffectiveConfig
```

Logic: for each source, read its cached manifest; convert `ManifestFileRef →
CollectionFileRef`; append only entries whose `(Source, RemotePath)` pair is
not already in `cfg.Collections` (user-explicit wins on collision).

---

## 3. New Deps — `src/Eru.Domain/Deps.fs`

```fsharp
ReadCachedManifest  : string -> Result<SourceManifest option, string>
                      // sourceName → parsed manifest or None if not cached
CacheSourceManifest : string -> string -> Result<unit, string>
                      // sourceName, rawJsonContent → parse + write to disk
```

Keeping JSON parsing in the adapter layer (domain never touches JSON directly),
consistent with how `ConfigAdapter` handles `GlobalConfig`/`LocalConfig`.

---

## 4. Init changes — `src/Eru.Domain/Init.fs`

- Change `configPath` to `Path.Combine(dir, ".eru", "config.json")`
- `WriteLocalFile` already creates parent directories (see `AdapterDeps.writeFile`),
  so `.eru/` is created automatically
- Update user-facing strings:
  - `"eru.json already exists."` → `".eru/config.json already exists."`
  - `"Initialized eru.json in %s"` → `"Initialized .eru/config.json in %s"`

---

## 5. Sync changes — `src/Eru.Domain/Sync.fs`

Add a manifest-refresh pass **before** the existing file-sync loop:

```fsharp
// 1. Refresh manifests for all sources (best-effort, silent on missing)
for src in effectiveCfg.Sources do
    match src.Url with
    | None -> ()
    | Some url ->
        let branch = src.Branch |> Option.defaultValue "HEAD"
        match deps.FetchRemoteContent url branch ".eru/manifest.json" with
        | Error _             -> ()   // no manifest — normal, skip silently
        | Ok []               -> ()   // empty result (post-glob-plan: shouldn't happen for an exact path)
        | Ok ((_, raw) :: _)  -> deps.CacheSourceManifest src.Name raw |> ignore

// 2. Enrich effective config with now-current cached manifests
let effectiveCfg = Config.withManifests deps.ReadCachedManifest effectiveCfg

// 3. Existing lock-file sync loop (unchanged) ...
```

`.eru/manifest.json` is always fetched as an **exact path** (never a glob), so
`FetchRemoteContent` returns at most one item. The `((_, raw) :: _)` pattern
is forward-compatible with the glob plan's `Result<(string * string) list, string>`
return type.

`DryRun` does **not** skip manifest refresh — manifests are metadata, not
content writes to the consumer repo.

---

## 6. Source.add changes — `src/Eru.Domain/Source.fs`

After writing the source config successfully, attempt a manifest fetch:

```fsharp
match src.Url with
| None -> ()
| Some url ->
    let branch = resolvedBranch   // whatever branch was used for top-level listing
    match deps.FetchRemoteContent url branch ".eru/manifest.json" with
    | Error _            -> ()   // no manifest — normal
    | Ok []              -> ()
    | Ok ((_, raw) :: _) ->
        match deps.CacheSourceManifest name raw with
        | Ok ()   -> ()
        | Error e -> eprintfn "Warning: could not cache manifest for '%s': %s" name e
```

Update error message (line 69): `"no eru.json found"` → `"no .eru/config.json found"`
Update success message (line 79): `"Added source '%s' to eru.json."` → `"Added source '%s' to .eru/config.json."`

---

## 7. Adapter wiring

### New `src/Eru.Adapters/ManifestAdapter.fs`

Implement:
- `readCachedManifest (sourceName: string)`: reads `sourceCacheManifestPath sourceName`,
  deserialises with `System.Text.Json` → `SourceManifest option`
- `cacheSourceManifest (sourceName: string) (rawJson: string)`: parse JSON → `SourceManifest`,
  write to `sourceCacheManifestPath sourceName` (create parent dirs)

### `src/Eru.Adapters/AdapterDeps.fs`

Wire the two new deps into `AdapterDeps.create`:

```fsharp
ReadCachedManifest  = ManifestAdapter.readCachedManifest
CacheSourceManifest = ManifestAdapter.cacheSourceManifest
```

---

## 8. String/message updates across domain

| File | Old | New |
|---|---|---|
| `Add.fs:131` | `"no eru.json found. Run 'eru init' first."` | `"no .eru/config.json found. Run 'eru init' first."` |
| `Args.fs:12` | `"Overwrite existing eru.json."` | `"Overwrite existing .eru/config.json."` |
| `LockFile.fs:11` | `"# eru.lock v1"` | unchanged (internal format marker, not user-facing path) |

---

## 9. Test updates

All test `makeDeps` helpers (in `InitTests`, `SourceTests`, `SyncTests`,
`AddTests`, `SearchTests`) need two new stub fields:

```fsharp
ReadCachedManifest  = fun _ -> Ok None
CacheSourceManifest = fun _ _ -> Ok ()
```

Specific assertion changes:

| Test file | Change |
|---|---|
| `InitTests.fs:39` | `"/tmp/cwd/eru.json"` → `"/tmp/cwd/.eru/config.json"` |
| `InitTests.fs:49` | `"/custom/dir/eru.json"` → `"/custom/dir/.eru/config.json"` |
| `InitTests.fs:32,42` | test names updated to reflect new path |
| `SourceTests.fs:72,80` | test names updated (`eru.json` → `.eru/config.json`) |
| `ConfigTests.fs` | add test: `merge populates Collections from GlobalConfig` |
| `ConfigTests.fs` | add test: `withManifests appends manifest files, user-explicit wins` |

---

## 10. Plan files to update

- `plans/collection-cache.md`: `CollectionCacheService` should iterate
  `EffectiveConfig.Collections` (now available) instead of `GlobalConfig.Collections`
- `plans/mcp-server.md`: note `.eru/config.json` rename in relevant prose

---

## 11. Glob plan interaction (`plans/glob-remote-path.md`)

The glob plan widens `FetchRemoteContent`'s return type from
`Result<string, string>` to `Result<(string * string) list, string>`.

**This plan is written assuming glob is implemented first or simultaneously.**
Every `FetchRemoteContent` call site in this plan already uses the list form
(`Ok ((_, raw) :: _)`, `Ok []`). If implementing this plan before the glob
plan, use the scalar form and update the manifest-fetch lines when the glob
plan lands.

`ManifestFileRef.Path` entries are converted directly to `CollectionFileRef.RemotePath`
by `Config.withManifests` — glob expansion happens at file-pull time (in `Add`/`Sync`),
not in the manifest merge step. A manifest entry `"path": "dotnet/**/*.md"` is
valid and will expand correctly once the glob plan is in.

---

## 12. Docs — AGENTS.md / README

Update references to `eru.json` → `.eru/config.json` and `eru.lock` →
`.eru/eru.lock`.

---

## Verification

```bash
dotnet build
dotnet test

# Local init creates .eru/config.json
dotnet run --project src/Eru -- init
ls .eru/config.json

# Source add caches manifest
dotnet run --project src/Eru -- source add https://github.com/some/knowledge-repo.git
ls ~/.cache/eru/sources/knowledge-repo/manifest.json   # if repo has .eru/manifest.json

# Sync refreshes manifests before syncing files
dotnet run --project src/Eru -- sync

# Lock file lands in .eru/
cat .eru/eru.lock
```
