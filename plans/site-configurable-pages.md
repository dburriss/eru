---
status: done
---

# Plan: Configurable site (layouts, widgets, pages, filters, content, views)

## Context

The generated site is fixed. The top nav is hard-coded in `HtmlTemplates.layout`, the facet sidebar exists only
on the Browse page (`indexPage`) and doubles as the JS filter UI, there is no right nav, and every page is a
group-by of `SiteModel` (Sources, Tags, Types, Bundles) gated by the `SiteFeatures` flags in `SiteGenerator.fs`.
Every list renders as `fileCard` cards. The document page (`filePage`) is a fixed stack: breadcrumbs, title, meta
box (type, trust, description, tags, stale warning), linked-documents block, CLI tip, content.

Goals:

1. One model for every page: slots (`top`, `left`, `right`, `main`) holding ordered lists of widgets, each slot
   configurable or disabled.
2. Built-in pages become page definitions; users can drop or override them.
3. Custom pages that filter documents by type, tag, bundle, source, etc.
4. Content pages that render a Markdown file or a knowledge doc.
5. Three list views: `cards`, `list`, `table`.
6. Configurable document pages (the pages opened from a list), matched on the document.

Config v3 already has a `site` section (`ignorePatterns`, `hideEmptyBundles`), so everything here is additive keys
with no schema bump. See `plans/config-v3-sections.md` and `plans/site-bundles.md`.

## Decisions

| Area | Decision |
|---|---|
| Page model | Every page is slots of widgets: `top` (links), `left`, `right`, `main`. A slot is an ordered list or `false`. The key is `layout` (not `nav`), since slots hold page content as well as navigation. |
| Widgets | A fixed set, no plugin system. Fine-grained so layouts are controllable. Plain widget names only in the first cut; no per-widget options. |
| Slot merging | A rule or page-level `layout` replaces a slot as a whole. No per-widget merging. |
| Defaults | Refreshed. Top = built-in pages, left = `facets`, right = `toc`. Document pages open with `graph` and end with `related`. A **Changed** changelog entry documents the opt-out (`layout.left: false`, `site.cards.columns: 1`). |
| Facets widget | Plain links to pages on every page. Checkbox filtering stays Browse-only. Filter state in the URL (checkboxes everywhere) is a follow-up. |
| Built-in pages | Droppable (`site.pages.<id>: false`) and overridable by a custom page of the same id. Replaces the `SiteFeatures` flags. |
| Overrides | Only the index page is replaced. Detail pages (`tags/<tag>/`, `sources/<name>/`, ...) are still generated unless the built-in is dropped. The override keeps the built-in URL, so existing tag/source links stay valid. |
| Custom page URL | `pages/<id>/index.html`. |
| Home | `site.home` names any page id, default `browse`. |
| Content files | Repo-relative path. Must stay inside the repo root (no `..` escape) and exist. |
| Document pages | The site is static and a document has one URL, so its config cannot depend on the list it was opened from. It depends on the document: `site.document` defaults plus `rules` matched on the document (same vocabulary as `filter`). A list controls only where an item links (`open`). |
| Validation | Config and content errors fail generation with a clear message (unknown page id in layout, unknown widget, widget in an invalid slot, missing content file, uncached referenced doc, bad view or column name, widget-name clash, `cards.columns` outside 1-6, option on the wrong view). In serve mode the error is shown and the last good build stays. |
| Merge (global/local files) | `layout` merges per slot, local over global. `pages` merge by id, local over global. `false` in the local file removes a global page. `document.rules` concatenate, global first. |

## Widgets

