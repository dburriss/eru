---
title: Customize the generated site
type: how-to
tags: [site, css, theme]
---

# Customize the generated site

See the [site generation reference](../reference/site-generation.md) for what `eru site generate` produces. This
guide covers changing how it looks.

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

## Offline use

The generated site is fully self-contained — no fonts, scripts, or stylesheets load from a CDN. Copy the output
directory anywhere and open `index.html`; it works without a network connection or a web server.
