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
├── index.html                    # Full file list with sidebar + search (JS-enhanced)
├── sources/
│   ├── index.html                # All sources with manifest indicator and file counts
│   └── <name>/
│       └── index.html            # Files for one source
├── tags/
│   ├── index.html                # All tags with file counts
│   └── <tag>/
│       └── index.html            # Files carrying a given tag
├── files/
│   └── <source>/
│       └── <slug>.html           # Rendered content page (cached/pulled .md files only)
├── assets/
│   └── css/
│       └── style.css             # Base stylesheet (dark mode via prefers-color-scheme)
├── js/
│   ├── app.js                    # Checkbox filtering + in-place search
│   └── theme.js                  # Manual dark/light theme toggle
└── data/
    ├── documents.json            # Full document list for client-side search
    ├── sources.json              # Source list with manifest flags
    └── manifest.json            # Schema version and document count
```

Every page is fully navigable as plain HTML with no JavaScript. JS is loaded as an optional enhancement that adds
in-place search and checkbox facet filtering. Dark/light theming works via CSS `prefers-color-scheme` even without
JS; the theme toggle button is JS-only.

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
| Manual dark/light toggle | — | ✓ |
| Dark mode via OS setting | ✓ | — |
