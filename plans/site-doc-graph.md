---
status: proposed
---

# Plan: Per-document link graph on the generated site

## Context

`eru` already builds a cross-document link graph (`src/Eru.Domain/LinkGraph.fs`, exposed
via the `eru graph` CLI) but it's never surfaced in the generated static site. `todo.md`
has an open item "Show graph on site".

The goal: when viewing a document's page on the generated site, show a small diagram of
that document's immediate neighborhood in the link graph — documents that link *to* it
(backlinks/incoming) and documents it links *to* (outgoing) — with the focused document,
incoming docs, and outgoing docs all visually distinct from one another. The diagram must
be interactive: hovering a node/edge shows its description as a tooltip, and clicking a
node navigates to that document's page.

Scope is deliberately the **per-document neighborhood view** only (not a whole-site graph
explorer page) — that's what was asked for, and it reuses the existing per-page rendering
path (`HtmlTemplates.filePage`) cleanly.

## Approach

Reuse `LinkGraph.execute` + `LinkGraph.edgesFor` (already implement exactly
"incoming"/"outgoing" for a node). Compute the graph once per site build in
`SiteGenerator.generate`, write it as `data/graph.json` (same fetch/fallback pattern as
`data/documents.json`), and add a small vanilla-JS SVG renderer (`js/graph.js`) that draws
the ego-network for the current page's document. Server-render a plain-list fallback in
the page HTML so the feature degrades gracefully with JS disabled or in `file://` static
mode, matching the project's existing "no build pipeline, embedded strings, graceful
degrade" style (see `appJs`'s `file:` protocol checks).

No new external JS libraries — hand-rolled two-column SVG layout (focus node centered,
incoming column on the left, outgoing column on the right), consistent with the rest of
the site's zero-dependency approach.

---

## Changes

### `src/Eru.Site/SiteModel.fs`

Add a small related-links model used by `filePage` for the no-JS fallback list:

```fsharp
type RelatedDoc = { Id: string; Title: string; PageUrl: string option; IsExternal: bool }
type RelatedLinks = { Incoming: RelatedDoc list; Outgoing: RelatedDoc list }
```

### `src/Eru.Site/SiteGenerator.fs`

- Add `Graph: bool` to `SiteFeatures` (default `true`).
- In `generate`, after building `model`, call `LinkGraph.execute deps { SourceFilter = None }`.
  Treat `Error` like the existing `modelResult` error path (propagate).
- Build `docsById: Map<string, SiteDocument>` from `model.Documents` (keyed by `doc.Id`,
  which already equals `EntryId.toString`).
- Add DTOs + serialize `data/graph.json` (gated on `opts.Features.Graph`):
  ```fsharp
  type GraphNodeDto = { id: string; kind: string; title: string; description: string option; pageUrl: string option }
  type GraphEdgeDto = { from: string; ``to``: string; description: string option }
  ```
  `kind` is `"internal"` or `"external"`. Title/description/pageUrl for internal nodes
  come from `docsById` lookup (`doc.Title`, `doc.Description`, `doc.PageUrl`); fall back to
  the filename (`Path.GetFileName`) with `description = None` and `pageUrl = None` for
  internal nodes with no matching document (dangling links). External nodes:
  `title = url`, `description = None`, `pageUrl = None`. Edge `description` is carried
  straight through from `LinkGraph.Edge.Description` (the link's markdown description/title
  text) — this is what the hover tooltip shows for an edge.
- Add a `relatedFor (doc: SiteDocument) : RelatedLinks` helper using
  `LinkGraph.edgesFor graphResult (InternalNode { Source = doc.Source; RemotePath = doc.RemotePath })`,
  mapping each `NodeId` through the same `docsById` lookup as above into `RelatedDoc`.
- In the file-page loop, compute `related = relatedFor doc` and pass it into
  `HtmlTemplates.filePage doc htmlContent related`.
- Add `graphJs` (new embedded JS string, same style as `appJs`/`themeJs`) and write it to
  `js/graph.js` when `opts.Features.Graph` is true. Reference it unconditionally from
  `layout` (same pattern already used for `theme.js`).
- Add graph-related CSS to the existing `css` string: `.doc-graph`, `.doc-graph-fallback`,
  `.doc-graph-legend`, `.doc-graph-tooltip` (small floating card, matches
  `.cli-tip`/`.doc-meta` surface styling), plus new theme tokens
  `--color-graph-incoming` / `--color-graph-outgoing` (reuse the existing green/amber
  badge palette) alongside `--color-primary` (focus) and a neutral/dashed style for
  external nodes — defined in both the light `:root` and the
  `prefers-color-scheme: dark` / `body.theme-dark` blocks, following the existing token
  pattern. Nodes/edges get `cursor: pointer` where clickable/hoverable.

### `src/Eru.Site/HtmlTemplates.fs`

- `filePage` signature becomes
  `filePage (doc: SiteDocument) (contentHtml: string) (related: RelatedLinks) : string`.
