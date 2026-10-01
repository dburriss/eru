# Plan: bring eru fully to OKF v0.2

## Context
eru targets OKF v0.2 (`plans/okf-conformance-validation.md:6`) but still has v0.1 remnants: the version it writes, the ingestor template shapes, and some docs. Spec checked against upstream `SPEC.md` (GoogleCloudPlatform/open-knowledge-format, v0.2). Key v0.2 facts:
- `okf_version: "0.2"` in root `index.md`; only `type` required; consumers MUST NOT reject missing optional/unknown keys.
- `generated: {by, at}`; `verified`: list of `{by, at}` (single map allowed). Absent `verified` = unverified.
- `status`: `draft|stable|deprecated`; absent ⇒ `stable`. `stale_after`: ISO 8601 datetime, stale when `now >= stale_after`.
- `sources`: list of **objects**: `resource` (required), optional `id`, `title`, `author`, `usage_count`, `last_modified`, `usage_window {from,to}`.
- Legacy: `timestamp` → `generated`; `# Citations` body → `sources`. Consumers MAY fall back to `timestamp`, MAY parse `# Citations`.
- Actors: `<producer>/<version>`, `human:<id>`, `process:<id>`; trust keys off `human:` prefix (already matches `HtmlTemplates.fs:17-22`).

## Deep-dive findings (spec re-read in full)
- `by` AND `at` are both **required** in `generated` and each `verified` entry; `at` must be ISO 8601 **with explicit UTC offset** (e.g. `2026-06-25T09:00:00Z`). eru's `actorAt` treats `at` as optional (fine for tolerant reading); validator should *warn* on missing/offset-less `at`. A bare date (`generated: <today>`, `2026-10-01`) is not spec-valid.
- `sources` entries are objects: `resource` (required; URL, bundle-relative `/path`, relative path, or non-navigable scope descriptor such as "all queries in BigQuery project X" — so never require resolvability), optional `id`, `title`, `author` (actor), `usage_count` (**integer**), `last_modified` (datetime). `usage_window: {from, to}` is a **top-level sibling** of `sources` (overridable per entry). Bare-string entries are not spec; read tolerantly, warn.
- Body attribution uses markdown footnotes keyed by `sources[].id` (`[^ga4-schema]`), not `# Citations`. Ingestor should assign `id`s if it cites claims.
- `stale_after`: absolute datetime with offset, stale when `now >= stale_after`. `status` default (absent) = `stable`.
- Consumers should preserve unknown keys when round-tripping — `OkfFix` edits must stay text-level (as `setType` is) and never re-serialize frontmatter.
- Root `index.md`: only `okf_version` allowed (already enforced). `log.md`: newest first, `YYYY-MM-DD` headings (already enforced; ordering not checked — optional warning). Index entries SHOULD carry the concept description (`OkfFix.conceptRow` already uses it; verify).
- New in v0.2, no eru work needed: type `Attested Computation` (+ `runtime, parameters, computation, executor, attester`) and `# Computation` heading; unknown types/keys must be tolerated.
- Trust tiers in `HtmlTemplates.trustTier` already match (no `verified`→unverified; any `human:` actor→human-reviewed; else machine-confirmed). Note: spec says *any* `human:` entry, code should be checked it uses `exists` not `forall`/last-only.

## Changes

### 1. Version string (created)
- `src/Eru.Domain/OkfFix.fs:15`: `okfVersion = "0.2"` (used at :56, :144, :148, :151, :222). Update doc comment.
- `agents/ingestor.agent.md:88`: `okf_version: "0.2"`.
- `plans/ingestor-okf-conformance.md:12,29`: update literals.
- Tests: `tests/Eru.Tests/OkfFixTests.fs:72` fixture → "0.2"; add a test asserting a newly created root index contains `okf_version: "0.2"`. Normalize `"1.0"` fixtures in FrontmatterTests/BundleDiscoveryTests/SourceBundleTests to "0.2" (cosmetic).
- Decision to keep: existing `okf_version` values are preserved (not upgraded) by `fix`, per `docs/reference/cli.md:889`.

