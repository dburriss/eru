module Eru.Site.Themes

open Eru
open Eru.Adapters

// A theme is a folder with a `theme.json` (default `site` config: layout slots, defaultView,
// cards, pages) and a `theme.css` laid over the built-in stylesheet. Both files are optional,
// but a folder with neither is not a theme. The user's own config and `custom.css` always win.

type ThemeOrigin =
    | BuiltIn
    | Project of string
    | User of string

type Theme = {
    Name        : string
    Description : string option
    Css         : string
    Defaults    : SiteSettings
    Origin      : ThemeOrigin
}

/// `theme.json` on disk.
type ThemeFile = {
    Description : string option
    Site        : SiteSection option
}

let originLabel =
    function
    | BuiltIn -> "built-in"
    | Project _ -> "project"
    | User _ -> "user"

// ── built-ins ────────────────────────────────────────────────────────────────

// The terminal look is the base stylesheet itself, so it adds nothing on top.
let private terminalJson = """{ "description": "Monospace, square corners, hairline grid and a query-line Browse." }"""

let private ledgerJson =
    """{
  "description": "Dense and quiet: sans-serif, rounded corners, a facet sidebar instead of the query line, and one row per file.",
  "site": {
    "defaultView": "rows",
    "layout": { "left": [ "facets" ], "toolbar": false }
  }
}"""

let private rule (selector: string) (body: string) = selector + " {\n  " + body + "\n}\n"

let private ledgerLight =
    "--color-bg: #fbfbfc; --color-surface: #f3f3f6; --color-border: #e6e6eb; --color-text: #16171d; "
    + "--color-muted: #6b6e7b; --color-primary: #4f46e5; --color-accent-soft: #e4e2fb; "
    + "--color-ok: #0f7b4d; --color-warn: #9a6200; --color-badge-bg: #f3f3f6; --color-badge-text: #4b4e5b;"

let private ledgerDark =
    "--color-bg: #0b0c10; --color-surface: #13141a; --color-border: #23252e; --color-text: #e9eaf0; "
    + "--color-muted: #8a8d9c; --color-primary: #8b84ff; --color-accent-soft: #24224a; "
    + "--color-ok: #4fd69c; --color-warn: #f0b650; --color-badge-bg: #1b1d26; --color-badge-text: #b4b7c6;"

