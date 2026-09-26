---
status: discussion
---

# Plan: OKF bundle discovery

## Context

`eru` already understands OKF (Open Knowledge Format) frontmatter for
*documents* — `Frontmatter.fs` extracts `type`, `title`, `status`,
`generated`, `verified`, `stale_after`, `resource`, and `eru okf validate`
checks §11 conformance. What it doesn't do yet is treat an OKF bundle
(a directory with a self-describing `index.md`/`log.md` and typed concept
files) as a *source of manifest-equivalent information* — today the only way
a source repo advertises "here are the files I publish, with these tags and
descriptions" is a hand-written `.eru/manifest.json`.

This is the item tracked in `todo.md`: "Integrate OKF into idea of source and
collection/OKF bundle." This doc captures the design discussion so far.
Collections (`CollectionConfig`, hand-curated cross-source file lists) are
explicitly **out of scope** — they stay as-is for now.

## Terms

- **Source** — a git repository (`SourceConfig`: `Name`, `Url`, `Branch`).
  A source is just "a repo eru knows about"; it says nothing about what's
  published from it.
- **Bundle** (new concept, name borrowed directly from OKF so we don't
  invent a second word for one idea) — a declared location within a source
  that publishes knowledge files, plus *how* to find out what's in it. A
  bundle is either:
  - **Manifest-backed** — a `.eru/manifest.json` at the bundle's path
    (today's only mechanism).
  - **OKF-backed** — a directory whose `index.md` carries `okf_version`;
    membership and metadata (`type`, `tags`, `description`, ...) are
    *discovered* by walking the tree and reading each file's frontmatter,
    rather than declared in a side-car JSON file.

  A source can have zero or more bundles at different paths. This replaces
  `SourceConfig.BasePath`, which today conflates "where the manifest lives"
  with "what repo this is" — `BasePath` moves onto `Bundle` as `Bundle.Path`.

## Decision: `RemotePath` stays repo-root-relative

`EntryId = {Source; RemotePath}` is eru's canonical file identity — it's the
on-disk encoding in `.eru/eru.lock` (`sourceName:remotePath`), the cache key
for `sources/<name>/index.json`, and the link-graph node key
(`LinkGraph.InternalNode`). All three assume it's globally unique per source.

If `RemotePath` were made relative to a bundle's path, two bundles in the
same source could collide on identical relative paths — most obviously
`index.md`/`log.md`, which OKF requires at *every* bundle root, so two
bundles would both produce `EntryId {Source; "index.md"}` for two different
files. That would break lock file storage, cache keying, and link-graph
identity simultaneously.

So: **`RemotePath` remains relative to the repo root**, exactly as today.
`Bundle` is a non-scoping metadata layer — it says "the files under this
subtree are described this way," it does not redefine what identifies a
file. Git already guarantees path uniqueness within one repo, so this keeps
collisions structurally impossible at the identity level.

## Problem: multiple bundles can still describe the same file

Even with identity settled, one declaration can still overlap another on the
same `RemotePath`. The one case that *doesn't* survive is two different-Kind
bundles registered at the identical path (`.eru/manifest.json` and a root
`index.md` with `okf_version` both at repo root) — bundle paths are unique
per source, so that resolves at **registration time**: auto-detect (or an
explicit `--kind`) picks one canonical `Kind` for that path, full stop, no
runtime ambiguity.

What does survive is **nested bundles** — a repo-root bundle and a
`docs/api/` sub-bundle (its own `index.md`, its own `okf_version`) both
physically contain `docs/api/auth.md`. The outer bundle's walk finds it; the
inner bundle's walk finds it too, possibly with different metadata. Since
`docs/api/` is a different, more specific path than `/`, this doesn't
conflict with path uniqueness — it's containment, not duplication.

This is not a new *kind* of problem — `Sync.fs` already merges two
differently-shaped metadata sources for one file (manifest vs. frontmatter):

- `Tags` — **union**: `existing.Tags @ Frontmatter.tags fm |> distinct`.
- `Description` — **precedence**: manifest wins if present, frontmatter is
  only a fallback (`Option.orElse`).
- `Type`/`Title`/`OkfStatus`/`Generated`/`Verified`/`StaleAfter`/`Resource`
  — frontmatter-exclusive, no competing writer today.

What's new is having *more than one* declaration of the **same kind** (two
bundles both offering `Tags` for one path) — something the current design
has never had to arbitrate, because there's exactly one manifest per source
and exactly one frontmatter block per file.