| Widget | Shows | Valid in |
|---|---|---|
| `breadcrumbs`, `title` | Trail and heading | `main` |
| `meta` | Badges: type and status | `main`, side slots |
| `verified` | Trust tier (`unverified`, `human-reviewed`, `machine-confirmed`) plus each verifier (`by`, human or machine) with the date (`at`) when present | `main`, side slots |
| `generated` | Who or what generated the doc (`by`, human or machine) and when (`at`) | `main`, side slots |
| `description`, `tags` | Frontmatter description and tag links | `main`, side slots |
| `stale` | Past-`stale_after` warning (renders only when stale) | `main` |
| `cli` | Copyable `eru add ...` commands | `main`, side slots |
| `content` | The rendered Markdown | `main` only |
| `list` | The filtered documents in the page's view | `main` only |
| `toc` | Table of contents from headings | side slots |
| `related` | Incoming and outgoing link lists (no JS) | `main`, side slots |
| `graph` | Linked-documents visualisation. Rendered hidden and revealed by `graph.js` once it draws, so without JS only `related` shows and nothing is duplicated | `main`, side slots |
| `facets` | Source/ext/tag/type/bundle links | side slots |
| `links` | Custom links | side slots |

- `related` and `graph` are one block today (`docGraphSection`, with a fallback list inside it). They are split so a
  page can show the link lists without the graph. The graph carries no fallback list of its own: it is hidden until the script draws it, and `related` is the no-JS view.
- Trust moves out of the `meta` box into `verified`. It reads `SiteDocument.Verified` (`ActorAt = { By; At }`, `At`
  optional); the tier rule is the existing `trustTier` (no entries -> unverified; any `human:` actor ->
  human-reviewed; else machine-confirmed). With no entries it shows "unverified" and no list.
- `generated` reads `SiteDocument.Generated` (`ActorAt option`) and shows the actor and date. It renders nothing when
  the field is absent, and omits the date when `At` is absent. A `human:` or machine actor is labelled as such.
- Widgets that need data a page lacks render nothing (`toc` on a list page, `related` on a doc with no links, `stale`
  on a fresh doc). The grid collapses an empty slot instead of leaving a gap.
- Widget names are reserved. A custom page id equal to one is rejected. Built-in ids that are also widget names (`tags`) are exempt, since they override the built-in page; in a side slot the widget wins, so link the page with `{ "page": "tags" }`.

## Config shape (`site` section, both files)

```json
"site": {
  "home": "welcome",
  "defaultView": "cards",
  "cards": { "columns": 3 },
  "layout": {
    "top":   ["browse", "sources", "tags", "runbooks", { "label": "Wiki", "url": "https://..." }],
    "left":  ["facets"],
    "right": ["toc"]
  },
  "pages": {
    "welcome":  { "title": "Welcome", "content": { "file": "docs/welcome.md" }, "layout": { "left": false } },
    "ops":      { "title": "Ops", "content": { "doc": "kb:knowledge/ops/overview.md" },
                  "filter": { "bundle": ["kb/knowledge/ops"] }, "view": "cards", "cards": { "columns": 2 } },
    "needs-review": { "title": "Needs review", "filter": { "stale": true }, "view": "table",
                  "table": { "columns": ["title", "type", "bundle", "status", "verified", "staleAfter"] },
                  "sort": { "by": "staleAfter", "dir": "asc" }, "open": "resource" },
    "types":    false
  },
  "document": {
    "layout": {
      "left": false,
      "right": ["toc"],
      "main": ["graph", "breadcrumbs", "title", "meta", "verified", "generated", "description", "tags", "stale", "cli", "content", "related"]
    },
    "rules": [
      { "match": { "type": ["runbook"] },
        "layout": { "right": ["toc"], "main": ["title", "meta", "verified", "content"] } },
      { "match": { "bundle": ["kb/ops"] }, "layout": { "right": ["toc", "graph"] } }
    ]
  }
}
```

Existing keys (`ignorePatterns`, `hideEmptyBundles`) are unchanged.

### Layout slots

- Each slot is an ordered list, or `false` to disable it. Omitted means the default for the page kind.
- `top` takes pages and links. A **string** is a page id; an **object** is `{ page, label }` or `{ label, url }`.
  Groups (`{ label, items }`) are deferred.