### 2. Ingestor template (created) — `agents/ingestor.agent.md:140-155`
- `generated:` → `by: <producer>/<version>` (e.g. `ingestor/<model>`) + `at: <full ISO 8601 datetime with Z>` (nested map; not a bare date).
- Drop `verified: false`; omit `verified` entirely (absent = unverified). Document that humans add `verified: [{by: human:<id>, at: ...}]`.
- `sources:` → list of objects: `- resource: inbox/archive/<source>/<file>` (add `title`/`author` when known).
- `stale_after: null` → omit unless set; `status: draft` stays valid.
- Also update the doc copy referenced in `docs/how-to/generate-docs-from-inbox-with-gh-aw.md` (:28, :86, :204-207): remove `verified: true/false` boolean instructions; reword the "untrusted" signal (e.g. via `status: draft` / no `verified`).

### 3. Reads (`src/Eru.Domain/Frontmatter.fs`)
- Already v0.2 for `generated`, `verified`, `stale_after`, `status`, `resource`, `tags`, `title`, `description`.
- Add (small, spec "SHOULD"): `sources` lens → `SourceRef list` (`Resource`, `Id`, `Title`, `Author`, `UsageCount: int option`, `LastModified: DateTimeOffset option`) plus top-level `usage_window` (`From`/`To`); tolerate bare strings as `{Resource = s}`; skip entries with no resource. Carry on `OkfDoc` (`Config.fs:200`) only if needed by consumers; otherwise expose lens only to avoid churn.
- Add legacy fallback: `generated` absent and scalar `timestamp` present → `{By = "unknown"; At = parsed}` (spec MAY). Decide: implement (cheap, spec-sanctioned). Skip `# Citations` body parsing (MAY, not worth it) and say so in docs.
- Treat absent `status` as `stable` where status is consumed (check `SiteModel.fs`/`OkfFix.conceptRow`); confirm no code treats absent as draft/unknown.
- Optionally log/ignore scalar `generated` (stays None, correct since not spec-shaped).

### 4. Validation (`src/Eru.Domain/OkfValidate.fs`)
- Conformance rules already match v0.2 (non-empty `type`, root-index only `okf_version`, `log.md` dates). Do not add shape rejection (spec forbids rejecting for optional fields).
- User chose: add non-fatal **warnings**. Extend `OkfValidate` results with a severity (Error vs Warning), keeping the exit code driven by errors only (spec: never reject for optional fields). Warn on: `okf_version` not "0.2"; `generated` not a `{by,at}` map (incl. scalar/legacy `timestamp`) or `at` missing/without UTC offset; `verified` not a map/list of `{by,at}` or entries missing `by`/`at`/offset; `stale_after` unparseable or without offset; `sources` not a list of objects, entries lacking `resource`, non-integer `usage_count`; `log.md` headings not newest-first; `status` outside `draft|stable|deprecated`; legacy `# Citations` section present. Update `OkfValidateCli` output + `docs/reference/cli.md:843` and add tests per warning (and that warnings don't fail validation).
- Read scope chosen: core + `timestamp` fallback; no `# Citations` body parsing.

### 5. Other created output
- `InboxSend.fs:86-89`: `generated` shape already v0.2. Fix unquoted `resource:` (YAML-quote URLs containing `: ` / `#`); update `InboxSendTests.fs:182-188` accordingly.
- `OkfFix.fs:102` `conceptRow`: read `stale_after` via `Frontmatter.staleAfter` (ISO-normalized) instead of raw scalar — optional polish; keep raw fallback if unparseable.

### 6. Docs
- `docs/reference/cli.md` (:843, :871, :889), `skills/eru/SKILL.md`, `skills/eru/references/commands.md`, `CHANGELOG.md`: state "OKF v0.2", note `fix` writes `okf_version: "0.2"`.

## Tests to add/update
- `FrontmatterTests.fs`: `sources` (objects, bare strings, missing `resource`), `timestamp` fallback, scalar `generated` still None, absent status.
- `OkfFixTests.fs`, `OkfValidateTests.fs`: version literal; any new warnings.
- `InboxSendTests.fs`: quoting.
- No change needed: `HtmlTemplatesTests.fs` trust tiers, `SiteGeneratorGraphTests.fs` (unless `OkfDoc` gains a field — then update fixtures at `IndexBuilderTests.fs:19`, `SiteGeneratorGraphTests.fs:19`).

## Verification
- `dotnet build` and `dotnet test` (whole solution).
- `eru okf init` in a temp dir → root `index.md` has `okf_version: "0.2"`; `eru okf validate` passes; `eru okf fix` on a v0.1 bundle keeps its existing version.
- Feed an ingestor-style note (new template) through `Frontmatter` parsing/site build and confirm generated shows and trust badge = unverified; add a `verified: [{by: human:x, at: ...}]` note → human-reviewed.