// Laid over the base stylesheet, so the palette rules repeat its four selectors (plain, OS dark,
// and the two toggle classes) in the same order; same specificity, later wins.
let private ledgerCss =
    "/* ledger theme */\n"
    + rule ":root" (ledgerLight
                    + " --font-family: ui-sans-serif, -apple-system, BlinkMacSystemFont, \"Geist\", \"Segoe UI\", Helvetica, Arial, sans-serif; --radius: 6px;")
    + "@media (prefers-color-scheme: dark) {\n" + rule "  :root" ledgerDark + "}\n"
    + rule ":root:where(.theme-light)" ledgerLight
    + rule ":root:where(.theme-dark)" ledgerDark
    + """
body { font-size: 13.5px; line-height: 1.45; }
h1 { font-size: 1.1rem; letter-spacing: -0.01em; }
h3 { font-size: 0.72rem; font-weight: 500; letter-spacing: 0.08em; opacity: 1; color: var(--color-muted); }

/* nav */
.site-nav { gap: 0.25rem; padding: 0 1.25rem; min-height: 48px; }
.nav-brand { font-size: 1rem; font-weight: 600; letter-spacing: -0.01em; margin-right: 1rem; gap: 0.5rem; }
.brand-mark { display: inline-block; width: 18px; height: 18px; border-radius: 5px; background: var(--color-primary); }
.brand-cursor { display: none; }
.site-nav a:not(.nav-brand) { padding: 0.3rem 0.65rem; border-radius: 6px; color: var(--color-muted); }
.site-nav a:not(.nav-brand):hover { background: var(--color-surface); color: var(--color-text); text-decoration: none; }
#theme-toggle { border-radius: 6px; font-size: 0.9rem; }

/* sidebar facets */
.region-left { padding-right: 0.9rem; border-right: 1px solid var(--color-border); }
.sidebar-section li { padding: 0.2rem 0.4rem; border-radius: 6px; }
.sidebar-section li:hover { background: var(--color-surface); }
.sidebar-section li input[type="checkbox"] { accent-color: var(--color-primary); }
.sidebar-section li .count { margin-left: auto; font-family: var(--font-mono); font-size: 0.72rem; }
.sidebar-section li.facet-empty { opacity: 0.4; }

/* query line */
.q-bar { border-color: var(--color-border); background: var(--color-surface); border-radius: 7px; padding: 0.3rem 0.4rem 0.3rem 0.75rem; }
.q-bar:focus-within { border-color: var(--color-primary); box-shadow: 0 0 0 3px var(--color-accent-soft); }
.q-prompt { color: var(--color-muted); font-weight: 400; }
.q-tok { border-radius: 999px; padding: 0 0.6rem; }
.q-pop { border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-bg); box-shadow: 0 10px 30px -12px rgba(0, 0, 0, 0.35); overflow: hidden; }
.q-opt.q-on { background: var(--color-surface); color: var(--color-text); }
.q-opt.q-on u, .q-opt.q-on em { color: var(--color-muted); }
.q-key { border-radius: 6px; }
kbd { border-radius: 4px; }

/* one row per file */
.doc-rows { border-top: 0; }
.doc-row { padding: 0.7rem 0.6rem; border-bottom: 0; border-top: 1px solid var(--color-border); }
.doc-row.q-sel { box-shadow: inset 2px 0 0 var(--color-primary); }
.doc-row .cell-name { font-weight: 500; align-items: center; }
.doc-row .cell-path { font-family: var(--font-mono); font-size: 0.75rem; }
.doc-row.q-sel .cell-path { opacity: 0.9; }
.card-path { font-family: var(--font-mono); margin: -0.15rem 0 0.4rem; }
.doc-row .cell-desc { padding-left: 14px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.doc-row-side { font-family: var(--font-mono); font-size: 0.78rem; color: var(--color-muted); }
.doc-row .add-filter { padding: 0 0.2rem; margin: 0; border-radius: 4px; border-color: transparent; color: var(--color-primary); }
.doc-row .add-filter:hover { border-color: var(--color-border); }
.doc-row .badge { border-radius: 999px; background: none; padding: 0; }

/* sync state as a dot */
.gl { font-size: 0; width: 6px; height: 6px; margin-right: 8px; border-radius: 50%; background: currentColor; align-self: center; }

/* other lists and tables */
.doc-table th { background: none; text-transform: none; letter-spacing: 0; font-size: 0.8rem; font-weight: 500; }
.doc-table th + th, .doc-table td + td { border-left: 0; }
.badge, .add-filter { border-radius: 4px; }
/* on: a soft tint rather than a solid fill, to stay quiet */
.add-filter.q-on, .add-filter.q-on:hover, .q-key.q-on, .q-key.q-on:hover {
  background: var(--color-accent-soft); border-color: var(--color-primary); color: var(--color-primary);
}
.q-key.q-on i { color: var(--color-primary); }
.region-footer { border-top-color: var(--color-border); }
"""

let private matrixJson =
    """{ "description": "The terminal layout in phosphor green: pale mint in light mode, black with a faint glow and scanlines in dark mode." }"""

let private matrixLight =
    "--color-bg: #f2faf3; --color-surface: #e2f3e5; --color-border: #a9d8b2; --color-text: #0a4d1c; "
    + "--color-muted: #3d7a4b; --color-primary: #00872a; --color-accent-soft: #cdecd3; "
    + "--color-ok: #00a832; --color-warn: #4d8f5c; --color-badge-bg: #e2f3e5; --color-badge-text: #0a4d1c; "
    + "--color-graph-incoming: #00872a; --color-graph-outgoing: #3d7a4b; --color-graph-external: #6b9a76;"

let private matrixDark =
    "--color-bg: #020a04; --color-surface: #06170b; --color-border: #0f3a1a; --color-text: #3dff6e; "
    + "--color-muted: #1f9a42; --color-primary: #7dff9c; --color-accent-soft: #0b2a14; "
    + "--color-ok: #5dff85; --color-warn: #22c24f; --color-badge-bg: #06170b; --color-badge-text: #7dff9c; "
    + "--color-graph-incoming: #5dff85; --color-graph-outgoing: #22c24f; --color-graph-external: #1f9a42;"

