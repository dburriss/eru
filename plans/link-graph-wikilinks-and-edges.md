# Enhance graph: wikilinks + edge descriptions

## Context

The `eru graph` command currently builds a graph of documents from standard markdown links `[description](path)` only. Two gaps prompted this change:

1. Many docs use Obsidian-style internal references `[[doc]]` / `[[doc|description]]`, which aren't recognized at all today — those connections are invisible to the graph.
2. `Edge` currently carries zero metadata beyond `From`/`To` — the link's description text (available in both syntaxes) is thrown away during parsing, even though it's valuable context once the graph is queried or rendered.

The desired outcome: both link syntaxes contribute edges, and edges carry an optional `Description`, laying groundwork for richer graph output later (not required to wire into every renderer now).

## Confirmed decisions

- `[[doc]]` (no pipe) → `Description = None`. No fallback to the target's title.
- `[[doc|description]]` and `[description](path)` → `Description = Some "..."`.
- External `http(s)://` links are **not** excluded from the graph — as today, they resolve to `ExternalNode` and get an edge. Description passthrough applies equally whether the markdown link target is internal or external (the description comes from the link's bracket text before resolution branches on the target). `[[...]]` wikilinks are always treated as internal-only (they don't produce `ExternalNode` targets in practice); only wikilinks get the two-step path/title-fallback resolution — markdown links (internal or external) keep today's resolution unchanged.
- Wikilink resolution is two-step: (1) try the same path-based resolution markdown links use today, (2) if that doesn't hit a known index entry, fall back to matching by title or filename-stem (Obsidian-style), scoped to the same source only.
- Markdown links do **not** get the title/stem fallback — only wikilinks. Keeps `[description](path)` behavior unchanged (no surprise redirects for broken relative paths).
- If neither step resolves against the index, keep today's permissive behavior: return the unverified path-based `InternalNode` rather than dropping the edge (parity with current markdown-link behavior, which has no existence check).
- CLI renderers: only worth lightly touching `renderJson` to include `description`; text/table/dot renderers are out of scope for this round.

## Current architecture (for reference)

- `src/Eru.Adapters/MarkdownLinkAdapter.fs` — `extractLinks (content: string) : string list`, Markdig AST walk (`UseAdvancedExtensions()`), collects `LinkInline.Url`. No wikilink support in Markdig itself.
- `src/Eru.Domain/Deps.fs` — `ExtractLinks: string -> string list` field.
- `src/Eru.Domain/LinkGraph.fs`:
  - `NodeId = InternalNode of EntryId | ExternalNode of Url: string`
  - `Edge = { From: NodeId; To: NodeId }` (no metadata today)
  - `resolveLink (source) (currentPath) (target) : NodeId option` — path-only resolution, no index-existence check, drops `#anchor`/`mailto:`, normalizes `.`/`..`.
  - `execute` reads each source's `IndexEntry` map, reads cached content, calls `deps.ExtractLinks`, resolves each raw URL via `resolveLink`, accumulates nodes/edges.
- `src/Eru.Domain/Config.fs` — `EntryId = { Source: string; RemotePath: string }`; `IndexEntry` (same file, ~line 99) includes `Title: string option`, populated from frontmatter in `Sync.fs`.
- `src/Eru.Cli/GraphCli.fs` — text/json/table/dot renderers over `BuildResult`.
- `tests/Eru.Tests/LinkGraphTests.fs` — `fakeExtractLinks` is a regex stand-in for the adapter.

## Implementation plan

### 1. `src/Eru.Domain/Deps.fs` — new shared types + signature change

Add before the `Deps` record:

```fsharp
type LinkKind =
    | MarkdownLink
    | Wikilink

type ExtractedLink = {
    Target      : string
    Description : string option
    Kind        : LinkKind
}
```

Change `Deps.ExtractLinks` from `string -> string list` to `string -> ExtractedLink list`.

