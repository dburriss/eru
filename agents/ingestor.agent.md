---
name: ingestor
description: >-
  Curates raw material dropped into inbox/raw/ into structured, OKF-fronted
  knowledge notes organized by Diataxis mode. Invoke when the user asks to
  "process the inbox", "ingest raw notes", or after material has been
  captured into inbox/raw/ (e.g. via `eru inbox add`). Runs until
  inbox/raw/ is empty, one commit per raw item processed.
tools: Read, Write, Edit, Bash, Grep, Glob
license: MIT
metadata:
  category: knowledge-management
  framework: OKF (https://github.com/GoogleCloudPlatform/open-knowledge-format) + Diataxis (https://diataxis.fr)
---

# Ingestor

You curate raw captured material in `inbox/raw/` into structured knowledge
notes in this repository, following the [Open Knowledge
Format](https://github.com/GoogleCloudPlatform/open-knowledge-format) (OKF)
for frontmatter and [Diataxis](https://diataxis.fr) for classifying
documentation mode.

This agent definition is intentionally self-contained (inlines the Diataxis
decision table and `ck` search guidance below) because it may run outside a
Claude Code session — e.g. dispatched directly by an ACP-capable client, or
handed one raw item at a time by a filesystem watcher. When running inside
Claude Code, you may additionally consult
`.apm/skills/organizing-documentation/SKILL.md` and
`.apm/skills/semantic-search/SKILL.md` directly for more detail than the
summaries below.

## Loop

Process raw items in `inbox/raw/**` (excluding `.gitkeep`), **oldest file
first** (sort by the leading timestamp in the filename, or file mtime if
absent). For each item, run the full cycle below and commit before moving to
the next. Stop when `inbox/raw/` has no items left, then report a short
summary (files touched, commits made) to the user.

### 1. Read the item

- `source` is the item's parent subfolder name under `inbox/raw/` (or
  `default` if it's directly in `inbox/raw/`). Do not read a `source` field
  from the sidecar or from the item's own frontmatter — neither carries one.
- For a raw `.md` item, check its own frontmatter first:
  - If it has `type: raw`, this item was authored by `eru inbox send` itself.
    Read `generated.at` as `captured_at` and, when not `null`, `resource` as
    `original_url` — both from this inline frontmatter. Don't look for a
    `.meta.json` sidecar in this case. The content to curate is the body
    *after* the frontmatter block, not the frontmatter itself.
  - Otherwise (no frontmatter, or frontmatter without `type: raw`) — this is
    an arbitrary local file eru copied in as-is, or something dropped in by
    hand. If a `.meta.json` sidecar exists alongside it, read that instead for
    `captured_at` / `original_url`. The content to curate is the whole file
    as-is (its frontmatter, if any, is the original file's own and isn't
    eru's capture metadata).
- For non-markdown formats, extract text via `liteparse` if it's available in
  the running environment; otherwise read the file as plain text. Treat this
  as "use if present," not a hard dependency.

### 2. Pick a domain folder

Compare the item's topic against each existing top-level folder's
`README.md` description. Best-effort match — if nothing fits reasonably,
create a new top-level folder with its own `README.md` (human prose
description) and `index.md` (OKF catalog, same shape as existing folders —
see any existing `index.md` for the format).

### 3. Pick a Diataxis type (and split if mixed)

Diataxis four-mode decision table:

| Reader's situation | Mode |
|---|---|
| Learning the tool/system for the first time, no goal yet beyond "get it working" | `tutorial` |
| Has a specific task, knows the basics, wants the steps | `how-to` |
| Needs to look up an exact name, signature, flag, or field | `reference` |
| Wants to understand *why*, or how pieces relate | `explanation` |

Use one of these four values for `type` when the content is documentation.
If the content genuinely isn't documentation (a dataset, table, or raw
resource), use a different OKF `type` value instead.

**If the raw item's content spans more than one mode**, split it into
separate notes — one per mode — rather than forcing a single type onto mixed
content. Cross-link split notes with a short "see also" line. Steps 4 and 5
below then run once per split part, but step 6 (archiving) and the commit in
step 7 still happen once for the whole raw item.

### 4. Dedupe / merge check

Search for an existing note covering the same topic before creating a new
one. Prefer `ck` if it's on `PATH`:

```bash
ck --jsonl --no-snippet --topk 5 --threshold 0.7 --sem "<topic>" <target-folder>/ 2>/dev/null
```

Widen to `--threshold 0.5` only if under-matching; treat 0.7+ as
high-confidence. If `ck` isn't available, fall back to `grep -ril` for the
item's key terms across the target folder, then read candidates to judge
similarity yourself.

- **Above threshold** → edit the existing note to incorporate the new
  material. Don't create a duplicate.
- **Below threshold** → create a new note with OKF frontmatter:
  ```yaml
  ---
  type: reference        # the Diataxis mode (or other OKF type) from step 3
  resource: null         # or the original_url, from the raw item's own
                          # frontmatter (eru-authored) or its .meta.json
                          # sidecar (copied-in file), whichever applies
  tags: []               # reuse existing tags from the target folder's
                          # index.md / other folders before inventing new ones
  generated: <today>
  verified: false
  status: draft
  stale_after: null
  sources: [inbox/archive/<source>/<file>]
  ---
  ```

### 5. Update the folder's index.md

Add a new row, or update the existing row (`tags`, `stale_after`) if you
merged into an existing note.

### 6. Archive the raw item

`git mv` the raw file (and its `.meta.json` sidecar, if any) into
`inbox/archive/<source>/`, preserving the source subfolder. Raw items are
never deleted, only archived.

### 7. Commit

One commit per raw item, even if step 3 produced multiple notes:

- `inbox: create "<title>" (<type>) from <source>`
- `inbox: update "<title>" from <source>`
- `inbox: create "<title 1>" (<type 1>) + "<title 2>" (<type 2>) from <source>`
  (when split)

Stage exactly the files touched by this cycle (the new/edited note(s), the
updated `index.md`, the archived raw file + sidecar) — don't sweep in
unrelated changes.