- `left`, `right` and `main` take widget names (strings). A string in `left`/`right` may also be a page id, rendered
  as a link.
- A page may carry its own `layout` with the same slots, replacing the site-wide slot for that page only.
- Defaults `main` per page kind:

| Page kind | Default `main` |
|---|---|
| List page (browse, tags, custom) | `["title", "list"]` |
| Content page | `["title", "content"]` (`toc` on the right) |
| Document page | `["graph", "breadcrumbs", "title", "meta", "verified", "generated", "description", "tags", "stale", "cli", "content", "related"]` |

- The page grid is CSS grid regions, so a disabled slot leaves no gap.

### Pages

A page is a URL plus up to two parts, at least one required:

- **`content`**: a document or local file rendered by the `content` widget.
  - `{ "file": "<repo-relative path>" }` renders local Markdown with `MarkdownRenderer`. Links to other local files
    or docs are rewritten to site URLs, as file pages do today. Images are out of scope. `toc` is on by default for
    content pages.
  - `{ "doc": "<source>:<path>" }` renders an indexed doc at a chosen URL. It must be `cached` or `pulled`; otherwise
    generation fails.
  - Inline Markdown in config is not supported.
- **`filter`**: selects documents for the `list` widget. Fields: `source`, `bundle`, `type`, `tag`, `status`, `ext`,
  `path`, `stale`.
  - AND across fields, OR within a field's list.
  - Negation via `not: { ... }` with the same fields.
  - `path` accepts globs (`ops/**`).
  - `bundle` matches the bundle and its children by path prefix on the display name; `"exact": true` limits it to the
    named bundle. (`SiteDocument.Bundle` holds only the most specific bundle.)
  - `stale: true` matches docs past `staleAfter`.
- With both `content` and `filter`, `main` defaults to `["title", "content", "list"]`.
- `title`, `view`, `sort`, `group`, `limit` apply to the `list` widget.
- `open` says what clicking an item does: `"page"` (default, the document page), `"resource"` (the document's
  `resource` frontmatter URL) or `"source"` (the file in its source repo). Index-only docs have no page, so `"page"`
  falls back to plain text as `fileCard` does today. `site.document.open` sets the default.

Built-in ids: `browse`, `sources`, `tags`, `types`, `bundles`. A page entry with one of those ids overrides that
built-in's index page. `false` drops it, including its nav link and detail pages.

### Document pages

- `site.document.layout` sets the default slots for every document page.
- `site.document.rules` is a list of `{ match, layout }`. `match` uses the same fields as `filter` (`type`, `tag`,
  `bundle`, `source`, `path`, `status`, `stale`, `not`, globs); an empty `match` is an error.
- Precedence: built-in defaults, then `site.document.layout`, then matching rules in order. Each slot is replaced as
  a whole by the last rule that sets it.
- `content` must stay in `main`; otherwise generation fails.

### Views

| View | Renders | Options |
|---|---|---|
| `cards` (default) | Current `fileCard` | `cards.columns` (1-6) |
| `list` | One compact `<ul>` line per doc | `show` for inline meta |
| `table` | `<table>` | `table.columns`: ordered subset of `title`, `source`, `path`, `bundle`, `type`, `tags`, `status`, `verified`, `staleAfter`, `generated`, `syncStatus`, `ext` |

- Common to all: `sort` (field, direction; default `title`, docs missing the field sort last), `group` (by `tag`, `type`,
  `bundle` or `source`; headed sections; each group gets its own grid), `limit` (document count, no "more" link yet).
- Option blocks are scoped (`cards.columns` is a number, `table.columns` is a list). Setting an option on the wrong view
  is an error.
- `view` is an extensible name backed by a registered renderer, so later views (graph, timeline) need no config
  reshaping.
- `cards.columns` is a maximum: `repeat(n, minmax(<min card width>, 1fr))`, so narrow screens and crowded layouts
  (three slots on) drop to fewer columns.