## Decision: track contributions per bundle, not a pre-blended value

The existing merge has a latent staleness bug that this would otherwise
inherit and multiply: manifest tags are fully rebuilt from `manifest.json`
every sync (`populateIndex` step 1c wipes and reseeds), but frontmatter tags
are only ever **appended** onto whatever was already cached
(`existing.Tags @ new |> distinct`). If a file's frontmatter drops a tag, it
never leaves `index.json` — it accumulates forever. This is invisible today
because there's only one contributor competing with the baseline; with
multiple bundles it would mean a bundle that stops covering a file leaves
permanent, unattributable residue in the merged tag set.

So: `IndexEntry` should stop storing pre-blended `Tags`/`Description`/etc.
and instead store **contributions keyed by declaring bundle** (repo-relative
path is still the map key; the value becomes something like
`Map<BundleId, Contribution>` where `Contribution` holds whatever a given
bundle declared for that file). The blended view search/site actually reads
(`Tags`, `Description`, ...) is then *computed fresh each sync* from
currently-declared contributions only — the same full-rebuild pattern
manifest seeding already uses, generalized to N contributors instead of an
assumed one.

This also gives:
- A real answer to "which bundle described this file" (useful for the site
  UI / debugging), instead of provenance being unrecoverably baked into one
  merged blob.
- A clean precedence rule for scalar fields (`Description`, `Type`, ...) —
  inspect the actual per-bundle values (e.g. most-specific bundle path wins,
  or declaration order) instead of a blind first-wins baked in at merge
  time.

## Performance

Concern raised: discovering an OKF bundle means reading frontmatter for
*every* file in it, including files nobody has pulled yet — unlike today's
lock-driven `ContentHash` caching, which only covers files already in
`.eru/eru.lock`.

Mitigations, in order of importance:

1. **Reuse the batched fetch.** `sync-batch-fetch.md` already turned
   per-file clones into one blobless sparse clone per source. Bundle
   discovery should do the same: list candidate `.md` paths cheaply (no
   blob content), then one clone/checkout pulls all their content together.
   Network I/O dominates, not parsing.
2. **Frontmatter parsing is already cheap.** `extractBlock` is a plain-text
   scan for the `---`...`---` fence — no Markdig render, no YAML parsing of
   anything but the small header block itself.
3. **Cache discovery results, not just pulled-file results.** Extend
   `sources/<name>/index.json` to hold discovered-but-not-pulled entries too,
   invalidated by comparing the source's HEAD commit SHA (one cheap
   `git ls-remote`) rather than re-walking and re-parsing on every command.
   First discovery of a bundle pays the full cost once; everything after is
   a SHA check until something actually changes.

## Backward compatibility

Three on-disk formats are potentially in scope; they need very different
treatment:

- **`.eru/eru.lock`** — no change. `RemotePath` is already repo-root-relative
  today (`Add.fs`'s `resolveRemotePath` already prefixes bare paths with
  `BasePath` before anything is stored), and `LockEntry.Tags`/`Description`
  are already a flattened *snapshot* taken at pull time, not a live merged
  view — the lock file has never recorded which bundle contributed a tag.
  Stays at `v1`.
- **`sources/<name>/index.json`** — this is the format that actually changes
  shape (flat `Tags`/`Description` → per-bundle contributions). It's already
  treated elsewhere as disposable, source-side derived state, so there's
  nothing to migrate: if the old flat shape fails to deserialize into the
  new shape, treat it as absent and let the next `eru sync` rebuild it from
  the manifest + a frontmatter walk. No data can be lost because none of it
  is authoritative — it's all reconstructible from the source repo.
- **`.eru/config.json` / global config** — the one real migration, but a
  small and lossless one: every existing
  `{Name; Url; Branch; BasePath: Some "docs"}` maps to
  `{Name; Url; Branch}` + one `Bundle {Path="docs"; Kind=Manifest}` (today's
  model already assumes at most one manifest-backed bundle per source, at
  `BasePath`); `BasePath: None` maps to a source with no declared bundle
  path. Config already carries a `Version` field with `Config.checkVersion`
  capped at `1` — bump to `2` and translate in-memory on read (old shape in,
  new shape out, rewritten on next save), matching the "refresh in place"
  approach — no need to keep reading/writing both shapes indefinitely.
