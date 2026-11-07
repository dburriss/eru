---
name: semantic-search
description: >-
  Activate when the user asks a conceptual question about a codebase or text
  corpus (e.g. "what do we have for X?", "find code/notes related to Y"),
  wants results by meaning rather than exact keywords, is looking for
  related/forgotten content to cross-reference, or when a plain grep/glob
  search for a concept comes up empty or is expected to miss synonyms and
  paraphrases. Uses the local `ferret` ranked semantic/lexical/hybrid search
  tool. Prefer this over grep for queries about ideas, concepts, or topics
  rather than exact strings, filenames, or code identifiers.
license: MIT
metadata:
  category: search
  requires: ferret (https://github.com/dburriss/ferret)
---

# Semantic Search

`ferret` ranks files by relevance: BM25 keyword search (`--lex`), meaning via
a local embedding model (`--sem`), or both fused (`--hybrid`). Nothing leaves
the machine and indexes live in Ferret's cache (`~/.cache/ferret`), not in the
searched directory. It closes the gap where plain grep fails: different
authors phrase the same concept differently, and you may not know the exact
terminology used in the codebase or notes you're searching.

Do NOT call `ferret` for ranked search without reading this file first. The
ranked flags and the "no score thresholds" rule below avoid the most common
wrong answers.

## Quick decision: which mode?

| Query is about...                                 | Mode     | Flag       |
|----------------------------------------------------|----------|------------|
| A natural-language question or unsure what fits    | Hybrid   | `--hybrid` |
| A concept or vague recall, phrased naturally        | Semantic | `--sem`    |
| An exact acronym, identifier, or heading you know   | Lexical  | `--lex`    |

Default to `--hybrid`: it has the best measured quality and handles phrases
that are half jargon, half concept (e.g. "AWS Lambda cold start"). Use
`--sem` for purely conceptual recall. Use `--lex` for a short exact term (an
acronym, a tag, a function name, a proper noun), and whenever the semantic
model isn't available (offline, first run not allowed to download): `--lex`
never needs it.

## Running a search

```bash
ferret --hybrid --jsonl --limit <N> "<query>" <path>
```

- `<path>` is the root to search. Pass an explicit directory (the repo root,
  or a subtree like `src/`), not a bare `.`, since the tool's working
  directory when invoked isn't guaranteed to be where you want to search.
- Use `--jsonl` for anything you're going to parse; each line is one JSON
  object: `{"file": "...", "line": N, "score": 4.7, "text": "..."}`. A file
  appears on up to 3 lines (its best matching lines), grouped together, files
  in rank order.
- Always pass `--limit` (max files, default 100). Semantic and hybrid search
  rank every file, so the tail is weak matches; start with 10.
- With a ranked mode the first positional is the query terms, not a regex.
  `-i -F -w -l -c` are usage errors here; if you need those, run a plain
  `ferret -i -w <pattern> <files>` over the ranked files afterwards.
- `--scores` prefixes each line with its score for human-facing output.
- Stdout carries the results; diagnostics (for example a missing model) go to
  stderr. Check the exit code: 0 match, 1 no match, 2 error.

### No score thresholds

Never filter on a score. Scores only compare within one run, and the scale
differs by mode (BM25 is unbounded, hybrid tops out around 0.03). Use rank
order and `--limit` instead. **Always sanity-check the top results against
the query yourself before presenting them**: semantic search returns
something for every query, even a nonsense one, so being returned is not
evidence of relevance.

### Fallback to grep

If a ranked search returns fewer than 3 files, or the top results are clearly
off-topic on inspection, fall back to a literal grep/glob search for the query
terms before telling the user nothing was found. Ranked search can miss very
short or unusual queries (bare acronyms with no expansion in the text,
code-like tokens) that a plain text match catches instantly.

## Presenting results

1. Keep Ferret's rank order, collapsing the up-to-3 lines per file into one
   entry.
2. Give file paths (relative to the search root) and a one-line reason each
   result matched: use the returned lines, or read the file around them if
   that isn't enough context. Don't dump raw JSON at the user, and don't
   present a score as a confidence level.
3. If the corpus is a notes vault using wiki-links, strip the path prefix and
   extension so results read as link targets (e.g. `Domain-Driven Design.md`
   → `[[Domain-Driven Design]]`) and suggest cross-links between top results.
4. If the corpus has time-bound content (daily notes, journals, changelogs),
   confirm with the user whether those should be excluded from conceptual
   search; they're usually noise for "what do we know about X" queries.

## Index and model management

- No manual step is needed for a first search: ranked searches build the
  index on first use and update it on later runs.
- Pre-build at a time you choose: `ferret --index <path>` (lexical),
  `ferret --index --sem <path>` (semantic), `ferret --index --hybrid <path>`
  (both; one command is enough). Rebuild from scratch with
  `ferret --index --clean <path>`; delete all indexes with `ferret --clean`.
  These take paths only, never a query.
- `--sem` and `--hybrid` need a ~32 MB model (`bge-small-en-v1.5`), downloaded
  on first use or ahead of time with `ferret --model download`. Set
  `FERRET_OFFLINE=1` to never download (a missing model is then an error
  naming the directory it looked in), and `FERRET_MODEL_DIR` to point at an
  existing copy.
- A same-size, same-mtime edit isn't noticed by the lexical index; run
  `ferret --index --clean <path>` if results look stale.
- Excluding files: put gitignore-syntax patterns in a `.ferretignore` **inside
  the search root** (ignore files above the root aren't applied, and there is
  no `--exclude` flag). Ferret also skips hidden files, symlinks, binary files
  and anything in `.gitignore` or `.ignore`.

## Gotchas

- **`--sem` and `--hybrid` always return `--limit` files**, even for a nonsense
  query (e.g. "purple monkey dishwasher" returns five unrelated files). Only
  `--lex` returns nothing when no term matches. If the top results share no
  meaning with the query, report that nothing relevant was found rather than
  presenting them.
- **Text-like non-markdown files are indexed too**, such as `.svg` logos,
  which can surface as junk hits. Exclude them (and generated or vendored
  files) with a `.ferretignore` inside the search root, e.g. `*.svg`.
- **`--lex` is noisy on conceptual queries** (common words match generic
  files); use `--hybrid` or `--sem` for those.

## Example

User: "What do we have on retry logic?"

```bash
ferret --hybrid --jsonl --limit 10 "retry logic" src/
```

→ Finds `RetryPolicy.cs`, `backoff.md`, `CircuitBreaker.cs`, in rank order.
Present as a ranked list with a one-line reason each matched.

## Resources

- [Ferret repository](https://github.com/dburriss/ferret)
