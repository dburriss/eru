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
