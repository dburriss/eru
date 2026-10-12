# Customising the generated site

Reference for changing how `eru site generate` / `eru site serve` look and what pages they build. All structure
lives under the `site` key of `.eru/config.json` (or `~/.config/eru/config.json`). Local config merges over global:
`layout` per slot, `pages` by id (a local `false` removes a global page), `document.rules` concatenate (global first).

Config errors stop generation with the key path (e.g. `site.pages.runbooks.view: unknown view 'grid'`); in
`eru site serve` the last good build keeps being served.

Full docs (repo `dburriss/eru`, `main` branch): https://github.com/dburriss/eru/blob/main/docs/reference/site-generation.md,
https://github.com/dburriss/eru/blob/main/docs/how-to/customize-the-generated-site.md,
https://github.com/dburriss/eru/blob/main/docs/how-to/build-custom-site-pages.md and
https://github.com/dburriss/eru/blob/main/docs/how-to/configure-document-pages.md. This file is self-contained;
the links are for extra detail only.

## Theming (CSS)

| File | Behaviour |
|---|---|
| `assets/css/style.css` | Regenerated every run, never edit |
| `assets/css/custom.css` | Created once blank, never overwritten; loads after `style.css` |

`--custom-css <path>` copies a theme file you maintain elsewhere into the output on every run.

CSS variables: `--color-primary`, `--color-text`, `--color-bg`, `--color-surface`, `--color-border`,
`--color-badge-bg`, `--color-badge-text`, `--font-family`, `--font-mono`, `--radius`. Dark mode: override inside
`@media (prefers-color-scheme: dark)` or `body.theme-dark`. Status badges: `.badge-pulled`, `.badge-cached`,
`.badge-index-only`. Components: `.file-card`, `.tag`, `.site-nav`.

## Site-wide keys

| Key | Meaning |
|---|---|
| `site.home` | Page id written to `index.html` (default `browse`; required if `browse` is dropped). Browse then moves to `browse/index.html` |
| `site.defaultView` | `cards` (default), `list`, `table` |
| `site.cards.columns` | Max columns 1-6 (default 3) |
| `site.layout` | `top`, `left`, `right` slots for every page |
| `site.pages` | Map of page id to definition, or `false` to drop a built-in |
| `site.document` | `layout`, `open`, `rules` for document pages |
| `site.ignorePatterns`, `site.hideEmptyBundles` | Exclude files / empty bundles |

## Layout slots

Slots: `top` (nav), `left`, `right`, `main`. Each is an ordered array, or `false` to disable. Precedence, last
wins, whole-slot replacement: built-in defaults, `site.layout`, the page's `layout`. `site.layout` takes only
`top`/`left`/`right`; `main` is per page or in `site.document`.

- `top` takes page ids and links: `"tags"`, `{ "page": "tags", "label": "Labels" }`, `{ "label": "Wiki", "url": "https://..." }`. Default: every page.
- `left` default `facets`; `right` default `toc`. A string is a widget name if one matches, else a page id link (use `{ "page": "tags" }` for the built-in `tags`).

Default `main` by page kind: list `title, list`; detail (`tags/<tag>/`) `breadcrumbs, title, cli, list`; content
`title, content`; document `graph, breadcrumbs, title, meta, verified, generated, description, tags, stale, cli, content, related`.

Widgets: `breadcrumbs`, `title`, `stale`, `content`, `list` (main only); `meta`, `verified`, `generated`,
`description`, `tags`, `cli`, `related`, `graph` (main + sides); `toc`, `facets`, `links` (sides only). Widgets
with no data render nothing and leave no gap.

## Pages

Built-ins: `browse`, `sources`, `tags`, `types`, `bundles`. `false` drops one (index, detail pages and nav link);
a page with the same id overrides only the index (URL kept, detail pages still generated). Custom pages are written
to `pages/<id>/index.html` and appended to the nav. An id may use letters, digits, `-`, `_`, `.` and must not equal
a widget name.

A page needs `content` and/or `filter`:

| Key | Meaning |
|---|---|
| `title` | Heading and nav label (default id) |
| `content` | `{ "file": "<repo-relative .md>" }` (cannot escape repo) or `{ "doc": "<source>:<path>" }` (must be cached/pulled) |
| `filter` | Which documents the `list` widget shows |
| `view`, `cards`, `table`, `list` | Rendering; an option block for another view is an error |
| `sort` | `{ "by": "<field>", "dir": "asc"\|"desc" }`, default title asc |
| `group` | `tag`, `type`, `bundle` or `source` |
| `limit` | Max documents after sorting |
| `open` | `page` (default), `resource` (frontmatter URL), `source` (file in repo) |
| `layout` | Slot overrides for this page |

Views: `cards` (`cards.columns`), `list` (`list.show`: default `source`, `type`, `status`), `table`
(`table.columns` from `title, source, path, bundle, type, tags, status, verified, staleAfter, generated, syncStatus, ext`).

### Filters

Fields: `source`, `bundle`, `type`, `tag`, `status`, `ext`, `path`, `stale`, `not`. Each takes a string or array;
fields AND, values within a field OR. `bundle` matches by path prefix (`kb/ops` matches `kb/ops/db`; `"exact": true`
limits it); `path` uses gitignore globs; `stale: true` = past `stale_after`; `not` is a nested filter. `{}` matches all.

