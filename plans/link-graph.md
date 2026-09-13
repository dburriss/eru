---
status: planned
---

# Plan: Link Graph

## Context

`eru` caches markdown knowledge files from multiple git sources but has no
notion of how those documents reference each other, or which external
webpages they link out to. To answer "what points to what" (and later,
things like dead-link detection or a "related documents" view on the
generated site), we need a graph of link edges between documents, where
each node has a stable identity.

Internal documents are identified by the pair `(SourceName, RemotePath)`,
formalized as `EntryId` in `plans/entry-id.md`. Links out to `http(s)://`
URLs have no existing identity concept in the domain at all. This plan
introduces a `NodeId` type that covers both cases (wrapping `EntryId` for
the internal case), and a `LinkGraph` module that builds a graph of edges
by scanning cached document bodies for markdown links.

**Depends on `plans/entry-id.md` landing first** — `NodeId.InternalNode`
is defined in terms of `EntryId` rather than a fresh `(string * string)`
pair.

**Reuse note:** the step after this is wiring the graph into
`eru site generate` (e.g. showing "links to" / "linked from" on each
generated page). `Eru.Site` already depends on `Eru.Domain` (see
`IndexBuilder.fs`), so as long as the graph-building logic lives in
`Eru.Domain` as pure data (`LinkGraph.execute` → `BuildResult`) rather than
behind CLI-only plumbing, `Eru.Site` can call it directly with no new
project dependency. The design below keeps all CLI-specific concerns
(table/json/dot rendering, Argu wiring) in `Eru.Cli`, and adds one small
reusable query helper in `Eru.Domain` for exactly the "what does this doc
link to/from" lookup a site page will need.

## Design

### 1. Domain: `src/Eru.Domain/LinkGraph.fs` (new)

Pure logic, no IO, following the `OkfValidate.fs` shape (`Command` /
`Deps.execute` split):

```fsharp
type NodeId =
    | InternalNode of EntryId
    | ExternalNode of Url: string

val nodeKey : NodeId -> string   // EntryId.toString or the raw URL — for JSON/table/dot rendering

type Edge = { From: NodeId; To: NodeId }

type Command = { SourceFilter: string option }
type BuildResult = { Nodes: NodeId list; Edges: Edge list }
```

- `resolveLink (source: string) (currentPath: string) (target: string) : NodeId option`
  - `http://`/`https://` targets → `Some (ExternalNode target)`.
  - anchor-only (`#...`), `mailto:`, empty targets → `None` (not a document link).
  - anything else is treated as a relative path: resolve against
    `Path.GetDirectoryName currentPath`, normalize `.`/`..` segments, and
    produce `InternalNode { Source = source; RemotePath = normalizedPath }`
    — **same-source only**; a link that resolves outside the source root
    is dropped (`None`) rather than guessed at.
- `execute (deps: Deps) (cmd: Command) : Result<BuildResult, string>`
  - Loads all `IndexEntry` values across sources (filtered by
    `cmd.SourceFilter` if given), following the same
    `eff.Sources |> List.collect (fun src -> deps.ReadSourceIndex src.Name ...)`
    pattern already used in `Search.fs` (there's no shared "all entries"
    helper yet — this follows existing precedent rather than introducing
    one).
  - For each entry, reads body via `deps.ReadCachedSourceContent src.Name path`.
  - Extracts raw link targets via the new `deps.ExtractLinks content` (see below).
  - Resolves each target with `resolveLink`, builds `Edge` list, and
    collects the distinct `Nodes` set (every scanned entry as
    `InternalNode`, plus every resolved link target).
- `edgesFor (result: BuildResult) (node: NodeId) : {| Outgoing: NodeId list; Incoming: NodeId list |}`
  — small pure query helper over `BuildResult`, added specifically so
  `Eru.Site`'s `IndexBuilder.fs` can later look up a single document's
  outbound/inbound links without re-deriving adjacency logic itself. Not
  consumed yet in this plan, but keeping it in `Eru.Domain` (not
  `Eru.Cli`) is what makes the site-generation follow-up a same-layer call
  rather than a new cross-project dependency.

### 2. `Deps` addition: `src/Eru.Domain/Deps.fs`

One new field, per the existing "add a field, wire an adapter" convention:

```fsharp
ExtractLinks: string -> string list   // raw markdown body -> raw link target strings
```

### 3. Adapter: `src/Eru.Adapters/MarkdownLinkAdapter.fs` (new)