`src/Eru.Adapters/AdapterDeps.fs` needs no edit — it wires `ExtractLinks = MarkdownLinkAdapter.extractLinks` by reference, so it follows the adapter's new signature automatically. Other test files stubbing `ExtractLinks = fun _ -> []` still type-check unchanged.

### 2. `src/Eru.Adapters/MarkdownLinkAdapter.fs` — parse both syntaxes

Keep one `Markdown.Parse(content, pipeline)` call, then do two passes:

**a. Existing AST walk, extended:**
- When walking blocks, if a block is a `CodeBlock` (covers `FencedCodeBlock` too), record its `Span` into an `excludedSpans: ResizeArray<int * int>`.
- When walking inlines, if an inline is `CodeInline`, record its `Span` too.
- For each `LinkInline` with a non-empty `Url`, build `{ Target = link.Url; Description = inlineToPlainText link; Kind = MarkdownLink }`, where `inlineToPlainText` is a small new recursive helper that concatenates `LiteralInline`/`CodeInline` content and recurses into other `ContainerInline`s, trimming the result and mapping `""` to `None`.

**b. New regex pass for wikilinks, run after the AST walk (so `excludedSpans` is populated):**
- Regex: `\[\[\s*([^\[\]|]+?)\s*(?:\|\s*(.+?)\s*)?\]\]` over the raw `content` string.
- Discard any match whose `Index` falls inside an `excludedSpans` range (keeps `[[...]]`-looking text inside fenced/inline code from being misparsed).
- Otherwise: `Target = group1.Trim()`, `Description = group2 trimmed if present else None`, `Kind = Wikilink`.

`extractLinks` returns `markdownLinks @ wikilinks`.

### 3. `src/Eru.Domain/LinkGraph.fs` — edge metadata + two-step wikilink resolution

- `Edge` becomes `{ From: NodeId; To: NodeId; Description: string option }` — update the one construction site in `execute`.
- Add a `TitleIndex` type and pure builder, scoped per-source:

```fsharp
type TitleIndex = { KnownPaths: Set<string>; ByKey: Map<string, string> }

let private slug (s: string) = s.Trim().ToLowerInvariant()

let private buildTitleIndex (entries: (string * IndexEntry) list) : TitleIndex =
    let knownPaths = entries |> List.map fst |> Set.ofList
    let byStem  = entries |> List.map (fun (path, _)    -> slug (Path.GetFileNameWithoutExtension path), path)
    let byTitle = entries |> List.choose (fun (path, e) -> e.Title |> Option.map (fun t -> slug t, path))
    { KnownPaths = knownPaths; ByKey = Map.ofList (byStem @ byTitle) } // titles inserted last, win on collision
```

- `resolveLink` is unchanged, used as-is for markdown links and as step 1 for wikilinks.
- Add `resolveWikilink`:

```fsharp
let resolveWikilink (source: string) (currentPath: string) (titleIndex: TitleIndex) (target: string) : NodeId option =
    match resolveLink source currentPath target with
    | Some (InternalNode id) as pathResolved when titleIndex.KnownPaths.Contains id.RemotePath ->
        pathResolved
    | pathResolved ->
        match titleIndex.ByKey.TryFind (slug target) with
        | Some remotePath -> Some (InternalNode { Source = source; RemotePath = remotePath })
        | None -> pathResolved
```

- In `execute`, build a `Map<string, TitleIndex>` from the already-loaded `entries` (grouped by source), and branch resolution by `link.Kind`:

```fsharp
deps.ExtractLinks content
|> List.choose (fun link ->
    let resolved =
        match link.Kind with
        | MarkdownLink -> resolveLink sourceName remotePath link.Target
        | Wikilink ->
            let ti = titleIndexBySource |> Map.tryFind sourceName |> Option.defaultValue { KnownPaths = Set.empty; ByKey = Map.empty }
            resolveWikilink sourceName remotePath ti link.Target
    resolved |> Option.map (fun target -> target, link.Description))
|> List.iter (fun (target, description) ->
    addNode target
    edges.Add { From = selfNode; To = target; Description = description })
```