- **`.eru/manifest.json`** (published by *other* repos) — untouched. It
  becomes the content of a `Manifest`-kind bundle; its own schema doesn't
  change.

## Versioning

Three of the four formats already carry *some* version marker, but none of
them do anything with it yet beyond the implicit "current version is 1":

- **`config.json`** (`GlobalConfig.Version`/`LocalConfig.Version`) and
  **`manifest.json`** (`SourceManifest.Version`) already have `Version: int`,
  checked by `Config.checkVersion` — but that function only ever rejects a
  file whose version is *newer* than supported
  (`version > supportedVersion`). There is no branch today for "version is
  older, migrate it," because the version has never changed from `1`. No new
  field needed here; what's missing is the actual migration function
  (`Version < 2` → translate the old shape in memory, e.g. `BasePath` →
  `Bundle`), which this work would be the first thing to require.
- **`.eru/eru.lock`** has a version marker (`# eru.lock v1`), but it's
  decorative — `LockFile.parse` strips every line starting with `#` without
  reading it. Not changing shape here, so nothing to do; noted only because
  it means this marker isn't actually wired up if it's ever needed.
- **`sources/<name>/index.json`** has *no* version field — it's serialized
  as a bare `Map<string, IndexEntry>`, with no room for a sibling key. This
  is also the one format whose shape is actually changing (flat fields →
  per-bundle contributions). Every read call site already falls back to
  `Map.empty` on deserialize failure, so today a shape mismatch happens to
  self-heal by accident — but relying on "the JSON happened to fail to
  parse" is fragile (a structurally-compatible-but-semantically-different
  shape wouldn't necessarily throw). Since this file's shape is changing
  anyway: wrap it as `{Version: int; Entries: Map<string, IndexEntry>}` now,
  so old-vs-new is an explicit check instead of an incidental one.

## CLI / UX: how bundles get declared

"Declaring a bundle" is two different acts, on two different sides, and one
of them needs no new tooling at all:

- **Producer side** (inside the source repo itself) — a Manifest-kind bundle
  is declared exactly as today, via `eru manifest init`/`add`/`remove`. An
  OKF-kind bundle isn't declared via any eru command at all: it announces
  itself by having an `index.md` with `okf_version` at some path, which is
  the whole point of OKF being self-describing. Nothing new needed here.
- **Consumer side** (the repo running `eru sync`/`eru add`) is the real
  question: how does it learn where a source's bundle(s) live? This is
  where `SourceConfig.BasePath` lived before — set once, at `eru source add`
  time, via `SourceAdd.detectBasePath`.

`detectBasePath` today is a **shallow, one-level check**: `ListRemoteTopLevel`
does a single, non-recursive listing of the repo root (like `git ls-tree
HEAD`, no `-r`), and the only thing checked is whether one of those top-level
entries is literally a directory named `KNOWLEDGE` or `knowledge`. It's a
convention match, not a tree walk — cheap by construction.

Decision: keep that shape, extended rather than replaced, split into two
tiers:

1. **Auto-detect one root bundle at `eru source add` time (unchanged cost).**
   Extend the existing top-level check: at the candidate location (the
   `KNOWLEDGE`/`knowledge` dir, or repo root if absent), look for an
   `index.md` with `okf_version` in addition to today's manifest check. This
   determines the bundle's `Kind` (`Okf` vs `Manifest`) automatically, same
   one-level listing, same zero-config experience for the common
   single-bundle-per-repo case. The result is stored as the source's
   (optional) bundle — `{Path; Kind}` — instead of a bare `BasePath` string.
2. **Explicit registration for additional bundles**, via a new command group
   mirroring the existing `source`/`collection`/`manifest` noun/verb
   pattern (`SubCommand` cases, `-g`/`Global`, `Dryrun`, `-o`/`Output`, per
   `Args.fs` convention):
   - `eru source bundle add <source> <path> [--kind manifest|okf]` — `--kind`
     optional, auto-detected the same way (checks for `index.md`+
     `okf_version` vs. `.eru/manifest.json` at `<path>`) when omitted.
   - `eru source bundle list <source>` — lists a source's registered
     bundles (path + kind), table/text/json like other `list` commands.
   - `eru source bundle remove <source> <path>` — deregisters a bundle
     (doesn't touch already-pulled files, same spirit as `source remove`
     vs. `disconnect`).

   Registering a bundle this way is also the trigger for the actual
   discovery walk described in Performance above (fetch + parse frontmatter
   under that path, seed `index.json`) — it's an on-demand, opt-in cost, not
   something that happens implicitly for paths nobody asked about.

