# Plan: `eru source add --scan --exclude <glob>`

## Context
`eru source add --scan` registers every directory whose `index.md` has `okf_version` as an okf bundle
(`scanBundles`, `src/Eru.Domain/SourceAdd.fs:49`). A root bundle covers (drops) every nested one, and there
is no way to skip a detected bundle. Users who want to ignore a root bundle (and get its nested bundles
instead) must skip `--scan` and register bundles by hand. Add a repeatable `--exclude <glob>` flag.

## Decisions (confirmed with user)
- Excluding a bundle drops **only that directory**, before nesting is resolved. Excluding the root therefore
  lets nested bundles (`a/`, `b/`) be registered on their own.
- `--exclude` is **repeatable** and takes **glob patterns**, matched against the bundle directory path.

## Changes
1. `src/Eru.Cli/Args.fs` (`SourceAddArgs`, ~line 60): add `| Exclude of pattern: string` (repeatable, so no
   `[<Unique>]`), usage text: "With --scan, do not register OKF bundles whose directory matches this glob
   (repeatable). Use '.' for the repo root."
2. `src/Eru.Domain/SourceAdd.fs`:
   - Add `Exclude : string list` to `Command` (line 5).
   - `scanBundles` gains an `exclude: string list` parameter. After `okfDirs` is computed (line 58-69) and
     before `dropNested`, filter out excluded dirs.
   - New private helper `isExcluded (patterns) (dir)`: `"."` or `"/"` (same convention as
     `SourceBundleAdd.fs:13`) matches dir `""`; otherwise reuse `Patterns.matchesAny patterns dir`
     (`src/Eru.Domain/Patterns.fs:34`). No new glob code.
   - Update the `scanBundles` comment and the call at line 86.
   - Fallback: when exclusion empties the list, `detectConventionBundle` still runs. Apply the same exclude
     filter to its result so `--exclude knowledge` is honoured, otherwise the fallback would re-add it.
   - Validation next to line 159: `--exclude` without `--scan` -> `Error "--exclude only applies together with --scan."`
3. `src/Eru.Cli/SourceAddCli.fs` (line 21): `Exclude = addArgs.GetResults SourceAddArgs.Exclude`.
4. `src/Eru.Tui/Browse/BrowseWindow.fs:179`: add `Exclude = []`.
5. Docs/changelog:
   - `docs/reference/cli.md` (~line 140-150): add `--exclude` to the usage line and flag table, plus an example
     `eru source add <url> --scan --exclude .`.
   - `skills/eru/references/commands.md` if it lists `--nested` (grep showed no hits in `skills/`, so likely
     no change; recheck while implementing).
   - `CHANGELOG.md` `[Unreleased]` -> `### Added`.

## Gotchas
- `Patterns.matchesGlob` matches a pattern with no `/` against the last path segment only, and a pattern with
  `/` against the full path. So `legacy` excludes any bundle dir named `legacy`, and `docs/legacy` excludes
  only that path. `docs/**` matches `docs/x` but **not** `docs` itself; document this and suggest `docs` plus
  `docs/**` if both are wanted.
- Matching is case-insensitive (regex uses `IgnoreCase`).
- `--exclude` applies to the scan only; it is not persisted in config, so re-running `eru source add --scan`
  needs the flag again.

## Tests (`tests/Eru.Tests/SourceTests.fs`, reuse `okfDeps` / `simpleCmd`)
- Add `Exclude = []` to `simpleCmd` (line 59).
- `--scan --exclude .` with root + `a` + `b` index files registers `a` and `b`, not root.
- `--scan --exclude a` keeps root only (a is covered anyway) and, with `--nested`, keeps root + a/b but not `a`.
- Glob: `--exclude 'docs/*'` drops `docs/x` but keeps `docs`.
- Excluding every detected bundle falls back to the `knowledge/` convention, and `--exclude knowledge`
  suppresses that too.
- `--exclude` without `--scan` is rejected and nothing is written.

## Verification
- `dotnet build` and `dotnet test tests/Eru.Tests` (new tests above).
- Manual, against a local repo with a root `index.md` (okf_version) and `a/index.md`, `b/index.md`:
  `eru source add <path> --scan --exclude . --dryrun` lists `a` and `b` only; without `--exclude` it lists
  root only.