- Lives in `Eru.Adapters` rather than a new `Eru.Graph` project —
  `Eru.Adapters` is already the impure/IO layer and already has package
  references (`YamlDotNet`, `SimpleExec`), so adding `Markdig` here keeps
  `Eru.Domain` dependency-free and avoids new project scaffolding.
- Add `<PackageReference Include="Markdig" Version="0.*" />` to
  `src/Eru.Adapters/Eru.Adapters.fsproj` (same version already used by
  `Eru.Site.fsproj`).
- `extractLinks (content: string) : string list` — parse with
  `Markdown.Parse`, walk the AST for `LinkInline` nodes, return their
  `Url` values (skip null/empty). A real AST walk (not the `Eru.Site`
  regex-ish `stripFrontmatter` helper), so reference-style links and
  nested inlines are handled correctly.
- Wire into `AdapterDeps.create`: `ExtractLinks = MarkdownLinkAdapter.extractLinks`.

### 4. CLI: `src/Eru.Cli/GraphCli.fs` (new)

Follows `OkfValidateCli.fs`'s shape exactly:

```fsharp
type Cmd = { SourceFilter: string option; Format: OutputFormat; Dot: bool }
```

- `(|GraphCmd|_|)` active pattern mapping `ParseResults<EruArgs>` → `Cmd`.
- `renderTable` — headers `["From"; "To"]`, one row per edge (`nodeKey` each side).
- `renderJson` — `{ nodes: string list; edges: {from: string; to: string} list }` via `System.Text.Json`, camelCase (no third-party JSON lib, per `CLAUDE.md`).
- `renderText` — `"<from> -> <to>"` per line.
- `renderDot` — used when `cmd.Dot` is set (takes precedence over `Format`):
  emits `digraph { ... }`, quoting node ids, and giving `ExternalNode`
  nodes a distinct shape (`[shape=box]`) so internal vs. external nodes
  are visually distinguishable when rendered with Graphviz.
- `run (deps: Eru.Deps) (cmd: Cmd) : int` — calls `LinkGraph.execute`,
  picks a renderer, returns 0/1.

### 5. CLI registration: `src/Eru.Cli/Args.fs`

- New `GraphArgs` DU: `Output of string` (`-o`), `Source of string`
  (optional filter), `Dot` (flag) — same attribute style as existing args
  (`ExactlyOnce`/`AltCommandLine`, `Usage` match arms).
- `EruArgs` gets `| [<SubCommand>] Graph of ParseResults<GraphArgs>` plus a
  `Usage` arm, mirroring the `Okf` entry.
- `src/Eru.Cli/Program.fs`: `open Eru.Cli.GraphCli`, add dispatch arm
  `| GraphCmd cmd -> GraphCli.run deps cmd`.

### 6. Tests: `tests/Eru.Tests/LinkGraphTests.fs` (new)

- Unit-test `resolveLink` directly (external URL → `ExternalNode`;
  relative path normalization incl. `..`; anchor/mailto → `None`;
  cross-source-looking target within same source stays same-source).
- `execute` tests build a full `Deps` record literal (per convention) with
  `ReadSourceIndex`, `ReadCachedSourceContent`, and `ExtractLinks` stubbed
  to fixed values, asserting the resulting `Nodes`/`Edges`.
- Every other existing full `Deps` literal across the test suite (the ~10
  files enumerated by the OKF-validate plan: `DisconnectTests.fs`,
  `AddTests.fs`, `IndexBuilderTests.fs`, `InitTests.fs`,
  `CollectionTests.fs`, `RemoveTests.fs`, `SyncTests.fs`, `SearchTests.fs`,
  `ManifestTests.fs`, `SourceTests.fs`) needs a trivial
  `ExtractLinks = fun _ -> []` stub added — this is the known "ripple
  cost" of adding a `Deps` field.

## Verification

- `dotnet build` — confirms `Eru.Adapters` picks up the new `Markdig`
  reference and everything compiles.
- `dotnet test` — new `LinkGraphTests.fs` pass; existing tests still pass
  after the `Deps` literal ripple update.
- Manual: in a repo with `eru` sources already synced, run:
  - `eru graph` (table output) — sanity-check edges look right for a
    document with known links.
  - `eru graph -o json` — confirms JSON shape.
  - `eru graph --dot > graph.dot && dot -Tsvg graph.dot -o graph.svg` (if
    Graphviz is installed) — visually confirms internal vs. external
    nodes render distinctly and edges match the source markdown.