Implication for existing commands: `eru source view` and `eru source files`
currently assume one `BasePath` per source; they'd need to iterate a
source's bundle list instead (e.g. group file listings by bundle, or add a
`--bundle <path>` filter) — exact rendering changes aren't nailed down yet.

## Decision: merge precedence for overlapping bundles

Working through it, the actual conflict surface is smaller than it first
looked:

- `Type`/`Title`/`OkfStatus`/`Generated`/`Verified`/`StaleAfter`/`Resource`
  **never conflict across bundles**. They're read straight from a file's own
  frontmatter block, and a file only has one frontmatter block — two
  OKF-kind bundles that both cover the same file (the nested case) are
  reading identical content, so they trivially agree. A Manifest-kind bundle
  can't declare these at all (`ManifestFileRef` only has
  `Path`/`Tags`/`Description`). No precedence rule needed, with or without
  multiple bundles.
- `Tags` stays **additive** — union across every covering bundle
  (manifest-declared + frontmatter-derived), same rule as today, just
  generalized from "exactly one manifest + one frontmatter" to "N covering
  bundles." More tags composing isn't a conflict.
- `Description` is the **only** field that needs a real tiebreak, because
  it's the only one more than one bundle can explicitly declare (any
  Manifest-kind bundle's `ManifestFileRef.Description`) — including two
  Manifest-kind bundles disagreeing with each other, e.g. a root manifest
  and a nested `docs/api/`-scoped manifest both listing
  `docs/api/auth.md`.

Rule, extending today's exact behavior (manifest wins, frontmatter is
fallback) to N bundles:

1. Find every bundle whose registered `Path` is a prefix of (or equal to)
   the file's `RemotePath` — the covering set.
2. Among Manifest-kind bundles in that set, take the one with the
   **longest matching path** (most specific containment wins) — a
   sub-bundle's own declaration about its own files outranks a parent's
   blanket one.
3. If no covering Manifest-kind bundle declares a `Description`, fall back
   to the frontmatter-derived one — unchanged from today.

This only requires bundle paths to be **unique per source** (no two bundles
registered at the exact same path — `eru source bundle add` should reject
that) so there's never a same-path tie to break, only containment to rank.

## Decision: nested bundles are allowed

No config error for a bundle path that's a subtree of another bundle's path.
Reasons:

- **The merge rule exists precisely for this case.** Longest-matching-path
  wins for `Description`, `Tags` unions, and the OKF-frontmatter fields have
  no conflict surface at all — that machinery was built to resolve
  containment. Forbidding nesting would throw it away with nothing left to
  do.
- **Nesting doesn't cost extra discovery work.** Content fetching is keyed
  by `RemotePath` per source — one batched clone covers every file in every
  bundle at once, cached by `ContentHash`. A file under two bundles' paths
  is fetched and frontmatter-parsed once regardless; "which bundles cover
  this file" is resolved from already-fetched data at merge time, not via a
  second walk.
- **The use case is real**: a repo with a simple root-level manifest for
  general docs, and one subtree (say `docs/api/`) that wants OKF's richer
  typing/status/verification without forcing that structure on the rest of
  the repo.

The only thing still disallowed is two bundles at the **identical** path
(enforced by the uniqueness constraint above) — containment is fine,
duplication isn't.

## Open questions

- Exact shape of `Bundle` (`{Path: string; Kind: Manifest | Okf}` or similar)
  and where it lives in config — replacing `SourceConfig.BasePath` touches
  `SourceAdd.fs`, `Add.fs`, `SourceView.fs`, `SourceList.fs`.
- Discovery cache invalidation details: is a source-level HEAD SHA check
  enough, or do we need finer-grained (per-bundle) freshness tracking?
- Exact rendering changes for `eru source view`/`eru source files` once a
  source can have more than one bundle.
- How `eru okf validate` relates to bundle discovery — shared file-walk
  code, given both need to walk a directory and read frontmatter?
