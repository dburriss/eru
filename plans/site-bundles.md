---
status: done
---

# Plan: Bundles in the generated site

## Context

A source can register several bundles (`Bundle = { Path; Kind }`, `Config.fs`). The generated site
ignores them: its tabs are Browse, Sources, Tags and Types, and `SiteModel` has no bundle concept. A repo
that holds several OKF bundles (for example `knowledge/software`, `knowledge/ops`) shows up as one flat
source.

Nested bundles cannot be registered through `--scan` today. `SourceAdd.scanBundles` finds every
`index.md` that carries `okf_version`, then `dropNested` discards any bundle covered by an earlier one.
A root `index.md` with `okf_version` therefore swallows every nested bundle.

Goals:

1. A **Bundles** tab in the site nav, after Types, with an index page and one page per bundle.
2. A way for `eru source add --scan` to keep nested bundles instead of dropping them.

A root `index.md` stays a bundle marker. It is still hidden from the site by `siteIgnorePatterns`; this
plan does not change that.

## Decisions

- The Bundles tab always shows, even when a source has a single bundle.
- A file is listed under its **most specific** bundle only (longest `Path` among all covering bundles,
  any kind). It is never counted in the parent bundle.
- No `name` override on `Bundle` for now. The display name is derived from the path. A name override is
  a later change; it would alter the config schema.
- No config schema change. `Bundles` already stores everything needed.
- Empty bundles are shown by default. The `siteHideEmptyBundles` setting (global `defaults` or local
  `settings`, default `false`) hides bundles that list no files; "empty" is counted after
  `siteIgnorePatterns`, so a bundle whose files are all ignored is hidden too.
- Bundle pages are gated by the existing `TagPages` feature flag, as Types pages are, so no new
  feature flag.

## Design

### 1. Scan: keep nested bundles (`SourceAdd.fs`, `Args.fs`, `SourceAddCli.fs`)

- New flag `--nested` on `eru source add`, only meaningful with `--scan`. Without `--scan` it is an
  error, or ignored with a warning (match how other flag combinations are handled in
  `SourceAddCli.fs`).
- `SourceAdd.Command` gains `Nested: bool`. `scanBundles` skips `dropNested` when it is set and
  returns every `okf_version` directory, root included, sorted by path.
- Default behaviour (no `--nested`) is unchanged: a root bundle covers its descendants.
- `eru source bundle add` already registers one explicit path. Check that it accepts a path under an
  existing bundle. Relax any such rejection so nested bundles can also be added one at a time.
- `detectionNote` in `SourceAdd.execute` already prints one line per bundle, so the output lists the
  nested ones without a change.

### 2. Sync with overlapping OKF bundles (`Sync.fs`, `SourceBundleAdd.fs`)

Step 1d walks each OKF bundle in turn. With a root bundle and a nested one, the nested files are
fetched and merged twice with identical results. This is wasteful, not wrong:

- The same `remotePath` is rewritten with the same frontmatter contribution.
- `IndexBlend.blend` only consults Manifest bundles, so overlapping OKF bundles do not change blended
  tags.

Cheap fix: while looping `okfBundles`, skip bundles that are nested under an earlier OKF bundle that
was walked successfully. A root bundle's walk already lists the nested paths, because `ListRemoteFiles`
returns every `.md` under it. Bundle membership for display is computed later (section 3), so nothing
is lost.

Confirm in the test for this that a nested bundle's files still end up in the index when only the
root was walked.

### 3. Model (`Eru.Domain/Config.fs`, `Eru.Site/SiteModel.fs`, `IndexBuilder.fs`)

- New pure helper in the `Bundle` module of `Config.fs`:
  `mostSpecificBundle : Bundle list -> string -> Bundle option` picks the longest `Path` among
  `coveringBundles`, any kind. Do not reuse `owningBundleForDisplay`: it prefers a Manifest bundle even
  when an Okf bundle is more specific.
- `SiteDocument` gains `Bundle : string option`, the bundle's display name (section 4), `None` when no
  registered bundle covers the file.
