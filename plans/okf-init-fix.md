# Plan: `eru okf init` and `eru okf fix`

## Context
`eru okf validate` only reports problems. Users must hand-write `index.md` files and repair frontmatter themselves. We want two commands that do the OKF housekeeping:
- `eru okf init <path>` scaffolds missing OKF structure (root `index.md` with `okf_version`, per-folder catalog `index.md`).
- `eru okf fix <path>` repairs existing files so that `eru okf validate` passes.

Decisions made with the user: two commands; fixer covers index frontmatter, concept frontmatter, log date headings, and generation of missing `index.md`; `log.md` is never created (only existing logs are repaired); writes happen by default, with `--dry-run` to preview.

## Design

### Domain (`src/Eru.Domain/`)
- New `OkfFix.fs` (pure logic over `Deps`, same style as `OkfValidate.fs`):
  - Reuse `Frontmatter.tryParse` / `Frontmatter.body` / `Frontmatter.classifyFile`, `Patterns.isOkfIgnored`, `deps.ListMarkdownFiles`, `deps.ReadLocalFile`, `deps.WriteLocalFile`, `deps.GetUtcNow`.
  - Result type: `Change { Path; Action (Created | Modified); Rule; Description }` list plus counts; `--dry-run` skips `WriteLocalFile`.
  - Fixes, keyed to the existing validate rule ids so validate and fix stay in sync:
    - `index-frontmatter`: root → frontmatter rewritten to exactly `okf_version: "<current>"`; non-root → block stripped (`Frontmatter.body`). Malformed YAML on an index is treated the same way (replace the block).
    - `no-frontmatter` → prepend `---\ntype: <default>\n---`.
    - `missing-type` → insert `type:` into the existing block as a text edit (keep other keys/order untouched, since there is no YAML serializer).
    - `malformed-yaml` on a concept → report as "needs manual fix", do not touch.
    - `log-date-heading` → rewrite `## <heading>` to ISO when the heading parses as a date (`DateTime.TryParse`); leave unparseable ones and report them.
    - Missing `index.md` → generate (see below).
- New `OkfInit.fs`: creates the root `index.md` (with `okf_version`) and per-folder catalog `index.md` for folders that lack one. Never overwrites an existing file. `fix` calls the same generator, so `init` is the "create only" subset (shared function, no duplicated logic).
- Catalog `index.md` shape: a markdown table of the folder's concepts (file link, title, type, tags, stale_after) built from each concept's frontmatter (`Frontmatter.title`, `type_`, `tags`, `stale_after`). Match the format used in `agents/ingestor.agent.md` (step 5 says rows carry `tags`/`stale_after`); **first step of implementation: confirm the exact column layout against a real bundle's `index.md`** since none is in this repo.
- Concept `type` default: unresolved heuristic. Proposal: `--default-type <t>` flag (default `reference`), overridable per run; no folder-name inference in v1.
- Existing-file safety: only write when content actually changes; never touch `README.md` or ignored paths (`okfIgnorePatterns` via `Config.resolveOkfIgnorePatterns`, as `OkfValidateCli.run` does).

### CLI (`src/Eru.Cli/`)
- `Args.fs`: add `OkfInitArgs` (`Path`, `--dry-run`) and `OkfFixArgs` (`Path`, `--dry-run`, `--default-type`, `--output/-o`) beside `OkfValidateArgs` (~line 567); add `Init` and `Fix` cases to `OkfArgs` (~577) with usage strings.
- New `OkfInitCli.fs` and `OkfFixCli.fs` modelled on `OkfValidateCli.fs` (active pattern `(|OkfInitCmd|_|)`, `run deps cmd`, reuse `OutputFormat.fs` for text/json/table). Exit 0 on success; `fix` exits 1 if manual-fix items remain.
- `Eru.Cli.fsproj`: add both files before `Program.fs` (~line 98-101).
- `Program.fs`: add `open`s and two dispatch lines near line 95.

### Docs and housekeeping (per AGENTS.md)
- `CHANGELOG.md`, `docs/reference/cli.md` (new sections after `okf validate`, ~line 835), `README.md:103`, `skills/eru/references/commands.md`.
- Per AGENTS.md no code is written until the user says "implement"; this plan should also be copied to `plans/okf-init-fix.md` at that point (existing convention).

## Tests (`tests/Eru.Tests/`)
- New `OkfFixTests.fs` and `OkfInitTests.fs`, added to `Eru.Tests.fsproj` (~lines 21-49). Reuse the in-memory fake `Deps` pattern from `OkfValidateTests.fs:7-45`, but capture `WriteLocalFile` calls in a `ResizeArray`.
- Cases: each rule above fixed; unknown keys/order preserved on `missing-type`; no write when already valid; `--dry-run` writes nothing; ignored paths and README untouched; idempotent (second run makes no changes); **round trip: after `fix`, `OkfValidate.execute` returns zero violations** (except malformed-yaml concepts); init never overwrites existing `index.md`.

## Verification
- `dotnet build` and `dotnet test` from the repo root.
- Manual: copy a messy sample bundle to the scratchpad, run `eru okf fix <dir> --dry-run`, then without it, then `eru okf validate <dir>` (expect clean); run `eru okf init` on an empty folder with a few concepts and inspect the generated `index.md`.

## Open items to resolve at implement time
1. Exact catalog table columns (read a real bundle's `index.md`).
2. Whether `okf_version` should be a constant in the domain (check for an existing one; `BundleKindWarning.fs` mentions it).
