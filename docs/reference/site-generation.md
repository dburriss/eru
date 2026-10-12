---
title: Site generation reference
type: reference
tags: [site, static-site, layout]
---

# Site generation reference

`eru site generate` and `eru site serve` produce a fully self-contained static HTML site for browsing and
searching your local knowledge cache. See [`eru site` in the CLI reference](cli.md#eru-site) for flags, and
[customize the generated site](../how-to/customize-the-generated-site.md) for theming.

## Generated layout

```
<output-dir>/
├── index.html                    # The home page (the Browse list unless `site.home` names another page)
├── browse/
│   └── index.html                # Browse, when `site.home` names a different page
├── pages/
│   └── <id>/
│       └── index.html            # A custom page from `site.pages` (not the home page)
├── sources/
│   ├── index.html                # All sources with manifest indicator and file counts
│   └── <name>/
│       └── index.html            # Files for one source
├── tags/
│   ├── index.html                # All tags with file counts
│   └── <tag>/
│       └── index.html            # Files carrying a given tag
├── types/
│   ├── index.html                # All document types with file counts
│   └── <type>/
│       └── index.html            # Files of a given type
├── bundles/
│   ├── index.html                # All registered bundles with kind and file counts
│   └── <source>/
│       ├── index.html            # Files in the source's root bundle
│       └── <path>/
│           └── index.html        # Files in a nested bundle (path segments become folders)
├── files/
│   └── <source>/
│       └── <slug>.html           # Rendered content page (cached/pulled .md files only)
├── assets/
│   └── css/
│       └── style.css             # Base stylesheet (dark mode via prefers-color-scheme)
├── js/
│   ├── app.js                    # Checkbox filtering, in-place search, sortable tables
│   └── theme.js                  # Manual dark/light theme toggle
└── data/
    ├── documents.json            # Full document list for client-side search
    ├── sources.json              # Source list with manifest flags
    └── manifest.json            # Schema version and document count
```

### Bundles

The **Bundles** tab lists every bundle registered on a source (see `eru source bundle list`), including ones with no
files yet. A bundle is named `<source>` for the repo root and `<source>/<path>` otherwise, so nested bundles carry
their parents' path. Each file is listed under its **most specific** bundle (the longest path covering it), never
also under an enclosing bundle. A bundle with no listed files still appears, with a count of 0, so a newly registered bundle is visible before its first
sync. Set `site.hideEmptyBundles` to `true` in the config to omit those (see the
[config reference](config-file.md#settings-sections)). Files covered by no bundle appear in no bundle page but remain in Browse, Sources,
Tags and Types. Register nested bundles with `eru source add --scan --nested` or `eru source bundle add`.

Every page is fully navigable as plain HTML with no JavaScript. JS is loaded as an optional enhancement that adds
in-place search, checkbox facet filtering and sortable table headers. Dark/light theming works via CSS `prefers-color-scheme` even without
JS; the theme toggle button is JS-only.

## Pages

Every page is built from the same model: a URL, up to two parts (a **content** source and a **filter**), and four
**slots** of widgets. The built-in pages are page definitions you can drop or override.

| Built-in id | URL | Shows |
|---|---|---|
| `browse` | `index.html` (`browse/index.html` when it is not the home page) | Every document, with the filter sidebar and search |
| `sources` | `sources/index.html` | Source cards; `sources/<name>/` lists one source's files |
| `tags` | `tags/index.html` | Tag list; `tags/<tag>/` lists one tag's files |
| `types` | `types/index.html` | Type list; `types/<type>/` lists one type's files |
| `bundles` | `bundles/index.html` | Bundle list; `bundles/<source>/<path>/` lists one bundle's files |

- **Drop** a built-in with `"pages": { "types": false }`. Its index page, detail pages and nav link all go, and
  links that would have pointed at them (tag chips, facet links, breadcrumbs) fall back to plain text.
- **Override** a built-in with a page of the same id. Only the index page is replaced; it keeps the built-in URL,
  so existing links stay valid, and the detail pages (`tags/<tag>/`, ...) are still generated.
- **Custom pages** live at `pages/<id>/index.html`. An id may contain letters, digits, `-`, `_` and `.`, and must
  not be a widget name (the built-in ids `tags` and so on are exempt because they override a built-in).
- **Home**: `site.home` names the page written to `index.html`, default `browse`. If `browse` is dropped,
  `site.home` is required.
- Custom pages are appended to the default top nav after the built-ins; list `site.layout.top` to choose the
  order yourself.

A page definition takes these keys (at least one of `content` and `filter` is required):

| Key | Meaning |
|---|---|
| `title` | Heading and default nav label (default: the id) |
| `content` | `{ "file": "<repo-relative .md path>" }` or `{ "doc": "<source>:<path>" }` |
| `filter` | Selects documents for the `list` widget — see [filters](#filters) |
| `view`, `cards`, `table`, `list` | How the list renders — see [views](#views) |
| `sort` | `{ "by": "<field>", "dir": "asc" \| "desc" }`, default `title` ascending; documents missing the field sort last |
| `group` | Headed sections by `tag`, `type`, `bundle` or `source`; each group gets its own grid, list or table |
| `limit` | Maximum number of documents (after sorting) |
| `open` | What a list item links to: `page` (default), `resource` (the `resource` frontmatter URL) or `source` (the file in its source repo). Falls back to the document page, then to plain text for index-only documents |
| `layout` | Replaces slots for this page only |

### Content pages

- `content.file` renders a local Markdown file with the same renderer as document pages. The path is relative to the
  repo root and must stay inside it (no `..` escape, not absolute). Links in the file to another content page, a
  pulled local file or an indexed document (`<source>:<path>` or a relative path) become site URLs; other links are
  left alone. Images are not copied. A `toc` shows on the right.
- `content.doc` renders an indexed document at a URL you choose. It must be `cached` or `pulled`.
- With both `content` and `filter`, the default `main` is `title`, `content`, `list`.
- Inline Markdown in config is not supported.
- `eru site serve` watches the referenced local files and rebuilds when they change.

### Filters

`filter` (and a document rule's `match`) take these fields: `source`, `bundle`, `type`, `tag`, `status`, `ext`,
`path` and `stale`. Each field takes a string or an array. Fields are ANDed; the values within a field are ORed.

| Field | Matches |
|---|---|
| `source`, `type`, `tag`, `status` | The document's source, `type`, any tag, or OKF lifecycle `status` (case-insensitive) |
| `bundle` | The bundle and its children, by path prefix on the display name (`kb/ops` matches `kb/ops/db`, not `kb/operations`). `"exact": true` limits it to the named bundle |
| `ext` | File extension, with or without the dot |
| `path` | Gitignore-style globs on the remote path (`ops/**`, `*.txt`) |
| `stale` | `true` for documents past `stale_after`, `false` for the rest |
| `not` | A nested filter with the same fields; matching documents are excluded |

`"filter": {}` matches every document.

## Layout slots and widgets

Each page has four slots, each an ordered list or `false` (disabled):

| Slot | Takes | Default |
|---|---|---|
| `top` | Page ids and links: `"tags"`, `{ "page": "tags", "label": "Labels" }`, `{ "label": "Wiki", "url": "https://..." }` | Every page that exists |
| `left` | Widget names, page ids, links | `facets` |
| `right` | Widget names, page ids, links | `toc` |
| `main` | Widget names | Depends on the page kind, below |

An empty slot, or one whose widgets all render nothing, leaves no gap: the page grid drops the region.

Precedence, last wins, each slot replaced **as a whole** (no per-widget merging): built-in defaults for the page
kind, then `site.layout`, then the page's own `layout`. `site.layout` takes `top`, `left` and `right` only; `main`
is set per page or in `site.document`.

A string in `left` or `right` is a widget name when one matches; otherwise it is a page id rendered as a link. For
the built-in `tags` page, which shares its name with a widget, write `{ "page": "tags" }`.

| Page kind | Default `main` |
|---|---|
| List page (browse, tags, custom) | `title`, `list` |
| Detail page (`tags/<tag>/`, ...) | `breadcrumbs`, `title`, `cli`, `list` |
| Content page | `title`, `content` |
| Document page | `graph`, `breadcrumbs`, `title`, `meta`, `verified`, `generated`, `description`, `tags`, `stale`, `cli`, `content`, `related` |

| Widget | Shows | Valid in |
|---|---|---|
| `breadcrumbs`, `title` | Trail and heading | `main` |
| `meta` | Type and lifecycle status badges | `main`, side slots |
| `verified` | Trust tier (`unverified`, `human-reviewed`, `machine-confirmed`) plus each verifier (`by`, human or machine, with `at` when present) | `main`, side slots |
| `generated` | Who or what generated the document (`by`) and when (`at`); nothing when the field is absent | `main`, side slots |
| `description`, `tags` | Frontmatter description and tag links | `main`, side slots |
| `stale` | Warning, only when past `stale_after` | `main` |
| `cli` | Copyable `eru add ...` commands | `main`, side slots |
| `content` | The rendered Markdown | `main` |
| `list` | The page's filtered documents, in its view, with a search box | `main` |
| `toc` | Table of contents from the content's headings | side slots |
| `related` | Incoming and outgoing link lists (no JS) | `main`, side slots |
| `graph` | Linked-documents visualisation. Hidden until JS draws it, so it needs `related` for readers without JS | `main`, side slots |
| `facets` | Source, extension, tag, type and bundle lists | side slots |
| `links` | The custom `{ "label", "url" }` links from `top` | side slots |

Widgets that need data the page lacks render nothing (`toc` on a list page, `related` on a document with no links,
`stale` on a fresh document). The `facets` widget is plain links everywhere; on Browse (and an override of it) the
script upgrades them to checkbox filters, and the extension list appears only there.

## Views

| View | Renders | Options |
|---|---|---|
| `cards` (default) | One card per document | `cards.columns` (1-6, default 3) — a maximum, so narrow screens and crowded layouts show fewer |
| `list` | One compact line per document | `list.show`: inline meta fields, default `source`, `type`, `status` |
| `table` | A `<table>` | `table.columns`: ordered subset of `title`, `source`, `path`, `bundle`, `type`, `tags`, `status`, `verified`, `staleAfter`, `generated`, `syncStatus`, `ext` (default `title`, `source`, `type`, `tags`, `status`) |

`site.defaultView` and `site.cards.columns` set the site-wide defaults; a page's own settings override them. An
option block for a different view than the page uses (for example `cards` on a `table` page) is an error. All three
views work without JS; click-to-sort table headers are a JS enhancement.

### The filter contract

Every view marks each document with `data-doc` and one `data-<facet>` attribute per facet (`source`, `ext`,
`tags`, `type`, `bundle`, `status`), each a JSON array of values so a value containing a space or pipe stays one
value. A facet list in the sidebar carries `data-facet="<name>"`; `app.js` builds its filter state from whichever
lists exist, so it knows nothing about cards, lists or tables. Matching is OR within a facet, AND across facets,
plus a text search. `data-doc-group` sections hide when none of their documents is visible, and `data-doc-count`
receives the visible count.

## Document pages

A document page is the page opened from a list. The site is static and a document has one URL, so its layout
cannot depend on the list it was opened from: it depends on the document.

Precedence: built-in defaults, then `site.layout`, then `site.document.layout`, then each matching entry of
`site.document.rules`, in order. A rule is `{ "match": { ... }, "layout": { ... } }`; `match` uses the
[filter](#filters) fields and cannot be empty. A later rule replaces a slot set by an earlier one. The `main` slot
must keep the `content` widget, in `site.document.layout` and in every rule. `site.document.open` sets the default
`open` target for lists.

## Configuration errors

Config and content problems fail the run with a message naming the key path: an unknown page id or widget, a
widget in a slot it is not valid in, a custom page id equal to a widget name, a bad view, column or sort field,
`cards.columns` outside 1-6, an option block for the wrong view, an empty rule `match`, a document `main` without
`content`, a missing content file, a path escaping the repo root, and an uncached `content.doc`. Nothing is
written when a page cannot be built; in `eru site serve` the error is printed and the last good build keeps being
served.

When both config files set `site` keys, `layout` merges per slot (local over global), `pages` merge by id (local
over global; a local `false` removes a global page) and `document.rules` concatenate, global first.

## File statuses

Each file in the index carries one of three statuses:

| Status | Meaning |
|---|---|
| `pulled` | File is tracked in `.eru/eru.lock` and present on disk |
| `cached` | File is in the local content cache but not checked out locally |
| `index-only` | Metadata only — file has not been cached or pulled |

Only `cached` and `pulled` files get rendered content pages and appear in search.

## `eru site serve` endpoints

In addition to the static site files, the server exposes:

| Endpoint | Description |
|---|---|
| `GET /api/search?q=<terms>` | Full-text search returning JSON `{ hits: [...] }` |
| `GET /api/sync` | Trigger an immediate background sync + site rebuild (returns 202) |
| `GET /api/events` | SSE stream — sends `data: rebuild` after every successful sync |

The browser connects to `/api/events` automatically and reloads the page on each `rebuild` event.

## Progressive enhancement summary

| Feature | Works without JS | Requires JS |
|---|---|---|
| Browse all files | ✓ | — |
| Navigate by source | ✓ | — |
| Navigate by tag | ✓ | — |
| Read rendered `.md` pages | ✓ | — |
| In-place search | — | ✓ |
| Checkbox facet filtering | — | ✓ |
| Custom pages, content pages, all three views | ✓ | — |
| Click-to-sort table headers | — | ✓ |
| Manual dark/light toggle | — | ✓ |
| Dark mode via OS setting | ✓ | — |
