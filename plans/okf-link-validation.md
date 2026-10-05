---
status: done
---

# Plan: Link validation in `eru okf validate`

## Context

Documents sent through `eru inbox send` often carry relative links (`./other.md`, `../img/x.png`,
`[[Wikilink]]`) written against the author's original directory. `inbox send` copies them verbatim,
the ingestor curates them into the knowledge bundle, and `eru site generate` renders them without
rewriting links, so the broken links end up in the generated documentation. Nothing in the pipeline
detects them.

The ingestor should produce self-contained, consistent documentation. `eru okf validate` already
ends the ingestor's self-check loop (`agents/ingestor.agent.md` step 7: validate, fix, re-run until
clean), so reporting broken links there lets the agent fix them by rewriting the note without the link.

Two validate commands exist: `eru okf validate` (`OkfValidate.fs`, content conformance) and
`eru manifest validate` (`ManifestValidate.fs`, manifest globs resolve to local files; `verify` is its
alias). Only `okf validate` reads note bodies, so only it changes. `manifest validate` is untouched, and
`eru okf verify` (records a `verified` sign-off) is unrelated.

**Out of scope (later):** batches of inbox items whose links resolve only depending on processing
order (end-of-batch checking, raw-name to note-path mapping, dependency ordering); `okf fix` removing
links automatically; a send-time warning in `inbox send`; rewriting links in `eru site generate`.

## Decisions

- The link check lives in `eru okf validate` (not `okf verify`, not `manifest validate`).
- Anchors accept either the GitHub-style slug or the Markdig auto-identifier.
- `--strict-links` is included now.

## Design

### 1. Link extraction (`src/Eru.Domain/OkfLinks.fs`, new, pure)

`extract : string -> Link list` over a concept file body (frontmatter stripped, fenced code blocks
and inline code ignored):

```fsharp
type LinkKind = Page | Image | Wikilink
type Link = { Kind: LinkKind; Target: string; Fragment: string option; Line: int; Text: string }
```

- `[text](target)` is `Page`, `![alt](target)` is `Image`, `[[Target]]` / `[[Target|alias]]` is `Wikilink`.
- Skipped: `http(s)://`, `mailto:`, other URI schemes, and anchor-only (`#heading`) links.
- A trailing `#fragment` is split into `Fragment`.
- Register in `src/Eru.Domain/Eru.Domain.fsproj` before `OkfValidate.fs`.

### 2. Resolution (`OkfValidate.fs`)

- File links resolve against the containing file's directory with `LinkGraph.resolveLink` /
  `normalizePath` (so `/abs` is bundle-root relative), then are tested against the bundle's file list
  (`deps.ListMarkdownFiles` already returns it). Targets that are not markdown (images, PDFs) are
  checked through a per-directory `deps.ListLocalFiles` listing, and a folder link through
  `deps.DirectoryExists`, so no new `Deps` member is needed. Concept files and `index.md` files are
  checked; `log.md` and `README.md` are not.
- Wikilinks resolve with `LinkGraph.resolveWikilink` against a `TitleIndex` built from the bundle's
  concepts (path stem or `title`). `LinkGraph.titleIndexOf` is the new public builder, and
  `LinkGraph.fs` now compiles before `OkfValidate.fs`.
- A `#fragment` on a resolved page link is checked against the target's headings. It passes if it
  matches either the GitHub-style slug or the Markdig auto-identifier of any heading (so anchors work
  on GitHub and on the generated site). A missing heading is reported separately from a missing file.

### 3. Report as warnings

New warnings (never affect the exit code, consistent with OKF permitting dangling links):

| Rule | Message |
|------|---------|
| `broken-link` | `line 12: link '[text](foo.md)' -> 'foo.md' not found` |
| `broken-image` | `line 5: image '![alt](img.png)' -> 'img.png' not found` |
| `broken-wikilink` | `line 9: wikilink '[[Title]]' does not match any note` |
| `broken-anchor` | `line 3: 'foo.md#setup' -> heading 'setup' not found in foo.md` |

`--strict-links` on `eru okf validate` promotes these four rules to violations (non-zero exit) for CI.
Messages carry the line number and the verbatim link text so an agent can find and edit it.

### 4. CLI / docs / agent

- `src/Eru.Cli/Args.fs` (`OkfValidateArgs`) + `OkfValidateCli.fs`: add `--strict-links`; the new
  warnings flow through the existing text/table/json renderers.
- `docs/reference/cli.md` (`okf validate`): document the rules and flag. Replace the sentence saying
  broken cross-links are not flagged (now: reported as warnings only).
- `agents/ingestor.agent.md` step 7: warnings include broken links. For each, rewrite the note
  without the link: `[text](x)` becomes `text`, `[[Title|alias]]` becomes `alias`, `[[Title]]` becomes
  `Title`, a broken image is deleted, and a broken anchor keeps the link to the file without the
  fragment. Re-run `eru okf validate` until no warnings remain. The ingestor must not emit relative
  links to files that are not in the bundle.
- `agents/ingestor.agent.md` note-writing rules (steps 4-5): notes must be self-contained, so do not
  carry over relative links or images that point outside the bundle; keep only links to notes that
  exist in the bundle, and external URLs.
- Other docs that describe `okf validate`, updated to mention link warnings and `--strict-links`:
  - `skills/eru/SKILL.md` (okf command summary) and `skills/eru/references/commands.md` (`eru okf validate` section).
  - `skills/organizing-documentation/SKILL.md` (validate / warnings guidance for authors).
  - `docs/how-to/set-up-a-knowledge-repo.md` (the ingestor's validate step and "keep validate clean").
  - `README.md` command table, only if the one-line description changes.
- `CHANGELOG.md`.

## Tests (`tests/Eru.Tests/OkfValidateTests.fs`, plus `OkfLinksTests.fs`)

- Extraction: page/image/wikilink/alias forms, fragments, ignored http/mailto/anchor-only, ignored
  fenced and inline code, correct line numbers.
- Resolution: relative `../` links, bundle-root `/abs` links, resolved vs missing file, resolved vs
  missing image, wikilink by stem and by title, missing heading vs missing file, anchor matching
  GitHub-style and Markdig slugs.
- Validate: warnings produced with the right rule and message and exit code unchanged; with
  `--strict-links` they become violations.

## Verification

1. `dotnet test` passes.
2. Run `eru okf validate <dir>` on a scratch bundle with one note per broken-link kind plus valid
   links (expect 4 warnings, exit 0); with `--strict-links` expect a non-zero exit; confirm `-o json`
   includes the new rules.
3. Run `eru manifest validate` to confirm it is unaffected.

## Outcome

Implemented as designed. Image existence needed no `Deps` addition (see section 2). `index.md` files
are link-checked as well as concepts, since the ingestor edits their catalog rows by hand.
