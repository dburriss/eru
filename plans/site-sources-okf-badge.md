# Sources page: drop "no manifest", add "okf" badge

## Context
The default site's sources page shows a `manifest` badge when a source has a cached `manifest.json`, otherwise `no manifest`. OKF sources have no manifest, so they always read "no manifest", which is noise. Show nothing for plain sources, and an `okf` badge for OKF sources.

## Changes
1. `src/Eru.Site/SiteModel.fs:29` – add `HasOkf : bool` to `SiteSource` (keep `HasManifest`; `data/sources.json` still uses it).
2. `src/Eru.Site/IndexBuilder.fs:97` – set `HasOkf = src.Bundles |> List.exists (fun b -> b.Kind = BundleKind.Okf)`. Folder sources are OKF-only, so check they get an Okf bundle; if they don't, also treat that case as OKF.
3. Badge rendering, same logic in two places, so extract one shared helper (e.g. `HtmlTemplates.sourceBadges : SiteSource -> string`) and use it in both:
   - `src/Eru.Site/HtmlTemplates.fs:102-104` (card list)
   - `src/Eru.Site/SiteGenerator.fs:1820-1822` (detail page title)
   Output: `manifest` badge if `HasManifest`, `okf` badge if `HasOkf`, nothing otherwise (a source can show both).
4. `src/Eru.Site/SiteGenerator.fs:~287` – replace `.badge-no-manifest` CSS with `.badge-okf` (plus a dark-theme variant next to the manifest one).
5. `plans/site-generate.md:178-181` – update the `[manifest]` / `[no manifest]` description.

6. Write this plan to `plans/site-sources-okf-badge.md` in the repo (first implementation step, matching the existing `plans/*.md` convention).

## Verification
- Add a small test in the Eru.Site tests: `sourcesListHtml` contains `badge-okf` for an OKF source, `badge-manifest` for a manifest source, and neither `no manifest` nor any badge for a plain source.
- `dotnet build` and `dotnet test`, then `eru site generate` and check the sources page and a source detail page.
- Work on the current branch (`main`); no new branches.
