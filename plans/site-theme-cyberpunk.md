---
status: done
---

# Plan: add the `cyberpunk` built-in site theme

## Context
`eru site` ships four built-in themes: `terminal` (base stylesheet), `ledger`, `matrix` (443d5dc) and
`smiley` (8bb5338). The "Eru Site Mocks" artifact has tab I · Cyberpunk: the terminal layout (F) in neon
magenta, cyan and yellow on deep violet. This adds it as a built-in selectable with `site.theme: cyberpunk`.
It is CSS-only like `matrix` and `smiley`: no domain, CLI or config changes (the theme name is only validated
as a safe folder name). The mock's own note says the glow "would need toning down for daily reading"; the
user chose to keep it **as in the mock**, so the effects below are all kept, in dark mode only.

## Decisions (from the design questions)
- **Glow:** as in the mock: grid, header glow, brand chromatic split, cursor glow, pulled-glyph glow,
  selected-row glow. All dark mode only.
- **Display font:** name Chakra Petch first, then system fallbacks (no web-font loading exists, no per-theme JS):
  `--font-display: "Chakra Petch", "Bank Gothic", "Eurostile", "Rajdhani", "Segoe UI Semibold", ui-sans-serif, system-ui, sans-serif`.
  Without Chakra Petch installed this falls to Eurostile on macOS, Bank Gothic or Segoe UI Semibold on Windows,
  and the system sans on Linux, always bold, uppercase and letter-spaced so it still reads as display type. Body
  text stays on the mono stack (`--font-family` untouched). The docs state this fallback.
- **Contrast:** deviate from the mock where it fails 4.5:1 (measured with a WCAG luminance script):

  | Pair | Mock | Plan |
  |---|---|---|
  | light cyan on bg / surface | 3.69 / 3.29 | `#006d82` (5.36 / 4.79) |
  | light yellow on bg / surface | 3.87 / 3.45 | `#6f6200` (5.5 / 4.91) |
  | light warn orange on bg | 4.28 | `#9a4f00` (re-check, expect > 4.5) |
  | light magenta on surface / selected row | 4.34 / 4.49 | `#c4004f` (4.85 / 5.02); white on it 5.4+ |
  | dark white on magenta (selected option) | 3.62 | dark ink `#0b0716` on `#ff2a6d` (5.5) |

  Dark values all pass (text 18:1, cyan 11.5, yellow 15, magenta 5.5, muted 5.5 on bg).

## Approach (copy the pattern of commits 443d5dc and 8bb5338)

### 0. Save this plan
First step on approval: copy this file to `plans/site-theme-cyberpunk.md` in the repo (plan mode blocks writing
it there now). It ships in the same commit, as the matrix and smiley plans did.

### 1. `src/Eru.Site/Themes.fs`
- `cyberpunkJson`: description only, no `site` defaults (terminal layout kept).
- `cyberpunkLight` / `cyberpunkDark` token strings, same token set as `matrixLight`/`smileyLight`
  (`--color-bg|surface|border|text|muted|primary|accent-soft|ok|warn|badge-bg|badge-text|graph-*`) plus
  theme variables `--cyberpunk-cy`, `--cyberpunk-yl`, `--cyberpunk-sel`, `--cyberpunk-on-primary` (as smiley adds
  `--smiley-nav`/`--smiley-hot`):
  - dark: bg `#0b0716`, surface `#140d26`, border `#2a1d4a`, text `#e6f7ff`, muted `#8b7fb3`, primary (magenta)
    `#ff2a6d`, accent-soft `#2a0a1f`, ok = cyan `#05d9e8`, warn = yellow `#f5e400`, badge-bg `#140d26`,
    badge-text `#e6f7ff`, graph incoming cyan / outgoing magenta / external muted; cy `#05d9e8`, yl `#f5e400`,
    sel `#1a0f30`, on-primary `#0b0716`
  - light (pale lavender): bg `#f4f1fb`, surface `#e9e3f7`, border `#cfc4ea`, text `#1b0f33`, muted `#6a5a8c`,
    primary `#c4004f`, accent-soft `#fbe0ec`, ok = cyan `#006d82`, warn = yellow `#6f6200`, badge-bg `#e9e3f7`,
    badge-text `#1b0f33`; cy `#006d82`, yl `#6f6200`, sel `#efe6fa`, on-primary `#ffffff`; stale-warning orange
    `#9a4f00` via a `--cyberpunk-wn` variable (dark: `#f5e400`)
