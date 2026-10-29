# Plan: README.md is a concept when it has a `type`

## Context
`Frontmatter.classifyFile` (`src/Eru.Domain/Frontmatter.fs:285`) classifies by filename only, so any
`README.md` (any depth, any case) is `ReadmeFile`: skipped by `eru okf validate`/`fix`/`verify` and kept
in discovery with empty metadata. Desired: a README with a non-empty frontmatter `type` is a normal
concept, like any other `.md`. A README without a `type` keeps today's behaviour. Site generation is left
alone (`site.ignorePatterns` still hides README.md by default and stays configurable).

## Changes
1. `src/Eru.Domain/Frontmatter.fs`: add `classifyContent (parseYaml: Yaml.Parse) (relPath) (content) : FileClass`.
   Calls `classifyFile`; if the result is `ReadmeFile` and `parse parseYaml content |> type_` is a
   non-blank string, return `ConceptFile`, else `ReadmeFile`. Keep `classifyFile` for filename-only callers
   and update its comment.
2. Switch callers that have the content to `classifyContent`:
   - `OkfValidate.fs:159`: concept path runs `validateConcept`, shape warnings and link checks.
   - `OkfFix.fs:133` (`repairFile`) and `OkfFix.fs:213` (concepts list feeding catalog `index.md` rows):
     the `current` map already holds contents, so the filter can read them.
   - `BundleDiscovery.fs:21` and `:77`: typed README gets full metadata and is published.
     (Untyped README is still kept with empty metadata, unchanged.)
   - `OkfVerify.fs:89`: it classifies before reading the file; move the check after `ReadLocalFile`
     and use `classifyContent` so `eru okf verify README.md` works when typed.
3. Docs: `docs/reference/cli.md:984` (and 1063 `verify` failure text), `docs/reference/config-file.md:195`
   ("README.md ... always skipped") reworded to "a README without `type` is skipped"; `CHANGELOG.md`
   `[Unreleased]` -> `### Changed`. Check `plans/`-independent docs in `docs/explanation` with a grep for README.

## Gotchas
- `eru okf fix` must not start adding a `type` to untyped READMEs; untyped stays `ReadmeFile` -> no repair.
- Frontmatter that fails to parse counts as untyped (README stays non-concept, no violation).
- Catalog `index.md` rows (`OkfFix.conceptRow`) will now list typed READMEs; the README in a folder
  is linked by its own filename, fine.

## Tests
- `FrontmatterTests.fs` (~216): `classifyContent` returns `ConceptFile` for typed README (any case, nested),
  `ReadmeFile` for untyped / blank type / malformed YAML; non-README results unchanged.
- `OkfValidateTests.fs`: typed README counted as a concept; typed README with bad shape yields warnings;
  untyped README still skipped.
- `OkfFixTests.fs`: untyped README untouched; typed README listed in the folder catalog.
- `BundleDiscoveryTests.fs:148`: keep existing untyped case; add typed README -> `Type = Some ...`, tags/description extracted.
- `OkfVerifyTests.fs`: verify succeeds on a typed README, errors on an untyped one.

## Verification
`dotnet build`, `dotnet test tests/Eru.Tests`, then manually: scratch bundle with `README.md` containing
`type: guide`; `eru okf validate <dir>` counts it as a concept, and an untyped README is still skipped.

## Follow-up: per-glob types for `eru okf fix`
Independent of the changes above; do after them. Lets `fix` write a chosen `type` by path glob, so
untyped READMEs (and other untyped files) can be adopted without a single global `--default-type`.

### What exists today
- `eru okf fix` already adds `type` to untyped concepts (`OkfFix.repairFile`, `OkfFix.fs:154-165`) using one
  value, `cmd.DefaultType` (`--default-type`, default `reference`, `OkfFixCli.fs:19`).
- READMEs hit `ReadmeFile -> None` first (`OkfFix.fs:134`), so fix never touches them.
- "None" already exists as `okf.ignorePatterns` (`Config.fs:495`, default `apm_modules/**`, `inbox/**`,
  `node_modules/**`): matching files are skipped by validate, fix and discovery. It does not need a new setting.

### Design
- New `okf` config key, an ordered list (first match wins), e.g. `okf.fixTypes`:
  `[{ "pattern": "README.md", "type": "guide" }, { "pattern": "docs/internal/**", "type": null }]`.
  A list rather than a map so order is explicit; reuses `Patterns.matchesGlob` (same glob rules as
  `ignorePatterns`).
