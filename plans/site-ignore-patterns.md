---
status: done
---
# Plan: Site Ignore Patterns

## Context

`eru site generate` builds a page for every markdown file across all configured/synced sources (`src/Eru.Site/IndexBuilder.fs:28-120`). Structural files like `index.md` and `log.md` aren't real content — they shouldn't appear as documents in the generated site at all (no listing, no search entry, no per-file page). This adds a configurable ignore-glob list, defaulting to `["index.md"; "log.md"]`, following the exact same config pattern already used for `BlockPatterns`/`AllowPatterns` (see `plans/block-patterns.md`).

Matched files are excluded entirely from the site model (not just from page generation) — no listing, no tag/type aggregation, no search entry, no per-file page.

## Approach

Reuse the existing gitignore-style glob matcher in `src/Eru.Domain/Patterns.fs` (`Patterns.matchesGlob`) — no new glob library needed. A pattern with no `/` (like `"index.md"`) already matches by filename only, which is exactly what we want.

### 1. Pattern matching (`src/Eru.Domain/Patterns.fs`)

Promote the existing `private matchesAny` to public — it's already the right shape (`string list -> string -> bool`) and becomes shared between the block/allow logic and the new ignore-pattern check. No behavior change to `isPathBlocked`/`isBlocked`.

### 2. Config types (`src/Eru.Domain/Config.fs`)

Add `Config.defaultSiteIgnorePatterns = ["index.md"; "log.md"]` next to `defaultBlockPatterns` (line ~115).

Add to `GlobalDefaults`:
- `SiteIgnorePatterns: string list option` — JSON `defaults.siteIgnorePatterns`

Add to `LocalSettings`:
- `SiteIgnorePatterns: string list option` — JSON `settings.siteIgnorePatterns`

Add to `EffectiveConfig`:
- `SiteIgnorePatterns: string list`

**Merge semantics** (same shape as `BlockPatterns`, `Config.merge` lines 221-228): local `Some ps` → use `ps`; else global value; else `Config.defaultSiteIgnorePatterns`.

### 3. Exclude matched files (`src/Eru.Site/IndexBuilder.fs`)

In `buildModel` (lines 42-76), filter out files whose `remotePath` matches an ignore pattern, alongside the existing `isGlob` filter (line 45):

```fsharp
let docs =
    index
    |> Map.toList
    |> List.filter (fun (remotePath, _) -> not (isGlob remotePath))
    |> List.filter (fun (remotePath, _) -> not (Patterns.matchesAny cfg.SiteIgnorePatterns remotePath))
    |> List.map (...)
```

Since `model.Documents` (and tags/types/extensions derived from it) feeds everything downstream — `data/*.json`, search data, and the per-file page loop in `SiteGenerator.generate` — this one filter excludes ignored files from the whole site.

### 4. Init template (`src/Eru.Domain/Init.fs`)

Unlike `blockPatterns`/`allowPatterns` (scaffolded as `null`, relying on the merge fallback), `siteIgnorePatterns` is scaffolded with its default values populated explicitly in both templates, so a newly created config visibly ignores `index.md`/`log.md` from the start:

**Local scaffold**: add `"siteIgnorePatterns": ["index.md", "log.md"]` to the `settings` block (line ~15), alongside the existing `null` fields.

**Global `emptyGlobal`**: add `SiteIgnorePatterns = Some Config.defaultSiteIgnorePatterns` to `Defaults` (line ~29), same as `BlockPatterns`.

### 5. CLI / plumbing

No changes needed. `SiteGenerateCli.run` (`src/Eru.Cli/SiteGenerateCli.fs:21-25`) already merges `EffectiveConfig` and passes it into `SiteGenerator.generate`, which passes `cfg` into `IndexBuilder.buildModel`. `cfg.SiteIgnorePatterns` flows through automatically.

### 6. Tests

- `tests/Eru.Tests/ConfigTests.fs`: add merge cases for `SiteIgnorePatterns` mirroring the existing `BlockPatterns` cases — default applies when unset, local overrides global, global applies when local unset.
- `tests/Eru.Tests/InitTests.fs`: update assertion for `emptyGlobal.Defaults` to include `SiteIgnorePatterns`.
- Site generator / index builder tests (e.g. `tests/Eru.Tests/SiteGeneratorGraphTests.fs` or wherever `IndexBuilder.buildModel` is exercised): add a case with an `index.md` entry in the source index and assert it's absent from `model.Documents` after `buildModel`, using both the default patterns and a custom override.

## Files modified

| File | Change |
|---|---|
| `src/Eru.Domain/Patterns.fs` | `matchesAny` made public |
| `src/Eru.Domain/Config.fs` | `defaultSiteIgnorePatterns`; new field on `GlobalDefaults`, `LocalSettings`, `EffectiveConfig`; `Config.merge` |
| `src/Eru.Domain/Init.fs` | Local scaffold adds `siteIgnorePatterns` key; global `emptyGlobal` writes explicit default |
| `src/Eru.Site/IndexBuilder.fs` | `buildModel` filters out files matching `cfg.SiteIgnorePatterns` |
| `tests/Eru.Tests/ConfigTests.fs` | New merge tests for `SiteIgnorePatterns` |
| `tests/Eru.Tests/InitTests.fs` | Updated assertion for `emptyGlobal.Defaults` |
| `tests/Eru.Tests/SiteGeneratorGraphTests.fs` (or `IndexBuilder` tests) | New case: ignored files excluded from `model.Documents` |

## Verification

1. `dotnet build` — clean compile
2. `dotnet test --filter "FullyQualifiedName~Config"` and `--filter "FullyQualifiedName~SiteGenerator"`
3. Manual smoke test:
   - Sync a source containing `index.md`/`log.md`, run `eru site generate` → no `files/<source>/index.html`/`log.html` written, and neither appears in `data/documents.json` or the tag/type listings.
   - Set local `"siteIgnorePatterns": []` → `index.md`/`log.md` now get pages generated (override disables the default).
   - Set local `"siteIgnorePatterns": ["draft-*.md"]` → files matching that pattern are excluded instead of the defaults.