- `cyberpunkCss`, marker `/* cyberpunk theme */`, same four palette selectors in the same order as
  `matrixCss` (`:root` with `--font-display`, OS-dark media query, `:root:where(.theme-light)`,
  `:root:where(.theme-dark)`). Then, class names checked against `SiteGenerator.fs`:
  - **Display type:** `.nav-brand` (700, 1.25rem), `.site-nav a`, `th`, `.q-pop-head`, `h3` use
    `--font-display`, uppercase, `letter-spacing: 0.08em`. `.nav-brand::after` (the base `_`) in yellow = the prompt/cursor.
  - **Nav:** bottom border magenta; active link cyan with cyan underline (find the active-link selector in
    `HtmlTemplates.fs:53`); `#theme-toggle` yellow border and text.
  - **Chamfered corners via `clip-path` polygons:** `.q-bar` (14px), `.q-tok` (6px), `.q-key`, `.add-filter`
    (6px, one corner). `clip-path` also clips outlines and outer box-shadows, so the base focus styles
    (`.q-bar:focus-within` 4px offset shadow, `.q-key:focus-visible` / `.add-filter:focus-visible` outline) are
    replaced by `inset` box-shadows in cyan. Check that `.q-pop` is a sibling of `.q-bar` inside `.query`
    (`SiteGenerator.fs:596-615`) so the popup is not clipped.
  - **Colour roles:** magenta marks active things, cyan marks focus and sync state, yellow marks the prompt and
    cursor. Per the mock: `.q-bar` border cyan with inset glow; `.q-tok` magenta border on
    accent-soft; `.q-key.q-on` cyan fill with `--color-bg` text; `.q-prompt` and `.q-input { caret-color }`
    yellow; `.q-pop` magenta border with offset magenta shadow (`6px 6px 0`); `.q-opt.q-on` magenta fill with
    `--cyberpunk-on-primary` text (also its `u`, `em`, `.gl`); `.add-filter:hover` cyan; selected row
    (`.doc-table tr.q-sel td:first-child`, `.file-card.q-sel`, `.doc-list-item.q-sel`, `.doc-row.q-sel`) gets the
    `--cyberpunk-sel` fill and a 3px inset magenta bar.
  - **Sync glyphs:** `.gl-pulled` cyan, `.gl-cached` yellow, `.gl-index-only` muted (all pass in both modes, so
    colour plus the glyph shape carries state).
  - **Dark-mode effects**, scoped like `matrixDarkEffects` (a `cyberpunkDarkEffects root indent` helper, emitted
    inside `@media (prefers-color-scheme: dark)` for `:root:not(.theme-light)` and again for `:root.theme-dark`):
    - `body`: 28px cyan grid (`linear-gradient` x2 at `rgba(5,217,232,.04)`)
    - `.site-nav`: `box-shadow: 0 1px 12px -2px rgba(255,42,109,.55)`
    - `.nav-brand`: `text-shadow: -2px 0 var(--color-primary), 2px 0 var(--cyberpunk-cy)` (the chromatic split)
    - cursor glow: `.q-prompt` yellow `text-shadow`; the input caret itself cannot glow, so `caret-color` yellow
    - `.gl-pulled`: `text-shadow: 0 0 6px var(--cyberpunk-cy)`
    - selected row: second inset glow `inset 12px 0 16px -12px rgba(255,42,109,.5)`
    - the `.q-bar` inset glow (`inset 0 0 18px rgba(5,217,232,.18)`) is dark-only too; light uses `.08` of the light cyan
  - **Guard:** `kbd, input, button, .q-opt.q-on { text-shadow: none; }` as matrix does, so effects never reach
    controls or the selected option. No animation, so no `prefers-reduced-motion` block.
  - **Badges:** override `.badge-manifest`, `.badge-okf`, `.badge-human-reviewed`, `.badge-machine-confirmed` and
    `.doc-stale-warning` in both modes, including the `body.theme-dark` variants the base uses
    (`SiteGenerator.fs:286-289, 560-571`) as smiley does: surface fill with a 1px border; manifest/okf/
    human-reviewed in cyan, machine-confirmed in yellow, stale warning in `--cyberpunk-wn`. Source/type badges
    already use `--color-badge-*`. Every pair is re-checked with the contrast script (target 4.5:1).
