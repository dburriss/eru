# Ingestor agent: produce valid OKF bundles, use `eru okf` commands

## Context
In dburriss/knowledge-base-demo, `eru okf validate` flags many violations in notes written by the ingestor agent (`agents/ingestor.agent.md`):
- Agent copies existing index.md shape, which carries frontmatter. The validator forbids frontmatter in folder indexes (`index-frontmatter`).
- Root index.md never gets `okf_version`, so eru auto-detects the repo as a manifest bundle.
- New folder README.md guidance is vague.

eru now ships `okf init`, `fix` and `validate`, and the agent and skills should use them.

Decisions (confirmed with user):
- okf_version is `"0.1"` (`src/Eru.Domain/OkfFix.fs:15`). The agent prefers `eru okf init`, so the value is not hard-coded where avoidable.
- No apm package or version field exists in this repo; record the change in `CHANGELOG.md` only. The apm package must be bumped in its own repo.
- Scope: agent, `skills/eru/SKILL.md`, `skills/organizing-documentation/SKILL.md`, and a docs page.

Facts from the validator:
- README.md is not part of the bundle: it has no `type`, so it is not a concept. The validator and fixer skip it entirely (`Frontmatter.classifyFile`, `OkfValidate.fs:74`). No frontmatter is needed, and there is no `type: readme`. The agent must NOT add frontmatter or a `type` to README.md, and must not list it in index.md.
- Root index.md frontmatter must be exactly `okf_version`. Subfolder index.md must have none.
- `inbox/**` is ignored by default.
- `init` and `fix` create missing catalog indexes only. They never add rows to an existing index.md, so row edits stay manual.

The agent is embedded in the CLI build (`Eru.Domain.fsproj:52`, `InboxProcess.fs:129-161`), so this changes the built-in `eru inbox process` instructions.

## Changes

### 1. `agents/ingestor.agent.md`
- **Step 2 (new top-level folder):** write `README.md` as plain prose. It is not part of the bundle (no `type`, so not a concept): no frontmatter, and no row in index.md. It exists only as the human description used for domain matching. Then run `eru okf init <repo-root>` to create the folder `index.md` and the root index.md if missing. Replace "same shape as existing, see any existing index.md". State: **folder index.md has NO YAML frontmatter**, just a heading and the catalog table `| Concept | Type | Tags | Stale after |`.
- **New "Bundle root" rule** (after step 2 or as its own short section):
  - If root index.md is missing, create it with only `---\nokf_version: "0.1"\n---` plus the top-level domain table (`eru okf init` does this).
  - If it exists with other frontmatter, reduce it to `okf_version` only.
  - When adding a top-level folder, add a row to the root index.md.
- **Step 5 (update index.md):** add or update the row by hand. Never add frontmatter. If an existing index has frontmatter, remove it (or run `eru okf fix <folder>`).
- **Step 6 or new Step 7 (self-check):** run `eru okf validate <domain folder>` on every folder touched, and `eru okf fix <folder> --dry-run` then `fix` for mechanical problems. Fix remaining violations (`missing-type`, `malformed-yaml`, which `fix` reports as manual) before finishing. Do this before or after archive; archive paths are under ignored `inbox/**`.
- Keep the file self-contained (the existing intro says so). Mention commands inline, noting that if `eru` is unavailable the agent follows the manual rules.

### 2. `skills/eru/SKILL.md` (~lines 196-201)
Extend the okf section from `validate` only to `init` (create missing indexes and the root marker, never overwrites), `fix` (repairs index frontmatter and missing `type`, `--dry-run`, `--default-type`), and `validate` (rules and exit codes). Point to `references/commands.md`, which already documents these.

### 3. `skills/organizing-documentation/SKILL.md`
Add a short note: after filing docs into a tree, run `eru okf init`/`validate` so indexes stay conformant. Include a one-line reminder that folder index.md has no frontmatter.

### 4. Docs
Update `docs/how-to/set-up-a-knowledge-repo.md` (and check `docs/reference/cli.md`) with the ingestor's okf self-check and the root-marker rule. Read the files first to place it correctly.

### 5. Housekeeping (per AGENTS.md)
- AGENTS.md requires a plan file in `plans/` (this file).
- AGENTS.md says to write code only when the user says "implement". Editing agent and skill markdown still needs that go-ahead.
- CHANGELOG addition of using eru okf to create fix and validate.

## Verification
- `dotnet build` (agent is an embedded resource) and run any tests covering `DefaultIngestorInstructions`.
- Run `eru okf validate` on a scratch bundle (scratchpad) built per the new instructions. Include a root index.md with `okf_version`, a folder index.md without frontmatter, and a README.md without frontmatter. Expect exit 0.
- Run `eru okf init` on an empty scratch dir, and `eru okf fix` on a bundle with frontmatter in index.md. Confirm the output matches what the agent text describes.
- Re-read the agent for consistency: no remaining instruction to copy frontmatter from indexes.