// Glow and scanlines belong to dark mode only: the OS preference unless the toggle forces light,
// or the toggle forced dark.
let private matrixDarkEffects (root: string) (indent: string) =
    rule (indent + root + " body") "text-shadow: 0 0 6px rgba(61, 255, 110, 0.35);"
    + rule (indent + root + " .site-nav, " + indent + root + " .region-main")
           "background-image: repeating-linear-gradient(0deg, rgba(61, 255, 110, 0.035) 0 1px, transparent 1px 3px);"

let private matrixCss =
    "/* matrix theme */\n"
    + rule ":root" matrixLight
    + "@media (prefers-color-scheme: dark) {\n" + rule "  :root" matrixDark + "}\n"
    + rule ":root:where(.theme-light)" matrixLight
    + rule ":root:where(.theme-dark)" matrixDark
    + "@media (prefers-color-scheme: dark) {\n" + matrixDarkEffects ":root:not(.theme-light)" "  " + "}\n"
    + matrixDarkEffects ":root.theme-dark" ""
    + """
/* the status glyphs and selection carry meaning, so keep the glow off the controls */
kbd, input, button, .q-opt.q-on, .add-filter.q-on { text-shadow: none; }

/* the prompt before the name */
.brand-mark::before { content: ">"; color: var(--color-primary); margin-right: 0.5rem; }
.brand-mark { display: inline; }

/* badges stay green-on-green in dark mode */
body.theme-dark .badge-manifest, body.theme-dark .badge-okf,
body.theme-dark .badge-human-reviewed, body.theme-dark .badge-machine-confirmed,
body.theme-dark .doc-stale-warning { background: var(--color-surface); color: var(--color-primary); border: 1px solid var(--color-border); }
"""

let private smileyJson =
    """{
  "description": "Coolblue blue and orange: a blue header, friendly sans-serif type, rounded pills, a facet sidebar and a card per file.",
  "site": { "defaultView": "cards", "layout": { "left": [ "facets" ] } }
}"""

// --smiley-nav is the header band, --smiley-hot the orange that marks whatever is active.
let private smileyLight =
    "--color-bg: #ffffff; --color-surface: #f5f7f9; --color-border: #dfe4e8; --color-text: #1a1a1a; "
    + "--color-muted: #6b7480; --color-primary: #0090e3; --color-accent-soft: #e6f4fc; "
    + "--color-ok: #00a651; --color-warn: #ff6600; --color-badge-bg: #e6f4fc; --color-badge-text: #006bb0; "
    + "--color-graph-incoming: #0090e3; --color-graph-outgoing: #ff6600; --color-graph-external: #6b7480; "
    + "--smiley-nav: #0090e3; --smiley-hot: #ff6600;"

let private smileyDark =
    "--color-bg: #0a1724; --color-surface: #112233; --color-border: #21384e; --color-text: #e9f2f9; "
    + "--color-muted: #8ea6bb; --color-primary: #4cb8ff; --color-accent-soft: #0f2d45; "
    + "--color-ok: #3fd47a; --color-warn: #ff8a3d; --color-badge-bg: #0f2d45; --color-badge-text: #4cb8ff; "
    + "--color-graph-incoming: #4cb8ff; --color-graph-outgoing: #ff8a3d; --color-graph-external: #8ea6bb; "
    + "--smiley-nav: #0a1724; --smiley-hot: #ff8a3d;"