- Register: append `"cyberpunk", cyberpunkJson, cyberpunkCss` to `builtIns` after `smiley`.

### 2. Tests (`tests/Eru.Tests/ThemeTests.fs`)
- `discover` assertion becomes `[ "terminal"; "ledger"; "matrix"; "smiley"; "cyberpunk"; "foo" ]`.
- Mirror the smiley tests: CSS comes after the base stylesheet and before `custom.css` (marker
  `/* cyberpunk theme */`, contains `#ff2a6d` and `#05d9e8`); the theme keeps the terminal `Defaults`
  (`Themes.resolve` equals terminal's, origin `BuiltIn`).
- One extra test, as matrix has glow-in-dark logic: the generated CSS contains the `text-shadow` / grid rules only
  after a dark selector (`:root.theme-dark` or inside the dark media block), none in the light palette rules.

### 3. Docs, changelog, screenshots
- `docs/reference/theme-list.md`: table row (`cyberpunk`, monospace body with angular display headings, Table, no
  sidebar, magenta/cyan/yellow); `## cyberpunk` section (deep violet with neon, pale lavender in light; grid,
  glow and chromatic brand dark only; clipped corners; the display-font fallback; contrast tweaks; inherits the
  terminal layout; `Settings: none` and `eru config set site.theme cyberpunk`); screenshot table.
- `docs/reference/images/themes/cyberpunk-{light,dark}.png`: 1280×720, via "Regenerating the screenshots"
  (`site.theme cyberpunk` + `site.showSyncStatus: true`, copy of `index.html` with `class="theme-light|dark"`
  and `js/theme.js` removed). **Source: a throwaway sample site in the scratchpad**, the same five sample files
  as the existing theme images (check `terminal-light.png`); never real internal docs.
- Mention `cyberpunk` in `docs/reference/site-generation.md` (themes table), `docs/reference/config-file.md`
  (`site.theme` row), `docs/how-to/write-a-site-theme.md` (`eru theme list` comment),
  `skills/eru/references/site-customization.md` (two places).
- `CHANGELOG.md`: bullet under Unreleased → Added after the `smiley` one.

## Repo rules
- Stay on `main`: no branches or worktrees; run `git branch --show-current` before committing.
- Commit message `feat: Add the cyberpunk site theme`, no `Co-Authored-By` or other attribution trailers
  (AGENTS.md and memory override the harness reminder).
- Do not implement until the user says so.

## Verification
1. `dotnet test --solution eru.slnx` passes, including the updated `discover` test.
2. `eru theme list` shows `terminal`, `ledger`, `matrix`, `smiley`, `cyberpunk`.
3. `eru config set site.theme cyberpunk`, `eru site generate`, `eru site serve`: check light, dark and auto
   (OS preference), the toggle, Browse query line and popup (focus rings visible despite `clip-path`, popup not
   clipped), table, graph and doc pages, badges, the stale warning and all three sync glyphs.
4. Re-run the contrast script on every text/background pair named above in both modes; all at least 4.5:1.
5. Confirm no glow, grid or text-shadow in light mode and none on kbd, input, button or the selected option.
6. Compare against tab I in the mock; capture the screenshots from the sample site.

## Out of scope
Per-theme JS (no hook exists) and web-font loading: Chakra Petch is named in the stack but not shipped.
