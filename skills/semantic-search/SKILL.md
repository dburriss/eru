---
name: semantic-search
description: >-
  Activate when the user asks a conceptual question about a codebase or text
  corpus (e.g. "what do we have for X?", "find code/notes related to Y"),
  wants results by meaning rather than exact keywords, is looking for
  related/forgotten content to cross-reference, or when a plain grep/glob
  search for a concept comes up empty or is expected to miss synonyms and
  paraphrases. Uses the local `ck` semantic/lexical/hybrid search tool.
  Prefer this over grep for queries about ideas, concepts, or topics rather
  than exact strings, filenames, or code identifiers.
license: MIT
metadata:
  category: search
  requires: ck (https://github.com/BeaconBay/ck)
---

# Semantic Search

`ck` finds text by what it means, not just what words it contains — built with
Rust, combining regex search with semantic search powered by local embedding
models (no API calls, no data leaves the machine). It closes the gap where
plain grep fails: different authors phrase the same concept differently, and
you may not know the exact terminology used in the codebase or notes you're
searching.

Do NOT call `ck` directly without reading this file first — the flags below
(especially `--jsonl` and the stderr redirection) avoid two sharp edges in how
`ck` behaves (see Gotchas).

## Quick decision: which mode?

| Query is about...                            | Mode      | Flag       |
|-----------------------------------------------|-----------|------------|
| A concept/idea, phrased naturally              | Semantic  | `--sem`    |
| An exact acronym, identifier, or heading you know | Lexical | `--lex`    |
| A technical phrase mixing jargon + concept     | Hybrid    | `--hybrid` |

Default to `--sem`. Reach for `--lex` when the user gives you a short exact
term (an acronym, a tag, a function name, a proper noun) — semantic search
sometimes ranks a result *about* the term below unrelated conceptually-similar
ones, while lexical nails it. Use `--hybrid` for phrases that are half jargon,
half concept (e.g. "AWS Lambda cold start", "JWT token validation").

## Running a search

```bash
ck --jsonl --no-snippet --topk <N> --threshold <T> --sem "<query>" <path> 2>/dev/null
```

- `<path>` is the root to search — pass an explicit directory (the repo root,
  or a subtree like `src/`), not a bare `.`, since the tool's working
  directory when invoked isn't guaranteed to be where you want to search.
- Always redirect stderr to `/dev/null` (or a log file) and never rely on it
  for results — see Gotchas.
- Use `--jsonl` for anything you're going to parse; each line is one JSON
  object: `{"path": "...", "span": {"line_start": N, "line_end": N, ...},
  "language": "...", "score": 0.83}`.
- `--no-snippet` keeps output compact for scanning many results — re-read the
  specific line range from the file yourself if you need surrounding context.
  Add `--scores` only for human-facing CLI/TUI use.
- `--lex` and `--hybrid` take the same flags; hybrid scores are RRF-fused and
  sit on a much smaller scale (~0.01–0.05) — don't compare a hybrid score to a
  semantic one.
- `--json` + `jq` works for one-off scripting instead of `--jsonl`, e.g.
  `ck --json --sem "auth" . | jq -r '.[].file'`.

### Thresholds (0.0–1.0)

Start at **0.7** for focused, high-confidence results. Widen to **0.5** when
the user is explicitly exploring or brainstorming ("what else might relate
to..."). Go to **0.8+** only when you want to be conservative about surfacing
a match (e.g. before proposing a cross-reference/link between two documents).

Treat the threshold as a starting point, not a guarantee — the default
`bge-small` model sits on a compressed similarity scale, so even an unrelated
or nonsense query can return results above 0.7. **Always sanity-check the top
results against the query yourself before presenting them** — don't assume
"score cleared the threshold" means "genuinely relevant."

### Fallback to grep

If semantic search returns fewer than 3 results, or the results are clearly
off-topic on inspection, fall back to a literal grep/glob search for the query
terms before telling the user nothing was found. Semantic search can miss
very short or unusual queries (bare acronyms with no expansion in the text,
code-like tokens) that a plain text match would catch instantly.

## Presenting results

1. Sort by score, collapsing multiple hits in the same file into one entry
   (a file can match several chunks) — show its best score.
2. Give file paths (relative to the search root), scores, and a one-line
   reason each result matched — read the relevant line range if the snippet
   isn't enough context. Don't dump raw JSON at the user.
3. If the corpus is a notes vault using wiki-links, strip the path prefix and
   extension so results read as link targets (e.g. `Domain-Driven Design.md`
   → `[[Domain-Driven Design]]`) and suggest cross-links between top results.
4. If the corpus has time-bound content (daily notes, journals, changelogs),
   confirm with the user whether those should be excluded from conceptual
   search — they're usually noise for "what do we know about X" queries
   unless a result there scores unusually high (e.g. above 0.8).

## Index management

- No manual step needed for a first search — `--sem`/`--lex`/`--hybrid` build
  or incrementally update the index automatically on first use.
- `ck --status <path>` shows how many files/chunks are indexed, if you want to
  confirm state (not a prerequisite).
- `ck --index --model <model> <path>` / `ck --switch-model <model> <path>`
  switch embedding models; only needed for large-corpus or code-specialized
  tuning, not routine use:

  | Model | Chunk size | Context | Best for |
  |---|---|---|---|
  | `bge-small` (default) | 400 tokens | 512 tokens | Fast indexing, general text/code |
  | `nomic-v1.5` | 1024 tokens | 8K tokens | Large functions, long documents |
  | `jina-code` | 1024 tokens | 8K tokens | Code-specialized understanding |

- A `.ckignore` file (gitignore syntax) excludes paths from indexing —
  attachments, generated files, or anything not worth semantic search.

## Gotchas

- **`ck`'s log/status lines can land on stdout, not just stderr**, and its
  automatic index-refresh step emits a `WARN` line per file it fails to
  parse. If you pipe `ck` output into something expecting pure JSONL, filter
  to lines starting with `{` (`grep '^{'`) rather than trusting stdout is
  clean — especially right after new files were added to the corpus.
- **A malformed PDF can crash `ck` outright** (exit code 101), on every
  search against that path, not just indexing runs. If semantic search starts
  failing with exit 101, suspect a newly-added PDF with a corrupt embedded
  font before debugging the query — exclude it via `.ckignore` (or exclude
  `*.pdf` entirely if PDFs aren't needed for search).
- Case-sensitivity and regex knowledge are irrelevant for `--sem`/`--hybrid`
  (that's the point) but still apply to `--lex`.

## Example

User: "What do we have on retry logic?"

```bash
ck --jsonl --no-snippet --topk 10 --threshold 0.7 --sem "retry logic" src/ 2>/dev/null
```

→ Finds `RetryPolicy.cs`, `backoff.md`, `CircuitBreaker.cs`. Present as a
ranked list with scores and a one-line reason each matched.

## Resources

- [CK Search Documentation](https://beaconbay.github.io/ck/)
- [GitHub Repository](https://github.com/BeaconBay/ck)