let private smileyCss =
    "/* smiley theme */\n"
    + rule ":root" (smileyLight
                    + " --font-family: ui-sans-serif, -apple-system, BlinkMacSystemFont, \"Instrument Sans\", \"Segoe UI\", Helvetica, Arial, sans-serif; --radius: 6px;")
    + "@media (prefers-color-scheme: dark) {\n" + rule "  :root" smileyDark + "}\n"
    + rule ":root:where(.theme-light)" smileyLight
    + rule ":root:where(.theme-dark)" smileyDark
    + """
body { font-size: 14px; }

/* the blue band: white text on the brand blue, navy in dark mode */
.site-nav { background: var(--smiley-nav); border-bottom-color: var(--smiley-nav); }
.site-nav a, .site-nav .nav-brand { color: rgba(255, 255, 255, 0.85); }
.site-nav a:hover { color: #fff; }
.nav-brand { font-size: 1.1rem; font-weight: 700; color: #fff; display: inline-flex; align-items: center; gap: 0.6rem; }
.site-nav .nav-brand { color: #fff; }
.brand-mark {
  display: inline-block; width: 22px; height: 22px; border-radius: 50%; flex: none;
  background: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 22 22'%3E%3Ccircle cx='7.6' cy='8.6' r='1.5' fill='%23fff'/%3E%3Ccircle cx='14.4' cy='8.6' r='1.5' fill='%23fff'/%3E%3Cpath d='M6.4 12.6q4.6 4.6 9.2 0' stroke='%23fff' stroke-width='1.8' fill='none' stroke-linecap='round'/%3E%3C/svg%3E") center / 100% no-repeat, var(--smiley-hot);
}
.brand-cursor { display: none; }
#theme-toggle { color: #fff; border-color: rgba(255, 255, 255, 0.4); }
.site-nav kbd { color: #fff; border-color: rgba(255, 255, 255, 0.5); }
body.theme-dark .site-nav { border-bottom-color: var(--color-border); }
@media (prefers-color-scheme: dark) { body:not(.theme-light) .site-nav { border-bottom-color: var(--color-border); } }

h3 { font-size: 0.8rem; letter-spacing: 0.06em; }

/* the sidebar filters by source, type and tag, in that order; the extension and bundle facets stay out of it */
.facets { display: flex; flex-direction: column; }
.sidebar-section:has([data-facet="source"]) { order: 1; }
.sidebar-section:has([data-facet="type"]) { order: 2; }
.sidebar-section:has([data-facet="tags"]) { order: 3; }
.sidebar-section:has([data-facet="ext"]), .sidebar-section:has([data-facet="bundle"]) { display: none; }

/* query line */
.q-bar { border: 2px solid var(--color-primary); border-radius: 8px; padding: 0.3rem 0.8rem; }
.q-bar:focus-within { box-shadow: 0 0 0 3px var(--color-accent-soft); }
.q-prompt { color: var(--color-primary); }
.q-tok { border-radius: 999px; padding: 0 0.7rem; background: var(--color-accent-soft); border-color: var(--color-primary); color: var(--color-primary); }
.q-key { border-radius: 999px; padding: 0.1rem 0.8rem; font-family: var(--font-family); }
.q-key.q-on { background: var(--color-primary); border-color: var(--color-primary); color: #fff; }
.q-key.q-on i { color: rgba(255, 255, 255, 0.85); }
.q-pop { border: 1px solid var(--color-border); border-radius: 10px; background: var(--color-bg); box-shadow: 0 12px 30px -12px rgba(0, 40, 80, 0.35); overflow: hidden; }
.q-opt.q-on { background: var(--color-primary); color: #fff; }
.q-opt.q-on u, .q-opt.q-on em { color: #fff; }
kbd { border-radius: 4px; }

/* listings: orange marks the selected row and the active tag */
.doc-table tr.q-sel td:first-child, .file-card.q-sel, .doc-list-item.q-sel, .doc-row.q-sel { box-shadow: inset 3px 0 0 var(--smiley-hot); }
.add-filter { border-radius: 999px; font-family: var(--font-family); }
.add-filter:hover { border-color: var(--smiley-hot); color: var(--smiley-hot); }
.add-filter.q-on, .add-filter.q-on:hover { background: var(--smiley-hot); border-color: var(--smiley-hot); color: #fff; }
.doc-table th { letter-spacing: 0.06em; }
.badge { border-radius: 999px; }

/* badges and the stale warning keep to the blue and orange palette in both modes */
.badge-manifest, .badge-okf, .badge-human-reviewed, .badge-machine-confirmed,
body.theme-dark .badge-manifest, body.theme-dark .badge-okf,
body.theme-dark .badge-human-reviewed, body.theme-dark .badge-machine-confirmed {
  background: var(--color-accent-soft); color: var(--color-badge-text); border: 1px solid var(--color-primary);
}
.doc-stale-warning { background: none; color: var(--color-warn); border: 1px solid var(--color-warn); }
"""