- New `SiteBundle = { Name; Source; Path; Kind; FileCount; Files }` and `SiteModel.Bundles`.
- `IndexBuilder.build` computes each document's bundle from `src.Bundles` and `remotePath`, then groups
  documents into `SiteBundle` values. Bundles with no files are still listed, with a count of 0, so a
  newly registered bundle is visible before its first sync.
- Files with no covering bundle appear in no bundle page. They remain in Browse, Sources, Tags and
  Types as today. Revisit if this turns out to hide files people expect to find.
- `SiteDocument` is built at one site in `IndexBuilder.fs`; other constructors (tests) need the new
  field.

### 4. Names and URLs

- Display name: the source name for a root bundle (`Path = ""`), otherwise
  `<source>/<path>`, for example `kb` and `kb/knowledge/software`. A nested bundle is namespaced by its
  parents because the path carries them.
- Page path: `bundles/<source>/<path>/index.html`, with the root bundle at `bundles/<source>/index.html`.
  Segments are URL-escaped individually, as `Uri.EscapeDataString` already does for tag and type slugs.
  The page depth is `2 + segment count`, which `HtmlTemplates.layout` already takes as an argument.
- `bundles/index.html` lists every bundle: name, kind (`okf` or `manifest`), source and file count,
  sorted by name.
- A source named so that `<source>/<path>` collides with another bundle's path cannot happen: `<source>`
  is always the first segment.

### 5. Templates and generator (`HtmlTemplates.fs`, `SiteGenerator.fs`)

- Nav in `layout`: add `<a href="{p}bundles/index.html">Bundles</a>` after Types.
- `bundlesPage` and `bundleFilesPage`, copied from `typesPage` and `typeFilesPage`. The per-bundle page
  has breadcrumbs `Bundles › <name>`, and a file list in the same card format as the other pages.
- Browse sidebar: add a **Bundles** section after Types, listing links with counts, like the Types
  section.
- `SiteGenerator.fs`: next to the `types/` block, write `bundles/index.html` and one page per bundle,
  gated by `Features.TagPages`.
- `data/documents.json` (`toDocDto`) gains a `bundle` field, so the search JSON can carry it. The
  `app.js` search keeps matching the same fields. Adding a bundle filter facet to the sidebar is a
  follow-up, not part of this plan.
- Serve mode (`eru site serve`) regenerates through the same code, so it needs no separate change.
  Verify that `plans/site-serve.md` routes do not hard-code the nav.

### 6. Docs

- `docs/reference/site-generation.md`: add `bundles/` to the generated layout, describe the page, and
  note the "most specific bundle" rule.
- `docs/reference/cli.md`: document `--nested` under `eru source add`.
- `docs/reference/config-file.md` / `docs/explanation/concepts.md`: only if they describe `--scan` as
  always dropping nested bundles. Update the wording.
- `CHANGELOG.md` under `## [Unreleased]`: `Added` entries for the Bundles tab and `--nested`.

## Tests

- `IndexBuilderTests.fs`: bundle assignment (root only; root plus nested, file goes to nested only;
  Manifest nested inside Okf root; Okf nested inside Manifest root, where `mostSpecificBundle` and
  `owningBundleForDisplay` differ; file outside every bundle gets `None`; empty bundle listed with 0).
- `SourceBundleTests.fs` or a new scan test: `--scan` with a root and nested `okf_version` index keeps
  one bundle by default and all of them with `--nested`; `--nested` without `--scan` is rejected or
  warned.
- Sync test: root plus nested OKF bundles walks the nested one once at most, and nested files are
  indexed.
- Site generator test (pattern: `SiteGeneratorGraphTests.fs`): nav contains the Bundles link,
  `bundles/index.html` and nested bundle pages exist at the expected paths with correct relative
  asset prefixes, and `documents.json` has the `bundle` field.
- Manual: register `knowledge-base-demo` with `eru source add <url> --scan --nested`, run
  `eru site generate --open`, check the tab, a nested bundle page and that no file appears under two
  bundles.

## Open questions

1. Whether `--nested` should become the default for `--scan` in a later release. This plan keeps the
   current default to avoid a breaking change.
2. Whether files covered by no bundle need an "unbundled" entry. Left out for now.
3. A bundle filter facet in the Browse sidebar and a name override on `Bundle`, both deferred.
