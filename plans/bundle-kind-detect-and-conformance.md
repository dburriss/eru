# Bundle Kind Auto-Detect Warning and OKF Validation Scope

## Context

`eru source bundle add <source> <path>` auto-detects `okf` only when the bundle's
`index.md` frontmatter has `okf_version` (`BundleDetect.detectKind` in `Config.fs`).
A repo whose root `index.md` has other frontmatter (e.g. `type: index`) or none is
silently registered as `manifest`, and discovery then finds no files with no
explanation.

Separately, `eru okf validate .` on a normal knowledge repo reports violations for
files that are not concepts: `README.md` ("no-frontmatter") and skill files under
`.agents`, `.claude`, `.github`.

This plan fixes the silent failure, makes the validator's message actionable, and
decides what counts as a concept file.

## Changes

### 1. Warn on `manifest` auto-detect with no manifest

In `SourceBundleAdd.execute` (`src/Eru.Domain/SourceBundleAdd.fs`):

- Only when the kind was auto-detected (no `--kind`) and the result is `Manifest`:
  check for `.eru/manifest.json` at `<path>/.eru/manifest.json` via
  `deps.FetchRemoteContent`.
- If missing, append a warning to the result message. The bundle is still
  registered (warning, not error). The warning lists the options:
  1. add `okf_version` to the bundle root `index.md` frontmatter,
  2. re-run with `--kind okf`,
  3. create a manifest (`eru manifest init`).
- Also shown for `--dry-run`.
- Not shown with explicit `--kind`, with `okf` detection, or when the manifest exists.
- `SourceAdd.fs` uses the same detection for a `KNOWLEDGE/` bundle and shares the
  warning via `BundleKindWarning.noManifestWarning`. `Add.fs` has no message channel
  for its detection, so it is left unchanged.

### 2. Actionable `index-frontmatter` message

In `OkfValidate.validateIndex` (`src/Eru.Domain/OkfValidate.fs`), the root-bundle
branch message becomes:

> bundle-root index.md frontmatter should contain only `okf_version`; this is what
> makes eru detect the directory as an OKF bundle

Non-root and malformed-YAML messages are unchanged.

### 3. README.md is not a concept file (decision: exclude)

READMEs are human prose, not concepts, so they are excluded like `index.md`/`log.md`.

- Add `ReadmeFile` to `Frontmatter.FileClass`; `classifyFile` matches
  `readme.md` case-insensitively, at any depth.
- `OkfValidate.execute`: skip `ReadmeFile` (no checks, not counted as a concept).
- `BundleDiscovery.toDiscoveredFile`: treat `ReadmeFile` like `IndexFile`/`LogFile`
  (empty contribution, no type/title).
- `siteIgnorePatterns` default becomes `["index.md", "log.md", "README.md"]`
  (`Config.defaultSiteIgnorePatterns`, also written by `Init.fs`). Patterns without
  a `/` match filename only and are case-insensitive, so this covers READMEs at any
  depth. Existing configs keep their explicit value; only the default and newly
  scaffolded configs change.
- Document in `docs/reference/cli.md` (`eru okf validate`) and the OKF notes.

### 4. Skip dot-directories; add `okfIgnorePatterns` setting (decision: both)

Current state: `OkfAdapter.listMarkdownFiles` only skips `.git`; `BundleDiscovery`
(via `git ls-tree -r`) skips nothing, so `.github/**/*.md` and `inbox/*.md` on a
remote OKF source are discovered and indexed too. `siteIgnorePatterns` only applies
at site build, after discovery.

- **Always skip dot-directories** (any path segment starting with `.`: `.agents`,
  `.claude`, `.github`, `.eru`, `.git`) at any depth. Not configurable; they are
  never concepts. Implemented as one shared domain helper (e.g.
  `Frontmatter.isIgnoredPath` / `Patterns`) used by both paths.
- **New setting `okfIgnorePatterns`** (`Settings.OkfIgnorePatterns: string list option`
  in `Config.fs`), resolved like `siteIgnorePatterns`: local, then global default,
  then built-in default.
- **Built-in default** `Config.defaultOkfIgnorePatterns`:
  `["apm_modules/**"; "inbox/**"; "node_modules/**"]`.
  - `Patterns` globs are anchored at the path root, so these match at the bundle
    root only; use `**/inbox/**` style patterns for nested matches.
  - Patterns are evaluated against bundle-relative paths in validate and against
    the bundle-relative portion in discovery, so both agree.
  - This is why dot-directories are a hardcoded segment check rather than a
    `.*/**` pattern: the glob engine can't express "any depth including root".
- `Init.fs` scaffolds the setting in the generated config (as for
  `siteIgnorePatterns`).
- Plumbing: `BundleDiscovery.walkBundle` gets the patterns from resolved config.
  `OkfValidate.Command` gains `IgnorePatterns: string list`; the CLI layer fills it
  from local config when present, otherwise the defaults.
- Document in `docs/reference/config-file.md` and the validate section of
  `docs/reference/cli.md`.

## Tests

- `SourceBundleAdd`: warning present for auto-detected `manifest` without a manifest;
  absent with `--kind okf`, `--kind manifest`, `okf_version` present, or manifest
  present; present in dry-run. Shared-helper coverage for `SourceAdd` and `Add`.
- `OkfValidate`: new root `index-frontmatter` message text; README produces no
  violation and is not counted as a concept.
- `Frontmatter.classifyFile`: `README.md`, `readme.md`, `docs/README.md` classify as
  `ReadmeFile`.
- `BundleDiscovery`: README excluded from discovered contributions; dot-directories
  and `okfIgnorePatterns` matches are not discovered.
- `OkfValidate`: dot-directory files and ignored patterns are skipped; custom
  `okfIgnorePatterns` honored.
- Config: `siteIgnorePatterns` default includes `README.md`; `IndexBuilder` filters a
  nested `docs/README.md`.
- Config: `okfIgnorePatterns` default value, local/global resolution and round-trip.
- Init scaffolds `okfIgnorePatterns`.

## Docs

- `docs/reference/cli.md`: `eru source bundle add` warning behavior; `eru okf validate`
  exclusions (README, dot-directories, ignore patterns).
- `docs/reference/config-file.md`: `okfIgnorePatterns`; new `siteIgnorePatterns` default.
- OKF notes: README is not a concept file; root `index.md` frontmatter must contain
  only `okf_version` to be detected as OKF.
- `CHANGELOG.md` entry.

## Decisions

- `okfIgnorePatterns` is a config setting with sane defaults, plus a hardcoded
  dot-directory skip.
- `siteIgnorePatterns` default gains `README.md`, so READMEs are hidden from generated
  sites by default (including manifest-kind bundles that list one). Users can
  override the setting to publish them.