let private cyberpunkJson =
    """{ "description": "The terminal layout in neon magenta, cyan and yellow: deep violet with a grid and glow in dark mode, pale lavender in light mode, angular display type and clipped corners." }"""

// --cyberpunk-cy marks focus and sync state, --cyberpunk-yl the prompt and cursor, --cyberpunk-wn the stale
// warning, --cyberpunk-sel the selected row and --cyberpunk-on-primary the text on a magenta fill. The glow
// variables are real glows in dark mode and nothing in light mode. Light cyan, yellow, warn and magenta are
// deeper than the mock's so that text on the lavender surfaces keeps 4.5:1.
let private cyberpunkLight =
    "--color-bg: #f4f1fb; --color-surface: #e9e3f7; --color-border: #cfc4ea; --color-text: #1b0f33; "
    + "--color-muted: #6a5a8c; --color-primary: #c4004f; --color-accent-soft: #fbe0ec; "
    + "--color-ok: #006d82; --color-warn: #6f6200; --color-badge-bg: #e9e3f7; --color-badge-text: #1b0f33; "
    + "--color-graph-incoming: #006d82; --color-graph-outgoing: #c4004f; --color-graph-external: #6a5a8c; "
    + "--cyberpunk-cy: #006d82; --cyberpunk-yl: #6f6200; --cyberpunk-wn: #9a4f00; --cyberpunk-sel: #efe6fa; "
    + "--cyberpunk-on-primary: #ffffff; --cyberpunk-bar-glow: rgba(0, 109, 130, 0.08); --cyberpunk-row-glow: transparent;"

let private cyberpunkDark =
    "--color-bg: #0b0716; --color-surface: #140d26; --color-border: #2a1d4a; --color-text: #e6f7ff; "
    + "--color-muted: #8b7fb3; --color-primary: #ff2a6d; --color-accent-soft: #2a0a1f; "
    + "--color-ok: #05d9e8; --color-warn: #f5e400; --color-badge-bg: #140d26; --color-badge-text: #e6f7ff; "
    + "--color-graph-incoming: #05d9e8; --color-graph-outgoing: #ff2a6d; --color-graph-external: #8b7fb3; "
    + "--cyberpunk-cy: #05d9e8; --cyberpunk-yl: #f5e400; --cyberpunk-wn: #f5e400; --cyberpunk-sel: #1a0f30; "
    + "--cyberpunk-on-primary: #0b0716; --cyberpunk-bar-glow: rgba(5, 217, 232, 0.18); --cyberpunk-row-glow: rgba(255, 42, 109, 0.5);"

// The grid, header glow, chromatic brand and glyph/prompt glows belong to dark mode only: the OS preference
// unless the toggle forces light, or the toggle forced dark.
let private cyberpunkDarkEffects (root: string) (indent: string) =
    rule (indent + root + " body")
         "background-image: linear-gradient(rgba(5, 217, 232, 0.04) 1px, transparent 1px), linear-gradient(90deg, rgba(5, 217, 232, 0.04) 1px, transparent 1px); background-size: 28px 28px;"
    + rule (indent + root + " .site-nav") "box-shadow: 0 1px 12px -2px rgba(255, 42, 109, 0.55);"
    + rule (indent + root + " .brand-name") "text-shadow: -2px 0 var(--color-primary), 2px 0 var(--cyberpunk-cy);"
    + rule (indent + root + " .q-prompt") "text-shadow: 0 0 8px var(--cyberpunk-yl);"
    + rule (indent + root + " .gl-pulled") "text-shadow: 0 0 6px var(--cyberpunk-cy);"

