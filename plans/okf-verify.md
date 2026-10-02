# OKF `verified` handling: `eru okf verify`, validate reporting, fix stripping

## Context

OKF v0.2 (https://cloud.google.com/blog/products/data-analytics/okf-v0-2-adds-trust-signals)
defines `verified` as a list of `{ by, at }` entries: independent confirmations
by a human (`human:<email>`) or a machine actor. Absent key = unverified; any
`human:` entry = human-reviewed; machine-only = machine-confirmed.

Observed: notes end up with a placeholder `verified` (e.g. `unknown`). Nothing in
`src/` writes `verified` (`OkfFix.fs`, `Init.fs` and `OkfAdapter.fs` never mention
it), so the placeholder comes from agents/LLMs following the skills loosely, or
from hand edits. The fix is to make `verified` something only a command sets,
and to make bad values visible and removable.

Decisions (user):
- `eru okf verify` takes a **single file**.
- `eru okf validate` reports a badly shaped `verified`.
- `eru okf fix` **strips** a badly shaped `verified`.

## 1. `eru okf verify <file>`

Appends `{ by, at }` to the file's `verified` list.

Args (`OkfVerifyArgs` in `Args.fs`, new `Verify` case on `OkfArgs`):
- `<file>` (MainCommand, ExactlyOnce): the concept `.md` file.
- `--by <actor>` (Unique): `human:<id>` or a machine actor name. Default
  `human:<git config user.email>`; error if no email can be resolved.
- `--at <ISO 8601 with offset>` (Unique, optional): default now (UTC, `Z`).
- `--dry-run` (Unique): report only.
- `-o/--output`: table (default), text, json, like the other okf commands.

Behaviour:
- Fails if the file has no frontmatter or no `type` (not a concept file); points
  at `eru okf fix`.
- Edits frontmatter as a **text edit** (same approach as `setType` in
  `OkfFix.fs`) so other keys, order and comments are untouched.
- Existing `verified`:
  - none: add `verified:` block with one entry.
  - well-formed list: append.
  - bare `{by, at}` mapping: normalize to a list, then append.
  - malformed (scalar like `unknown`/`false`, entries missing `by`/`at`):
    replace with a fresh list containing only the new entry, and say so in the
    output (the old value was invalid, so nothing valid is lost).
- Same `by` already present: append a new entry anyway (history of
  confirmations is the point); no dedupe.
- Never touches `generated`.

New `OkfVerify.fs` in `Eru.Domain` (pure edit function + `execute deps cmd`),
`OkfVerifyCli.fs` in `Eru.Cli`, registered in both `.fsproj` files and
`Program.fs`. Git email lookup goes through the existing SimpleExec/deps
abstraction used elsewhere, so it can be stubbed in tests.

## 2. `eru okf validate` reports bad `verified`

`Frontmatter.shapeWarnings` already emits a `verified-shape` warning for scalars
and for entries missing `by`/`at`/offset. Work:
- Confirm placeholder scalars (`unknown`, `false`, `true`, empty) are covered
  and make the message actionable: "`verified` must be a list of `{by, at}`
  entries; run `eru okf fix` to strip it or `eru okf verify` to set it".
- Keep it a **warning** (non-fatal), consistent with §11 and existing
  `⚠` behaviour. Open point: promote to a violation (exit 1) if you want it to
  gate CI. Default is warning.
- Also flag `by` that is neither `human:<id>` nor a plausible machine actor name
  (e.g. literal `unknown`) as `verified-shape`.

## 3. `eru okf fix` strips bad `verified`

In `OkfFix.fs`, add a repair step (rule `verified-shape`, matching validate):
- Valid entries are kept. Invalid entries are dropped. If none remain, remove the
  `verified` key entirely (absent = unverified, per spec).
- Scalar / non-list non-mapping values are removed.
- Text edit only; removes the key's line(s) including indented continuation
  lines. Respects `--dry-run`; reported as a `Change`.
- `okf init` (`CreateOnly` mode) stays untouched: it never writes `verified`.

## 4. Docs and agent guidance

- `docs/reference/cli.md` and `skills/eru/references/commands.md`: document
  `okf verify`, the validate warning wording, and fix stripping.
- `CHANGELOG.md`: entry under unreleased.
- `agents/ingestor.agent.md`, `skills/eru/SKILL.md`,
  `skills/organizing-documentation/SKILL.md`,
  `docs/how-to/generate-docs-from-inbox-with-gh-aw.md`: state that agents must
  never write `verified`; only `eru okf verify` does.

## 5. Tests (xUnit, `tests/Eru.Tests`)

- `OkfVerifyTests.fs`: no `verified` -> added; existing list -> appended; bare
  mapping -> normalized + appended; scalar `unknown` -> replaced; other keys and
  body preserved byte-for-byte; no frontmatter/`type` -> error; `--dry-run`
  writes nothing; default `by` from stubbed git email; missing email -> error.
- `OkfFixTests.fs`: `verified: unknown` stripped; list with one bad entry keeps
  the good ones; all bad -> key removed; valid `verified` untouched; dry-run;
  `init` mode never adds/removes `verified`.
- `OkfValidateTests.fs` / frontmatter tests: placeholder scalar and
  `by: unknown` produce `verified-shape` warnings; valid list does not.

## Order of work

1. Frontmatter/validate wording and `by` check (+ tests).
2. `fix` stripping (+ tests).
3. `okf verify` domain, args, CLI (+ tests).
4. Docs, skills, agent template, changelog.
5. `dotnet build` and `dotnet test --solution eru.slnx`; manual run of
   `eru okf verify` on a scratch file.
