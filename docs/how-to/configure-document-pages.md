---
title: Configure document pages
type: how-to
tags: [site, document, layout, widgets]
---

# Configure document pages

A document page is the page a list item opens (`files/<source>/<slug>.html`). Because the site is static and a
document has a single URL, its layout can depend on the document, not on the list you came from. Configure it
under `site.document`. See the [site generation reference](../reference/site-generation.md#document-pages) for the
precedence rules.

## Change every document page

```json
{
  "site": {
    "document": {
      "layout": {
        "left": false,
        "right": ["toc"],
        "main": ["graph", "breadcrumbs", "title", "meta", "verified", "generated", "description", "tags", "stale", "cli", "content", "related"]
      }
    }
  }
}
```

That is the default layout: the link graph at the top, the document in the middle and the linked-document lists at the bottom. Remove widgets to hide them. `content` must stay in `main`.

## Show who verified and generated a document

`verified` shows the trust tier and each verifier with the date; `generated` shows who or what produced the
document and when. Both are in the default `main`. Move them to a side column to keep the reading area clean:

```json
"layout": { "main": ["title", "tags", "content"], "right": ["toc", "verified", "generated"] }
```

## Different layouts for different documents

Add `rules`. Each rule matches documents with the same fields as a [page filter](build-custom-site-pages.md) and
replaces whole slots; later rules win.

```json
"document": {
  "rules": [
    { "match": { "type": ["runbook"] },
      "layout": { "right": ["toc"], "main": ["title", "meta", "verified", "content"] } },
    { "match": { "bundle": ["kb/ops"] }, "layout": { "right": ["toc", "graph"] } }
  ]
}
```

A runbook in `kb/ops` gets the runbook `main` from the first rule and the graph in `right` from the second.

## Move or remove the link graph and linked documents

`graph` is first in `main` and `related` (the link lists only) is last. Both render nothing for a document with no links. The graph is hidden until JavaScript has drawn it, so without JavaScript (or when opening the files directly from disk) only the `related` lists show and nothing is repeated. Keep `related` in the layout if you want link lists for readers without JavaScript. Move either widget to a side slot, for example `"right": ["toc", "related"]`, or leave it out of `main`.

## Control where list items link

`site.document.open` sets where list items link by default: `page`, `resource` or `source`. A custom page can
override it with its own `open`.