// There is no web-font loading, so the display stack names Chakra Petch first and falls back to angular
// system faces: Eurostile on macOS, Bank Gothic or Segoe UI Semibold on Windows, the system sans elsewhere.
let private cyberpunkCss =
    "/* cyberpunk theme */\n"
    + rule ":root" (cyberpunkLight
                    + " --font-display: \"Chakra Petch\", \"Bank Gothic\", \"Eurostile\", \"Rajdhani\", \"Segoe UI Semibold\", ui-sans-serif, system-ui, sans-serif;")
    + "@media (prefers-color-scheme: dark) {\n" + rule "  :root" cyberpunkDark + "}\n"
    + rule ":root:where(.theme-light)" cyberpunkLight
    + rule ":root:where(.theme-dark)" cyberpunkDark
    + "@media (prefers-color-scheme: dark) {\n" + cyberpunkDarkEffects ":root:not(.theme-light)" "  " + "}\n"
    + cyberpunkDarkEffects ":root.theme-dark" ""
    + """
/* angular display type for the brand, nav, headings and popup heads */
.nav-brand, .site-nav a, h3, .doc-table th, .q-pop-head {
  font-family: var(--font-display); text-transform: uppercase; letter-spacing: 0.08em;
}
.nav-brand { font-size: 1.25rem; font-weight: 700; }
.brand-cursor { color: var(--cyberpunk-yl); text-shadow: none; }
.site-nav { border-bottom-color: var(--color-primary); }
.site-nav a:hover { color: var(--cyberpunk-cy); }
#theme-toggle { color: var(--cyberpunk-yl); border-color: var(--cyberpunk-yl); }

/* query line: clipped corners. clip-path also clips outlines and outer shadows, so focus is an inset ring */
.q-bar {
  border: 1px solid var(--cyberpunk-cy);
  clip-path: polygon(0 0, calc(100% - 14px) 0, 100% 14px, 100% 100%, 14px 100%, 0 calc(100% - 14px));
  box-shadow: inset 0 0 0 1px var(--cyberpunk-cy), inset 0 0 18px var(--cyberpunk-bar-glow);
}
.q-bar:focus-within {
  border-color: var(--cyberpunk-cy);
  box-shadow: inset 0 0 0 2px var(--cyberpunk-cy), inset 0 0 18px var(--cyberpunk-bar-glow);
}
.q-prompt { color: var(--cyberpunk-yl); }
.q-input { caret-color: var(--cyberpunk-yl); }
.q-tok {
  border-color: var(--color-primary); color: var(--color-primary); background: var(--color-accent-soft);
  clip-path: polygon(0 0, calc(100% - 6px) 0, 100% 6px, 100% 100%, 6px 100%, 0 calc(100% - 6px));
}
.q-key, .add-filter { clip-path: polygon(0 0, calc(100% - 6px) 0, 100% 6px, 100% 100%, 0 100%); }
.q-key.q-on { background: var(--cyberpunk-cy); border-color: var(--cyberpunk-cy); color: var(--color-bg); }
.q-key.q-on i { color: var(--color-bg); }
.add-filter.q-on, .add-filter.q-on:hover { background: var(--color-primary); border-color: var(--color-primary); color: var(--cyberpunk-on-primary); }
.q-key:focus-visible, .add-filter:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--cyberpunk-cy); }
.add-filter:hover { border-color: var(--cyberpunk-cy); color: var(--cyberpunk-cy); }
.q-pop { border-color: var(--color-primary); box-shadow: 6px 6px 0 var(--color-accent-soft); }
.q-opt.q-on { background: var(--color-primary); color: var(--cyberpunk-on-primary); }
.q-opt.q-on u, .q-opt.q-on em, .q-opt.q-on .gl { color: var(--cyberpunk-on-primary); }

/* listings: magenta marks the selected row */
.doc-table tr.q-sel td { background: var(--cyberpunk-sel); }
.doc-row.q-sel, .doc-list-item.q-sel, .file-card.q-sel { background: var(--cyberpunk-sel); }
.doc-table tr.q-sel td:first-child, .file-card.q-sel, .doc-list-item.q-sel, .doc-row.q-sel {
  box-shadow: inset 3px 0 0 var(--color-primary), inset 12px 0 16px -12px var(--cyberpunk-row-glow);
}

/* cyan is synced, yellow is cached; the glyph shapes carry the state too */
.gl-pulled { color: var(--cyberpunk-cy); }
.gl-cached { color: var(--cyberpunk-yl); }

/* the glow stays off controls and the selected option */
kbd, input, button, .q-opt.q-on { text-shadow: none; }

/* badges and the stale warning keep to the neon palette in both modes */
.badge-manifest, .badge-okf, .badge-human-reviewed,
body.theme-dark .badge-manifest, body.theme-dark .badge-okf, body.theme-dark .badge-human-reviewed {
  background: var(--color-surface); color: var(--cyberpunk-cy); border: 1px solid var(--cyberpunk-cy);
}
.badge-machine-confirmed, body.theme-dark .badge-machine-confirmed {
  background: var(--color-surface); color: var(--cyberpunk-yl); border: 1px solid var(--cyberpunk-yl);
}
.doc-stale-warning, body.theme-dark .doc-stale-warning {
  background: var(--color-surface); color: var(--cyberpunk-wn); border: 1px solid var(--cyberpunk-wn);
}
"""

