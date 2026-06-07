---
status: proposed
---

# Plan: Wire `documents.json` into site search

## Context

`eru site generate` already writes `data/documents.json` (full document list: title,
description, body, tags, source, extension, status, pageUrl) and `data/sources.json`
when `Features.Search = true`. However `js/app.js` never fetches these files — search
is implemented as a plain substring match against each `.file-card` DOM node's
`textContent`.

The DOM approach only searches what is visually rendered in the card (title, description
snippet ≤120 chars, tags). The `body` field in `documents.json` contains the first ~500
chars of each document's content, which is never searched.

This plan replaces the DOM text search with a JSON-backed search, while keeping the
existing facet filtering (source / extension / tags) unchanged.

---

## Problem: data root URL varies by page depth

`app.js` is a single shared file at `js/app.js`. Pages sit at different depths:

| Page | Depth | Prefix to reach root |
|---|---|---|
| `index.html` | 0 | `./` |
| `sources/index.html` | 1 | `../` |
| `sources/<name>/index.html` | 2 | `../../` |
| `tags/index.html` | 1 | `../` |
| `tags/<tag>/index.html` | 2 | `../../` |
| `files/<source>/<slug>.html` | 2 | `../../` |

`fetch()` URLs are resolved relative to the **document** (the page), not the script.
So `app.js` cannot hard-code a path to `data/documents.json`.

**Solution:** each page's layout includes an inline config script before `app.js`:

```html
<script>window.ERU_DATA_ROOT = "../../data/";</script>
<script src="../../js/app.js"></script>
```

`app.js` reads `window.ERU_DATA_ROOT` and constructs the fetch URL at runtime.

---

## Search algorithm

No external library. The existing `body` field gives enough content for a simple
tokenised match that is significantly better than the current DOM approach.

**Index build (once, on first search or DOMContentLoaded):**

1. `fetch(window.ERU_DATA_ROOT + 'documents.json')`
2. For each document, build a normalised searchable string:
   `[title, description, body, ...tags].filter(Boolean).join(' ').toLowerCase()`
3. Store as `{id, searchable, doc}` in a module-level array.

**Query (each keystroke):**

- Split query on whitespace → terms
- A document matches if every term appears in its `searchable` string (AND semantics,
  consistent with MCP `search_knowledge`)
- Collect matching `id` values into a `Set`
- In `applyFilters`, add an `idOk = jsonIndex === null || matchingIds.has(card.dataset.id)` guard
  (falls back to no-id-filter when JSON hasn't loaded yet)

**Fallback:** if `fetch` fails (network error, `file://` on Firefox, `Search` feature
disabled), `jsonIndex` stays `null` and `applyFilters` falls back to the current DOM
`textContent` substring match so nothing breaks.

---

## Changes

### `src/Eru.Site/HtmlTemplates.fs`

Add `dataRoot` parameter to `layout`:

```fsharp
let layout (depth: int) (title: string) (body: string) : string =
    let p = prefixFor depth
    // existing...
    // Add before </body>:
    // <script>window.ERU_DATA_ROOT = "{p}data/";</script>
    // <script src="{p}js/app.js"></script>
```

All callers already pass `depth`; no call-site changes needed.

### `src/Eru.Site/SiteGenerator.fs` — `appJs`

Replace the text search section of `applyFilters` and add JSON loading:

**Additions to module-level state:**
```js
var jsonIndex = null;   // null = not loaded yet, [] = loaded (may be empty)
var matchingIds = null; // Set of doc ids matching current query, null when no query
```

**New `loadJsonIndex()` function:**
```js
function loadJsonIndex() {
  if (!window.ERU_DATA_ROOT) return;
  fetch(window.ERU_DATA_ROOT + 'documents.json')
    .then(function(r) { return r.json(); })
    .then(function(docs) {
      jsonIndex = docs.map(function(d) {
        var parts = [d.title, d.description, d.body].concat(d.tags || []);
        return { id: d.id, s: parts.filter(Boolean).join(' ').toLowerCase() };
      });
      applyFilters();   // re-run with the index now available
    })
    .catch(function() { jsonIndex = []; });  // fetch failed — DOM fallback stays
}
```

**Modified `applyFilters()`:**
- When `q` is non-empty and `jsonIndex` is non-null (loaded):
  - Compute `matchingIds` as a `Set` of ids where every term matches `entry.s`
  - `textOk = matchingIds.has(card.dataset.id)`
- When `q` is non-empty and `jsonIndex` is null (still loading):
  - `textOk = card.textContent.toLowerCase().indexOf(q) >= 0` (current behaviour)
- When `q` is empty: `textOk = true`

**On `DOMContentLoaded`:** call `loadJsonIndex()` after wiring up the checkboxes.

### `src/Eru.Site/HtmlTemplates.fs` — `fileCard`

Add `data-id` attribute to each card so `applyFilters` can match against `matchingIds`:

```html
<article class="file-card"
         data-id="{escapeHtml doc.Id}"
         data-source="..."
         data-ext="..."
         data-tags="...">
```

`doc.Id` is already `"<sourceName>:<remotePath>"`, matching `d.id` in `documents.json`.

---

## Files to change

| File | Change |
|---|---|
| `src/Eru.Site/HtmlTemplates.fs` | Pass `data-id` on `.file-card`; inject `window.ERU_DATA_ROOT` inline script in `layout` |
| `src/Eru.Site/SiteGenerator.fs` | Rewrite `appJs` search section to load JSON and use id-set matching |

No changes to `IndexBuilder.fs`, `SiteModel.fs`, or CLI files. `documents.json` is
already being written correctly.

---

## Verification

```bash
dotnet build
dotnet test

# Generate and open
dotnet run --project src/Eru -- site generate --open

# Checks:
# - Typing a word present only in a document body (not its visible snippet) returns that card
# - Typing a word not in any document yields an empty list
# - Sidebar checkboxes still filter correctly independent of search
# - Open index.html directly via file:// in Chrome — search works (fetch succeeds on Chrome)
# - Open via file:// in Firefox — falls back to DOM search gracefully (no error in console)
# - Repeat search on a source/<name>/index.html and tags/<tag>/index.html page to confirm
#   ERU_DATA_ROOT resolves correctly from deeper pages
```