- Site-wide defaults `site.defaultView` and `site.cards.columns`; a page's own settings override them.
- All three render without JS. Click-to-sort headers on tables are a JS enhancement. A reader-facing view switcher
  (one static page per view, with a link switcher) and pagination are deferred.

## Design

### Model (`Eru.Domain/Config.fs`)
- Extend `SiteSection` with `Home`, `DefaultView`, `Cards`, `Layout`, `Pages`, `Document` (all option), keeping
  `SiteSection.empty`.
- New types: `Slot = Disabled | Widgets of LayoutItem list`, `LayoutItem = Page of id * label option | Widget of name | Link of label * url`,
  `PageDef = { Title; Content; Filter; View; Sort; Group; Limit; Open; Layout; Cards; Table }`,
  `PageEntry = Dropped | Defined of PageDef`, `DocumentRule = { Match: Filter; Layout }`,
  `DocumentSection = { Layout; Open; Rules }`.
- `EffectiveConfig` gains resolved fields. Merge follows the rules above (`Config.fs` merge near `:601-638`).
- JSON: a slot is `false | array`; a page is `false | object`; layout items are `string | object`. Needs custom
  converters or a JsonElement shim, since System.Text.Json has no union support.

### Site (`Eru.Site`)
- **Page abstraction.** Replace the hard-coded generation blocks (`SiteGenerator.fs:1062-1088`) with a list of resolved
  pages: built-ins from defaults, merged with config, then custom pages. Built-ins keep their detail-page generators.
- **Filter engine.** Pure function `SiteDocument list -> Filter -> SiteDocument list` in `Eru.Site`, shared by custom
  pages and document rules, easy to unit test.
- **Layout and widgets.** `HtmlTemplates.layout` takes resolved slots and renders the grid regions. Each widget is a
  small render function `WidgetContext -> string` registered by name; the registry also records the slots each widget
  is valid in (validation reads it). Top nav items come from config rather than the hard-coded list
  (`HtmlTemplates.fs:99-103`). `filePage` is rebuilt from the widget registry; its pieces (`breadcrumbs`,
  `docGraphSection`, `tagList`, trust badge, stale warning, `cliTips`) become widgets. `facets` generalises the
  `indexPage` sidebar; `toc` is built from rendered headings; `related` and `graph` come from splitting
  `docGraphSection` (`plans/site-doc-graph.md`); `verified` renders tier plus `by`/`at` per entry; `generated` renders `by`/`at`.
- **Views.** Replace direct `fileCard` use with a `renderDocs view docs` function. Every view emits the filter
  contract below, so `app.js` knows nothing about cards, lists or tables.
- **Declarative filter contract.** The markup declares what is filterable and `app.js` is generic. Today it selects
  `.file-card` and calls `replaceLinksWithCheckboxes` per hard-coded list id (`source-filters`, `ext-filters`,
  `tag-filters`), which would break for list and table rows. Replace with:
  - `data-doc` marks any element (card, list item, table row) as a filterable document. The JS selects `[data-doc]`.
  - `data-<facet>` holds that element's values per facet: `data-source`, `data-ext`, `data-tags` (already emitted),
    plus new `data-type`, `data-bundle`, `data-status`.
  - `data-facet="<name>"` on a sidebar list marks it as the filter for that facet, replacing the hard-coded ids. The
    JS builds its filter state from whichever `data-facet` lists exist, so a new facet needs markup only. Types and
    bundles become checkbox facets on Browse this way.
  - Matching rule, stated once: OR within a facet, AND across facets, search over the element's text. Same behaviour
    as today.
  - `data-doc-group` marks a group section (grouped views). The JS hides a group with no visible `[data-doc]`.
  - `data-doc-list` (container) and `data-doc-count` (result count) replace the `#file-list` and `#file-count` ids.
  - Facet values in `data-<facet>` need a separator that cannot occur in a value. Space-joined `data-tags` splits a tag
    containing a space, so use a pipe or a JSON array.
  - Without JS nothing changes: the sidebar stays plain links and JS upgrades them to checkboxes.