let private builtIns : (string * string * string) list =
    [ "terminal", terminalJson, ""
      "ledger", ledgerJson, ledgerCss
      "matrix", matrixJson, matrixCss
      "smiley", smileyJson, smileyCss
      "cyberpunk", cyberpunkJson, cyberpunkCss ]

let builtInNames = builtIns |> List.map (fun (n, _, _) -> n)

// ── loading ──────────────────────────────────────────────────────────────────

let private build (name: string) (origin: ThemeOrigin) (json: string) (css: string) : Result<Theme, string> =
    Serialization.deserialize<ThemeFile> json
    |> Result.bind (fun f ->
        if obj.ReferenceEquals(f, null) then Error "theme.json is empty"
        else
            f.Site |> Option.defaultValue SiteSection.empty |> SiteSettings.parse
            |> Result.map (fun defaults -> { Name = name; Description = f.Description; Css = css; Defaults = defaults; Origin = origin }))
    |> Result.mapError (fun e -> $"theme '{name}': theme.json: {e}")

let private fromDir (deps: Deps) (name: string) (origin: ThemeOrigin) (dir: string) : Result<Theme option, string> =
    let read file = deps.ReadLocalFile (PathJoin.Combine(dir, file))
    match read "theme.json", read "theme.css" with
    | Error e, _ | _, Error e -> Error $"theme '{name}': {e}"
    | Ok None, Ok None -> Ok None
    | Ok json, Ok css -> build name origin (json |> Option.defaultValue "{}") (css |> Option.defaultValue "") |> Result.map Some

let private roots (deps: Deps) : (string -> ThemeOrigin) * string list =
    let project = Paths.localThemesDir (deps.GetCwd())
    let user = Paths.userThemesDir ()
    (fun dir -> if dir = project then Project dir else User dir), [ project; user ]

/// Looks a theme up by name: built-in first, then `.eru/themes/<name>/`, then the user's
/// themes directory.
let resolve (deps: Deps) (name: string) : Result<Theme, string> =
    match builtIns |> List.tryFind (fun (n, _, _) -> n = name) with
    | Some (_, json, css) -> build name BuiltIn json css
    | None ->
        let origin, dirs = roots deps
        let rec search =
            function
            | [] -> Ok None
            | dir :: rest ->
                match fromDir deps name (origin dir) (PathJoin.Combine(dir, name)) with
                | Ok None -> search rest
                | other -> other
        match search dirs with
        | Ok (Some theme) -> Ok theme
        | Error e -> Error e
        | Ok None ->
            let installed =
                dirs
                |> List.collect (fun d -> deps.ListLocalDirectories d |> Result.defaultValue [])
                |> List.map PathUtil.fileName
            let available = builtInNames @ installed |> List.distinct |> String.concat ", "
            Error $"site.theme: unknown theme '{name}' (available: {available})"

/// Every theme that can be selected, in resolution order; a name shadowed by an earlier
/// location is listed once.
let discover (deps: Deps) : (string * Result<Theme, string>) list =
    let _, dirs = roots deps
    let installed =
        dirs
        |> List.collect (fun d -> deps.ListLocalDirectories d |> Result.defaultValue [])
        |> List.map PathUtil.fileName
    builtInNames @ installed
    |> List.distinct
    |> List.map (fun name -> name, resolve deps name)

/// Lays the user's settings over the theme's defaults.
let apply (theme: Theme) (settings: SiteSettings) : SiteSettings =
    SiteSettings.merge theme.Defaults settings
