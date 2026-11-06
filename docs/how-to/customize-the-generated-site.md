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

## Pick a theme

`site.theme` switches the whole look; `eru theme list` shows the choices.

```bash
eru config set site.theme ledger
```

Your `custom.css` and `site` settings still apply on top of a theme. To make your own, see
[write or install a site theme](write-a-site-theme.md).

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
  --color-primary: #c2410c;   /* accent: filter tokens, focus, the cursor in the logo */
  --color-text: #201d1d;      /* body copy */
  --color-bg: #fdfcfc;        /* page background */
  --color-surface: #f4f1f1;   /* table header, hover and code background */
  --color-border: #d6d1d1;    /* hairlines and dividers */
  --color-muted: #6f6969;     /* descriptions, counts and labels */
  --color-accent-soft: #fde7da; /* focus shadow of the query line */
  --color-ok: #15803d;        /* pulled */
  --color-warn: #a16207;      /* cached */
  --color-badge-bg: #f1ece9;  /* type badge background */
  --color-badge-text: #7c2d12;/* type badge text */
  --font-family: var(--font-mono);  /* the whole site is monospace by default */
  --font-prose: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif; /* rendered Markdown */
  --font-mono: ui-monospace, "SF Mono", "JetBrains Mono", Menlo, Consolas, monospace;
  --radius: 0;                /* square corners; set e.g. 6px to soften */
}
```

A `:root` override applies in both light and dark mode. To give each mode its own colours, use `body.theme-light` and
`body.theme-dark`, which follow the theme toggle (a `prefers-color-scheme` media query ignores it):

```css
body.theme-dark {
  --color-text: #c9d1d9;
  --color-bg: #0d1117;
  --color-surface: #161b22;
  --color-border: #30363d;
  --color-primary: #58a6ff;
  --color-badge-bg: #1e2a3a;
  --color-badge-text: #79b8ff;
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

Every page has a nav, a left column, a main column, a right column, and full-width `top`, `toolbar` and `footer` bands. Set them under `site.layout` in
`.eru/config.json` (or the global config). Each slot is an ordered list, or `false` to switch it off:

```json
{
  "site": {
    "layout": {
      "nav": ["browse", "sources", { "label": "Wiki", "url": "https://wiki.example.com" }],
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

## Move the query line

Browse shows the query line in the `toolbar` band, below the title. Put it in a column instead, or bring back the
plain search box:

```json
{ "site": { "pages": { "browse": { "filter": {}, "layout": { "toolbar": false, "left": ["query", "facets"] } } } } }
```

`query` is valid in `toolbar`, `left` and `right`, once per page. Any other page can opt in with
`"toolbar": ["query"]`. `"layout": { "toolbar": false }` under `site` restores the plain search box on Browse.

## Change the footer

Every page ends with a link to the eru docs. Replace it with your own links, or remove it:

```json
{ "site": { "layout": { "footer": [{ "label": "Handbook", "url": "https://wiki.example.com" }] } } }
```

`"footer": false` removes the footer.

## Drop or reorder built-in pages

```json
{ "site": { "pages": { "types": false, "bundles": false } } }
```

`false` removes the page, its detail pages and its nav link. To reorder the nav, list the pages in `layout.nav`.

## Opt out of the refreshed defaults

Sites generated before layouts existed had a plain nav, no side columns on most pages and a single-column list.
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
