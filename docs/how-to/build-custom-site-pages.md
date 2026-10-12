---
title: Build custom site pages
type: how-to
tags: [site, pages, filters, views]
---

# Build custom site pages

Besides the built-in pages, `eru site generate` can build pages you define: a filtered list of documents, a page
that renders a Markdown file or a knowledge document, or both together. Pages are defined under `site.pages` in
`.eru/config.json` (shared team pages can live in the global config). The full key list is in the
[site generation reference](../reference/site-generation.md#pages).

## A filtered list of documents

A page that shows every runbook, sorted by title, as a compact list:

```json
{
  "site": {
    "pages": {
      "runbooks": {
        "title": "Runbooks",
        "filter": { "type": ["runbook"] },
        "view": "list"
      }
    }
  }
}
```

It is written to `pages/runbooks/index.html` and added to the top nav after the built-in pages.

Filters combine fields with AND and values within a field with OR. Negate with `not`, match paths with globs,
and match a bundle with or without its children:

```json
"filter": {
  "bundle": ["kb/ops"],
  "tag": ["db", "backup"],
  "path": ["runbooks/**"],
  "not": { "status": ["deprecated"] }
}
```

## A "needs review" table

```json
"needs-review": {
  "title": "Needs review",
  "filter": { "stale": true },
  "view": "table",
  "table": { "columns": ["title", "type", "bundle", "status", "verified", "staleAfter"] },
  "sort": { "by": "staleAfter", "dir": "asc" },
  "open": "resource"
}
```

`open` decides where clicking a row goes: the document page (`page`, default), the document's `resource`
frontmatter URL (`resource`) or the file in its source repository (`source`).

## Grouped cards

```json
"by-type": { "filter": {}, "group": "type", "cards": { "columns": 2 } }
```

`group` accepts `tag`, `type`, `bundle` or `source`; each group is a headed section with its own grid.

## A landing page from a Markdown file

```json
{
  "site": {
    "home": "welcome",
    "pages": {
      "welcome": {
        "title": "Welcome",
        "content": { "file": "docs/welcome.md" },
        "layout": { "left": false }
      }
    }
  }
}
```

`site.home` makes it the page at `index.html`; Browse moves to `browse/index.html`. The file path is relative to
the repo root and cannot leave it. Links to other content pages, pulled local files and indexed documents are
rewritten to site URLs. `eru site serve` rebuilds when the file changes.

To render an indexed document at a URL you choose, use `"content": { "doc": "kb:knowledge/ops/overview.md" }`;
the document must be cached or pulled (`eru sync`).

Give a page both `content` and `filter` to show the text above the matching documents:

```json
"ops": {
  "content": { "doc": "kb:knowledge/ops/overview.md" },
  "filter": { "bundle": ["kb/knowledge/ops"] }
}
```

## Override a built-in page

A page whose id is a built-in id (`browse`, `sources`, `tags`, `types`, `bundles`) replaces that page's index and
keeps its URL. The detail pages, such as `tags/<tag>/`, are still generated.

```json
{ "site": { "pages": { "tags": { "title": "Labels", "filter": { "tag": ["ops"] } } } } }
```

## Troubleshooting

Config problems stop generation with a message naming the key, such as
`site.pages.runbooks.view: unknown view 'grid'`. A custom page id may not equal a widget name (`toc`, `stale`,
`list`, ...). In `eru site serve` a failed rebuild is printed and the last good site keeps being served.