This keeps parsing impure/adapter-side and resolution pure/domain-side — no new impure calls, just threading already-loaded `IndexEntry` data into a pure helper.

- `edgesFor` needs no change.

### 4. `tests/Eru.Tests/LinkGraphTests.fs`

- Update `fakeExtractLinks` to return `ExtractedLink list`: keep the markdown-link regex (add a capture group for link text → `Description`), add a wikilink regex `\[\[([^\]|]+)(?:\|([^\]]+))?\]\]` producing `Kind = Wikilink`.
- Update every `Edge` literal in existing tests to include `Description` (mostly `None`).
- Add test cases:
  - Wikilink parsing: `[[guide]]` (no description) and `[[guide|See the guide]]` (with description).
  - Two-step resolution: path-hit (`[[guide.md]]` resolves via step 1), stem-fallback hit (`[[guide]]` with no matching literal path but matching filename stem), title-fallback hit (target matches an `IndexEntry.Title`, not the stem), title-over-stem collision (assert title wins), both-miss permissive fallback (assert unresolved `InternalNode` still returned, not dropped).
  - `execute` end-to-end fixture mixing a markdown link, a stem-only wikilink, and an external URL, asserting final `Nodes`/`Edges`/`Description`.
- Confirm existing markdown-link tests (external, anchor-only, mailto, relative sibling, `..` escape, anchor stripping) still pass with only shape (not behavior) changes.

Add a new adapter-level test file (e.g. `tests/Eru.Tests/MarkdownLinkAdapterTests.fs`, registered in `Eru.Tests.fsproj`) exercising the real Markdig-based `extractLinks`, since the domain-level fake can't validate real parsing:
  - `[[doc]]` / `[[doc|description]]` extracted correctly.
  - `[[...]]`-looking text inside a fenced code block is **not** extracted.
  - `[[...]]`-looking text inside inline code (`` `[[x]]` ``) is **not** extracted.
  - `[description](target)` still extracts target + description, for both an internal relative path and an external `https://...` URL — assert both get `Description = Some "description"` and the external one still resolves to `ExternalNode` with that description attached on the `Edge`.
  - Mixed-syntax document extracts both kinds correctly.

### 5. `src/Eru.Cli/GraphCli.fs` (light touch)

- `renderJson`: add `description = e.Description` to the edge record, following whatever `System.Text.Json` convention this file already uses for `string option` fields (check current output shape before finalizing — no `OptionConverterFactory` is wired into the CLI's `JsonSerializerOptions` today, that's only used in `Eru.Adapters/Serialization.fs`).
- Leave `renderText`/`renderTable`/`renderDot` untouched.

## Verification

1. `dotnet build` — confirm the `Deps.ExtractLinks` signature change propagates (only `LinkGraph.fs`, adapter, and test files touch it).
2. `dotnet test`, focusing on `LinkGraphTests` and the new adapter test file — cover wikilink parsing (no-pipe/with-pipe), code-fence/inline-code exclusion, two-step resolution (path hit / stem hit / title hit / title-over-stem collision / both-miss permissive fallback), and the `execute` end-to-end mixed fixture.
3. Full `dotnet test` run to confirm the other test files stubbing `ExtractLinks = fun _ -> []` still compile and pass untouched.
4. Manual smoke test: run `eru graph --output json` against a small fixture with both link styles inside and outside code fences; eyeball the rendered edges and descriptions.

## Noted but explicitly out of scope

- Dangling/unresolved-link detection as a first-class feature — the `TitleIndex.KnownPaths` built here gives an exact existence check that a future feature could use to flag unresolved `InternalNode`s, but this round only uses it internally for step 1 verification.
- Wikilink escaping (e.g. literal `\[\[...\]\]`) is not handled.
- Plumbing `Description` into `renderText`/`renderTable`/`renderDot`.
