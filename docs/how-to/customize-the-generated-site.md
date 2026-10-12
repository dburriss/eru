---
title: Customize the generated site
type: how-to
tags: [site, css, theme]
---

# Customize the generated site

See the [site generation reference](../reference/site-generation.md) for what `eru site generate` produces. This
guide covers changing how it looks and what each page shows. For new pages see
[build custom site pages](build-custom-site-pages.md); for the page a document opens to see
[configure document pages](configure-document-pages.md).

## Generate the site

```bash
eru site generate                       # into ./cache-site/
eru site generate -o /tmp/my-site       # into a custom directory
eru site generate --open                # and open it in the browser
```

Or serve it with live reload while you iterate on the theme:

```bash
eru site serve --open
```

## How the CSS files behave

Every `eru site generate` run writes two CSS files:

| File | Behaviour |
|---|---|
| `assets/css/style.css` | Always regenerated — **do not edit directly** |
| `assets/css/custom.css` | Created once as a blank placeholder, then **never overwritten** |

`custom.css` loads after `style.css` on every page, so anything you write there overrides the base styles, and
your edits survive re-runs.

## Point at a theme file you maintain elsewhere

If you keep your theme in a separate file (e.g. a shared repo), pass it on every generate/serve call and eru
copies it into the output for you:

```bash
eru site generate --custom-css ~/themes/company.css
```

This is the recommended workflow for teams sharing a theme: keep the source CSS in version control, pass its path
via `--custom-css`, and let `style.css` stay untouched as the base.

## Override the colour scheme and typography

`style.css` exposes CSS custom properties. Put overrides in `custom.css` or your `--custom-css` file:

```css
:root {
  --color-primary: #0366d6;   /* links, active states, focus rings */
  --color-text: #24292e;      /* body copy */
  --color-bg: #ffffff;        /* page background */
  --color-surface: #f6f8fa;   /* card and sidebar background */
  --color-border: #e1e4e8;    /* card borders and dividers */
  --color-badge-bg: #eef2ff;  /* source/tag badge background */
  --color-badge-text: #3730a3;/* source/tag badge text */
  --font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
  --font-mono: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  --radius: 6px;               /* border radius for cards and inputs */
}
```

Dark mode overrides go inside `@media (prefers-color-scheme: dark)` and/or `body.theme-dark`:

```css
@media (prefers-color-scheme: dark) {
  :root {
    --color-text: #c9d1d9;
    --color-bg: #0d1117;
    --color-surface: #161b22;
    --color-border: #30363d;
    --color-primary: #58a6ff;
    --color-badge-bg: #1e2a3a;
    --color-badge-text: #79b8ff;
  }
}
```

## Override status badge colours

```css
.badge-pulled     { background: #d1fae5; color: #065f46; }
.badge-cached     { background: #fef3c7; color: #92400e; }
.badge-index-only { background: #f5f5f5; color: #666; border: 1px solid #e0e0e0; }
```

## Other common component overrides

```css
/* Remove card border, use shadow only */
.file-card {
  border: none;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.08);
}

/* Pill-shaped tags */
.tag {
  border-radius: 24px;
}

/* Coloured nav bar */
.site-nav {
  background: #003082;
  color: #fff;
}
.site-nav a { color: #fff; }

/* Larger border radius on cards */
.file-card { border-radius: 12px; }
```

## Change the layout

Every page has a top nav, a left column, a main column and a right column. Set them under `site.layout` in
`.eru/config.json` (or the global config). Each slot is an ordered list, or `false` to switch it off:

```json
{
  "site": {
    "layout": {
      "top": ["browse", "sources", { "label": "Wiki", "url": "https://wiki.example.com" }],
      "left": false,
      "right": ["toc"]
    }
  }
}
```

This site drops the left facet column everywhere, keeps only the table of contents on the right and trims the nav
to two built-in pages plus an external link. An omitted slot keeps its default; a disabled or empty slot leaves no
gap in the page. The widgets you can put in a slot are listed in the
[site generation reference](../reference/site-generation.md#layout-slots-and-widgets).

## Drop or reorder built-in pages

```json
{ "site": { "pages": { "types": false, "bundles": false } } }
```

`false` removes the page, its detail pages and its nav link. To reorder the nav, list the pages in `layout.top`.

## Opt out of the refreshed defaults

Sites generated before layouts existed had a plain top nav, no side columns on most pages and a single-column list.
The defaults are now a facet column on the left, a table of contents and linked documents on the right, and a card
grid of up to three columns. To get close to the old look:

```json
{
  "site": {
    "layout": { "left": false, "right": false },
    "cards": { "columns": 1 }
  }
}
```

Document pages now show the link graph above the document and the linked-document lists below it. To remove them,
set `site.document.layout.main` to the widgets you want (without `graph` and `related`).

## Choose how lists look

```json
{ "site": { "defaultView": "list" } }
```

`cards` (default), `list` and `table` are available; see [views](../reference/site-generation.md#views).

## Offline use

The generated site is fully self-contained — no fonts, scripts, or stylesheets load from a CDN. Copy the output
directory anywhere and open `index.html`; it works without a network connection or a web server.
