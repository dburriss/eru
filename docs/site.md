# eru site

Generate a fully self-contained static HTML site for browsing and searching your local knowledge cache.

---

## `eru site generate`

```
eru site generate [-o <dir>] [--open] [--custom-css <path>]
```

| Flag | Default | Description |
|---|---|---|
| `-o` / `--output` | `./cache-site/` | Directory to write the generated site into |
| `--open` | off | Open `index.html` in the default browser after generation |
| `--custom-css <path>` | — | Path to a CSS file that is copied into the site and loaded after `style.css` |

**Examples**

```bash
# Generate into the default ./cache-site/ directory
eru site generate

# Generate into a custom directory
eru site generate -o /tmp/my-site

# Generate and immediately open in the browser
eru site generate --open

# Combine flags
eru site generate -o /tmp/my-site --open
```

---

## What gets generated

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

Every page is fully navigable as plain HTML with no JavaScript. JS is loaded as an optional enhancement that adds in-place search and checkbox facet filtering. Dark/light theming works via CSS `prefers-color-scheme` even without JS; the theme toggle button is JS-only.

---

## File statuses

Each file in the index carries one of three statuses:

| Status | Meaning |
|---|---|
| `pulled` | File is tracked in `.eru/eru.lock` and present on disk |
| `cached` | File is in the local content cache but not checked out locally |
| `index-only` | Metadata only — file has not been cached or pulled |

Only `cached` and `pulled` files get rendered content pages and appear in search.

---

## Customising the site

### How CSS files are managed

Every `eru site generate` run writes two CSS files:

| File | Behaviour |
|---|---|
| `assets/css/style.css` | Always regenerated — **do not edit directly** |
| `assets/css/custom.css` | Created once as a blank placeholder, then **never overwritten** |

`custom.css` is loaded after `style.css` in every page, so any rule you write there overrides the base styles. Because `eru` never touches it again, your edits survive re-runs.

If you maintain your theme in a separate file (e.g. a dedicated repo), point `--custom-css` at it and `eru` will copy it into the output on every run:

```bash
eru site generate --custom-css ~/themes/my-company.css
```

This is the recommended workflow for teams sharing a theme: keep the source CSS in version control, pass its path via `--custom-css`, and `style.css` stays untouched as the base.

---

### CSS custom properties

The base `style.css` exposes CSS custom properties that control the entire colour scheme and typography. To override them, create a file (e.g. `my-theme.css`) and pass it to the generator. Support for `--config` is planned; in the meantime you can append your overrides directly to the generated `style.css`.

The full set of overridable properties:

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

### Status badge colours

Status badges use their own CSS classes. Override them independently:

```css
.badge-pulled     { background: #d1fae5; color: #065f46; }
.badge-cached     { background: #fef3c7; color: #92400e; }
.badge-index-only { background: #f5f5f5; color: #666; border: 1px solid #e0e0e0; }
```

### Component overrides

Common overrides beyond custom properties:

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

---

## Offline use

The generated site is fully self-contained. No fonts, scripts, or stylesheets are loaded from a CDN. Copy the output directory to any location and open `index.html` — it works without a network connection or a web server.

---

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
