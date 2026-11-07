---
title: Write or install a site theme
type: how-to
tags: [site, css, theme]
---

# Write or install a site theme

A theme bundles a stylesheet and default `site` settings, so a whole look can be switched with one key. The
[themes reference](../reference/site-generation.md#themes) has the lookup order and layering; this guide walks
through making one. To tweak a single colour instead, use [custom.css](customize-the-generated-site.md).

## Switch to an existing theme

```bash
eru theme list                       # terminal, ledger, matrix and anything installed
eru config set site.theme ledger     # or "theme": "ledger" under "site" in .eru/config.json
eru site generate
```

## Write a theme

Create a folder named after the theme, in `.eru/themes/` to keep it with the repo or in `~/.config/eru/themes/`
to use it everywhere:

```
.eru/themes/paper/
├── theme.json
└── theme.css
```

`theme.json` holds the defaults the theme wants for the site's layout and views:

```json
{
  "description": "Warm paper tones, one row per file.",
  "site": {
    "defaultView": "rows",
    "layout": { "left": ["facets"] }
  }
}
```

`theme.css` loads after the built-in stylesheet, so override the colour variables and any component class. Repeat
the variables under `:root:where(.theme-light)` and `:root:where(.theme-dark)` if the theme should look the same
whichever mode the toggle picks:

```css
:root, :root:where(.theme-light) {
  --color-bg: #fbf7f0;
  --color-text: #2b2620;
  --color-primary: #9a3412;
}
@media (prefers-color-scheme: dark) {
  :root { --color-bg: #1b1814; --color-text: #ece4d8; --color-primary: #fb923c; }
}
:root:where(.theme-dark) { --color-bg: #1b1814; --color-text: #ece4d8; --color-primary: #fb923c; }
```

Select it and check the result:

```bash
eru config set site.theme paper
eru site serve --open
```

`eru site serve` rebuilds when the config changes, so re-run it (or touch `.eru/config.json`) after editing a
theme file.

## Check it loaded

`eru theme list` marks the active theme with `*`. A theme with a broken `theme.json` is listed as `invalid` with the
reason, and `eru site generate` fails naming the theme.

## Share a theme

Copy the folder: commit `.eru/themes/<name>/` to share it with a repo, or drop it into another machine's
`~/.config/eru/themes/`. Your own `site` settings and `custom.css` always override a theme, so the same theme
can serve several repos.