```json
{
  "site": {
    "home": "welcome",
    "layout": { "top": ["browse", "runbooks", { "label": "Wiki", "url": "https://wiki.example.com" }], "left": false },
    "pages": {
      "welcome": { "title": "Welcome", "content": { "file": "docs/welcome.md" } },
      "runbooks": { "title": "Runbooks", "filter": { "type": ["runbook"], "not": { "status": ["deprecated"] } }, "view": "list" },
      "needs-review": {
        "filter": { "stale": true }, "view": "table",
        "table": { "columns": ["title", "type", "verified", "staleAfter"] },
        "sort": { "by": "staleAfter", "dir": "asc" }, "open": "resource"
      },
      "types": false
    }
  }
}
```

## Document pages

A document page (`files/<source>/<slug>.html`) layout depends on the document, not the list it was opened from.
Precedence: defaults, `site.layout`, `site.document.layout`, then each matching `site.document.rules` entry in
order (`{ "match": <filter>, "layout": {...} }`; `match` can't be empty; later rules replace earlier slots). `main`
must always contain `content`.

```json
"document": {
  "layout": { "right": ["toc"] },
  "rules": [
    { "match": { "type": ["runbook"] }, "layout": { "main": ["title", "meta", "verified", "content"] } }
  ]
}
```

`graph` is hidden until JS draws it, so keep `related` for no-JS readers.

## Recipes

Each is a fragment to merge into the existing `site` block.

**Drop the left column everywhere, keep only a TOC, trim the nav**
```json
"layout": { "top": ["browse", "sources", { "label": "Wiki", "url": "https://wiki.example.com" }], "left": false, "right": ["toc"] }
```

**Grouped cards by type, two columns**
```json
"pages": { "by-type": { "filter": {}, "group": "type", "cards": { "columns": 2 } } }
```

**Landing page from a Markdown file** (Browse moves to `browse/index.html`)
```json
"home": "welcome",
"pages": { "welcome": { "title": "Welcome", "content": { "file": "docs/welcome.md" }, "layout": { "left": false } } }
```
Links in the file to other content pages, pulled local files and indexed documents (`<source>:<path>` or relative) become site URLs; other links and images are left as-is.

**Text above a filtered list** (both `content` and `filter`; default `main` is `title, content, list`)
```json
"pages": { "ops": { "content": { "doc": "kb:knowledge/ops/overview.md" }, "filter": { "bundle": ["kb/knowledge/ops"] } } }
```

**Relabel or narrow a built-in page** (keeps its URL; detail pages like `tags/<tag>/` still generated)
```json
"pages": { "tags": { "title": "Labels", "filter": { "tag": ["ops"] } } }
```

**Remove built-in pages** (index, detail pages and nav link go; links to them become plain text)
```json
"pages": { "types": false, "bundles": false }
```

**Plain old look** (no side columns, single-column list)
```json
"layout": { "left": false, "right": false }, "cards": { "columns": 1 }
```

**Move trust info out of the reading area**
```json
"document": { "layout": { "main": ["title", "tags", "content"], "right": ["toc", "verified", "generated"] } }
```

**Remove the link graph and linked lists from document pages**: set `document.layout.main` without `graph` and `related`.

**Runbooks get a trimmed layout; ops bundle also shows the graph**
```json
"document": { "rules": [
  { "match": { "type": ["runbook"] }, "layout": { "main": ["title", "meta", "verified", "content"] } },
  { "match": { "bundle": ["kb/ops"] }, "layout": { "right": ["toc", "graph"] } }
] }
```
A runbook in `kb/ops` gets both: rules apply in order and each replaces only the slots it sets.

**Team theme in version control**: keep the CSS in a shared repo and always pass `--custom-css <path>`; `style.css` stays the untouched base.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Run aborts naming a key path | Config validation; nothing was written. Typical causes: unknown page id or widget, widget in a slot it is not valid in (e.g. `toc` in `main`), custom page id equal to a widget name, unknown view/column/sort field, `cards.columns` outside 1-6, option block for another view (e.g. `cards` on a `table` page), empty rule `match`, document `main` without `content`, missing/escaping `content.file`, uncached `content.doc` |
| `site.home` error | `site.home` is required when `browse` is dropped and must name an existing page |
| Page not in nav | `site.layout.top` is set and omits it; list it there (custom pages are only auto-appended to the default nav) |
| Edited `style.css` and lost it | Regenerated each run; use `custom.css` or `--custom-css` |
| `custom.css` edits ignored | Only `custom.css` is preserved; confirm it is under the output dir actually being served |
| `content.doc` fails | Document is `index-only`; run `eru sync` so it is cached or pulled |
| Layout change had no effect | Check precedence: a document rule or the page's own `layout` replaces the slot set in `site.layout`; slots are replaced whole, not merged per widget |
| Graph blank | Needs JS and is hidden without it (also when opening files from disk); keep `related` for no-JS readers |
| Widget shows nothing | By design when its data is absent (`toc` on a list page, `stale` on a fresh document, `verified` fields absent) and the column collapses |
| Facets not checkboxes | Only on Browse (and an override of it) with JS; elsewhere they are plain links |
| Global page unwanted in one repo | Set the same id to `false` in the local config |
| `eru site serve` shows old content after a bad edit | A failed rebuild keeps serving the last good build; read the printed error |

## Workflow

1. Edit `site` in `.eru/config.json`.
2. `eru site serve --open` to iterate (rebuilds on config and content-file changes), or `eru site generate -o <dir>`.
3. Fix any reported key-path error and re-run.