- New scalar `okf.defaultType` (string) replaces the hard-coded `"reference"` in `OkfFixCli.fs:19`. Today the
  default is CLI-only (`--default-type`); there is no config key.
- Type resolution in `fix` for a file with no/blank `type`, highest precedence first:
  1. `--default-type` flag (explicit, overrides a glob's type for this run);
  2. first matching `okf.fixTypes` glob;
  3. `okf.defaultType` (local overrides global, as for other `okf` keys);
  4. built-in `reference`.
  Exceptions: a matching glob with `type: null` still means "leave the file alone" (the flag supplies a type,
  it does not cancel an exclusion). A README is only typed when a glob matches it, so with the flag set an
  unmatched README stays untouched; the flag, `okf.defaultType` and `reference` only apply to concepts.
- Existing `type` is never overwritten.
- A glob match with a type pulls a README into the concept repair path: in `repairFile`, check the glob
  before the `ReadmeFile` branch, then run the normal `ConceptFile` handling (so `verified` repair etc. also
  apply) with that type. This is the only way a README gets a type from `fix`.
- `null` is only useful as an exception under a broader glob (e.g. `docs/**: reference` then
  `docs/internal/**: null`). To exclude a path from everything, keep using `okf.ignorePatterns`.

### Changes
1. `Config.fs`: add `FixTypes: (string * string option) list option` and `DefaultType: string option` to
   `OkfSection` (:124), `OkfSection.empty` (:207), the resolved config record (:197) and merge/serialise code
   (:643, :790, :823). Add `Config.defaultOkfType = "reference"`, `Config.resolveOkfFixTypes` (default `[]`)
   and `Config.resolveOkfDefaultType` next to `resolveOkfIgnorePatterns` (:501).
2. `ConfigCmd.fs:107`: register `okf.defaultType` (default `reference`) as a normal scalar key, and
   `okf.fixTypes` as a list-valued key if `eru config set` supports non-string lists; otherwise document
   `fixTypes` as file-only.
3. `OkfFix.fs`: add `FixTypes` to `Command`; add a private `typeFor cmd rel : string option option`
   (outer None = no match). Use it in `repairFile` (README/concept branches replace `cmd.DefaultType`).
   Concept-catalog filter (:213) is already switched to `classifyContent` by the main plan, and reads
   `current`, so READMEs typed by `fix` appear in the generated `index.md`.
4. `OkfFixCli.fs`: `Cmd.DefaultType` (:9, :19) becomes `string option` (no `Option.defaultValue "reference"`);
   in `runMode` (:60) read `fixTypes` and `defaultType` alongside `resolveOkfIgnorePatterns`. Pass into
   `OkfFix.Command`: `FixTypes`, `DefaultType: string` (config > built-in) and `TypeOverride: string option`
   (the flag). `OkfFix.typeFor` applies flag > glob > `DefaultType`.
5. `Args.fs:694`: help text says "default: reference"; reword to "default: `okf.defaultType`, else reference".
6. Docs: `docs/reference/config-file.md` (both new keys), `docs/reference/cli.md:1039-1046` (`fix` precedence:
   `--default-type` > glob > `okf.defaultType` > `reference`), `CHANGELOG.md` `### Added`.

### Decisions
- An explicit `--default-type` flag overrides a matching glob's type (flag > glob > `okf.defaultType` > `reference`).
  `null` globs still exclude the file.
- `eru okf init` (`CreateOnly`) does not use `okf.fixTypes` or `okf.defaultType`; it only scaffolds indexes.

### Tests
- `OkfFixTests.fs`: untyped README + matching glob gets `type: <glob type>` and appears in the folder catalog;
  untyped README with no match is untouched; existing type is kept; first match wins; `null` skips;
  non-README untyped file uses the glob type, falls back to `--default-type`/config/`reference` with no match.
- Resolution: flag beats glob, glob beats `okf.defaultType`, `okf.defaultType` beats `reference`; flag does not
  override a `null` glob and does not type an unmatched README; `okf init` ignores both settings.
- Config tests: `okf.fixTypes` and `okf.defaultType` parse/merge (local overrides global), defaults
  (empty list / `reference`), round-trip.