- Add a "Linked documents" section rendered right after `metaBox` (before the CLI tip /
  markdown body):
  - A container `<div id="doc-graph" data-node-id="{escapeHtml doc.Id}">` that JS will
    replace with an SVG diagram.
  - Inside it by default (server-rendered fallback, works with no JS): two labeled
    lists — "Links to this document" (`related.Incoming`) and "Links from this document"
    (`related.Outgoing`) — each item an `<a>` to `PageUrl` when present, else plain
    (muted) text; external items get a `target="_blank" rel="noopener"` link to the raw
    URL. If both lists are empty, render nothing (omit the whole section).
  - A small static legend describing the three node kinds (focus / incoming / outgoing),
    shown once JS renders the SVG.

### `js/graph.js` (new embedded string in `SiteGenerator.fs`, written alongside `theme.js`/`app.js`)

- On `DOMContentLoaded`, find `#doc-graph`; bail if absent (non-file pages) or if `file:`
  protocol (keep server-rendered fallback list).
- Fetch `window.ERU_DATA_ROOT + 'graph.json'` (same root global already set in `layout`);
  on any failure, silently keep the fallback list.
- From the fetched graph, filter edges touching `data-node-id`, split into
  incoming/outgoing neighbor node lists (dedup by id).
- If there are no neighbors, leave the fallback text as-is.
- Otherwise build an inline SVG: focus node centered vertically, incoming nodes in a left
  column each with an arrow edge pointing into the focus node, outgoing nodes in a right
  column with arrows pointing out from the focus node. Node color/shape rules:
  - Focus node: larger, filled with `--color-primary`.
  - Incoming (backlinks): filled with `--color-graph-incoming` (green, matches "pulled"
    badge tone).
  - Outgoing: filled with `--color-graph-outgoing` (amber, matches "cached" badge tone).
  - External nodes: rendered as a dashed rounded rect instead of a circle, regardless of
    incoming/outgoing, and link out with `target="_blank"`.
  - Internal nodes with a `pageUrl` are wrapped in an `<a>` (href = site-root prefix,
    derived by stripping the trailing `data/` off `window.ERU_DATA_ROOT`, + `pageUrl`) so
    clicking navigates to that document; internal nodes without a `pageUrl` (dangling
    links) render muted/non-clickable.
  - Colors are read via `getComputedStyle` on the CSS custom properties above, so the
    diagram automatically follows the light/dark theme toggle.
- **Interactivity:**
  - *Click*: handled by wrapping clickable nodes in `<a href>` as above (native
    navigation, no JS click handler needed); external nodes link out via
    `target="_blank" rel="noopener"`.
  - *Hover*: a single reusable tooltip `<div>` (absolutely positioned, appended once to
    `#doc-graph`) is shown/positioned on `mouseenter`/`mousemove`/`focus` of a node or
    edge and hidden on `mouseleave`/`blur`:
    - Hovering a node shows its `title` plus `description` (when present) — for the
      focus node, its own document description; for neighbor nodes, their description
      if the linked document declares one, otherwise just the title/URL.
    - Hovering an edge (the connecting line) shows the edge's `description` (the link
      text from the source markdown) when present, else the neighbor node's title as a
      minimal fallback so hover never shows an empty tooltip.
    - Nodes/edges get `tabindex="0"` plus the same hover handlers wired to
      `focus`/`blur` so the tooltip is also reachable via keyboard, not just mouse.
- Replace the container's fallback content with the rendered `<svg>` + tooltip `<div>` +
  legend.

---

## Files to change

| File | Change |
|---|---|
| `src/Eru.Site/SiteModel.fs` | Add `RelatedDoc` / `RelatedLinks` types |
| `src/Eru.Site/SiteGenerator.fs` | Compute graph, write `data/graph.json`, add `graphJs`, add graph CSS + theme tokens, add `Graph` feature flag, pass `related` into `filePage` |
| `src/Eru.Site/HtmlTemplates.fs` | `filePage` gains `related` parameter and renders the graph container + fallback list |
| `tests/Eru.Tests/HtmlTemplatesTests.fs` | Update `filePage` call sites for new parameter |

No changes to `IndexBuilder.fs`, `LinkGraph.fs`, or CLI files — the graph model and
`eru graph` command are reused as-is.

---

## Verification

```bash
dotnet build
dotnet test

dotnet run --project src/Eru -- site generate
```

- Inspect `cache-site/data/graph.json` for correct node/edge shape.
- Open a generated `files/<source>/<slug>.html` page for a document with both inbound and
  outbound links (or run `eru site serve`):
  - SVG shows the focused document centered, incoming/outgoing neighbors visually distinct.
  - Dark/light theme toggle updates the diagram colors.
  - Hovering a node shows a tooltip with its title/description.
  - Hovering an edge shows the link description.
  - Clicking a neighbor node navigates to that document's page.
- Check a document with no links renders no graph section.
- Check with JS disabled (or via `file://` open) that the plain-text fallback list still
  renders (with plain, non-hoverable links).
- Repeat existing `HtmlTemplatesTests` pass after signature update.