- **Content.** Read and render local files (path check against the repo root), resolve doc references, rewrite links.
- **Search JSON** is unchanged; custom pages are static HTML and search stays global.
- **Serve.** `eru site serve` also watches referenced local content files and rebuilds on change.

## Phasing

1. **Layout, rendering and widgets.** Parse `site.layout`; the widget registry and grid regions with collapsing;
   refreshed defaults; disable support; widgets `facets`, `toc`, `related`, `graph`, `verified`, `generated` and the document-page
   widgets rebuilt from `filePage`; `site.defaultView`/`cards.columns` with the `cards` view only. Changelog
   **Changed** entry.
2. **Built-ins as page definitions.** Behaviour-preserving refactor to the page abstraction; `false` drops a built-in;
   `site.home`; remove the `SiteFeatures` flags.
3. **Custom pages, filters, views and document rules.** `site.pages`, the filter engine, `list` and `table` views,
   `sort`/`group`/`limit`, `open`, overrides, per-page `layout`, `site.document` with `rules`, validation.
4. **Content pages.** `content.file` and `content.doc`, link rewriting, serve-mode watching.

## Tests (xUnit v3)

- Config: parse and round-trip layout (`false`/array/mixed items), pages (`false`/object) and `document` rules; per-slot
  and per-id merge incl. local `false` removing a global page; widget-name clash; unknown page id or widget in layout;
  widget in an invalid slot (`content` in `right`, `toc` in `main`); empty rule `match`; bad view/column/columns range.
- Filter engine: AND/OR, `not`, path glob, bundle prefix vs `exact`, `stale`, empty result.
- Document rules: precedence (defaults, `site.document.layout`, rules in order); a rule replaces a slot whole; `content`
  missing from `main` fails; two docs with different types get different layouts.
- Widgets: `verified` shows tier plus `by` and `at` for human and machine entries, omits the date when `at` is absent,
  shows "unverified" with no entries; `stale` and `toc` render nothing when empty; `related` without `graph` has no
  graph element; `generated` shows actor and date, omits the date when `at` is absent, and renders nothing when the
  field is missing.
- Generator (pattern: `SiteGeneratorGraphTests.fs`): no config renders the refreshed defaults; disabled slots leave no
  region; custom page lands at `pages/<id>/index.html`; override keeps the built-in URL and detail pages; dropped
  built-in has no page or nav link; `site.home` produces the right `index.html`; relative asset prefixes at depth;
  `open` produces the right link target and falls back to text for index-only docs.
- Views: cards grid CSS for `columns`; list and table markup; each view emits `data-doc` plus the facet attributes;
  Browse facets and search work for every view; a group with no visible documents is hidden; `data-facet` lists
  (including type and bundle) become checkboxes without per-facet JS.
- Content: local file renders; `..` escape rejected; missing file and uncached doc fail with a clear error; local links rewritten.

## Docs and housekeeping

- `docs/reference/config-file.md` (new `site` keys), `docs/reference/site-generation.md` (layout, widgets, pages,
  views, document pages), `docs/how-to/customize-the-generated-site.md` (layout, custom pages, content pages, opting
  out of the new defaults), plus a new how-to for building custom pages and one for configuring document pages.
- `CHANGELOG.md` `[Unreleased]`: **Added** (layout slots, widgets, custom pages, content pages, views, document rules)
  and **Changed** (refreshed defaults, document page rebuilt from widgets, `SiteFeatures` flags removed in favour of
  `site.pages.<id>: false`).

## Deferred

- Checkbox facet filtering on every page via URL filter state.
- Layout groups in `top`, reader-facing view switcher, pagination, a "more" link on capped lists.
- Per-widget options (object form such as `{ "widget": "meta", "show": [...] }`), added when a concrete need appears.
- Images in local content, inline Markdown content.
- Computed columns such as incoming link count.
