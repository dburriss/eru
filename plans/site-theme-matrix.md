---
status: in-progress
---

# Plan: add the `matrix` built-in site theme

## Context
`eru site` ships two built-in themes: `terminal` (the base stylesheet, default) and `ledger`.
The "Eru Site Mocks" artifact has a third direction, G · Matrix: the Terminal (F) layout in
phosphor green, with a faint glow and scanlines. This adds it as a built-in theme selectable with
`site.theme: matrix`. No domain, CLI or config changes are needed, because the theme name is only
validated as a safe folder name (`SiteConfig.fs:670`).

## Approach
Matrix reuses the terminal layout, so the theme is CSS-only: a palette plus a few overrides, built
the same way as `ledgerCss`.

### 1. `src/Eru.Site/Themes.fs`
- Add `matrixJson`: description only, no `site` defaults, so the terminal layout is kept
  (like `terminalJson`, line ~36).
- Add `matrixLight` / `matrixDark` token strings covering every token in `ledgerLight` /
  `ledgerDark` (`--color-bg|surface|border|text|muted|primary|accent-soft|ok|warn|badge-bg|badge-text|graph-*`).
  Values come from the mock's `.M` block:
  - dark: bg `#020a04`, surface `#06170b`, border `#0f3a1a`, text `#3dff6e`, muted `#1f9a42`, accent `#7dff9c`
  - light: bg `#f2faf3`, surface `#e2f3e5`, border `#a9d8b2`, text `#0a4d1c`, muted `#3d7a4b`, accent `#00872a`
- Add `matrixCss` with the same four selectors as `ledgerCss` (`:root`, the `prefers-color-scheme: dark`
  media query, `:root:where(.theme-light)`, `:root:where(.theme-dark)`), then:
  - `--font-family` set to the mono stack, so everything is monospace
  - dark mode only: `text-shadow` glow and the repeating-linear-gradient scanline overlay on the
    header and main area, both off in light mode
  - `body.theme-dark .badge-*` and `.doc-stale-warning` overrides, so badges stay green-on-green
    and readable
  - status relies on the glyph shapes already in the terminal look, not colour alone
  - a leading marker comment `/* matrix theme */`
  - `@media (prefers-reduced-motion)` guard if any animation is added (none planned)
- Append `"matrix", matrixJson, matrixCss` to `builtIns` (~line 148), after `ledger`.

### 2. Tests (`tests/Eru.Tests/ThemeTests.fs`)
- Update the `discover` assertion (~line 121) to `[ "terminal"; "ledger"; "matrix"; "foo" ]`.
- Add tests mirroring ledger's, using `SiteTestSupport` helpers:
  - the matrix CSS comes after the base CSS and before `custom.css`
  - the marker comment is present
  - the theme adds no `site` defaults
  - a project theme folder named `matrix` cannot shadow the built-in
  - `site.theme: matrix` resolves with no error

### 3. Docs and changelog
- `docs/reference/theme-list.md` (main doc update, do first):
  - add a `matrix` row to the themes table (~lines 16-19)
  - add a `## matrix` section modelled on the `ledger` and `terminal` ones: description, what it
    looks like in light and dark (pale mint vs black/phosphor green, glow and scanlines in dark
    only), the default view/layout (none, inherits terminal), and how to select it (`site.theme: matrix`)
  - add screenshots `docs/reference/images/themes/matrix-{light,dark}.png` (1280×720), captured
    with the file's "Regenerating the screenshots" steps
  - update any intro text that names only terminal and ledger
- `docs/reference/site-generation.md` (~line 253 themes table)
- `docs/reference/config-file.md:227` (`site.theme` row)
- `docs/how-to/write-a-site-theme.md` ("terminal, ledger and anything installed")
- `skills/eru/references/site-customization.md:32-43`
- `CHANGELOG.md`: bullet under `## [Unreleased]` → `### Added` next to the existing "Site themes" entry

## Repo rules to follow
- Work on `main`: no new branches or worktrees. Run `git branch --show-current` before committing.
- Conventional Commits (`feat: Add the matrix site theme`); no `Co-Authored-By` or other attribution
  trailers, because AGENTS.md overrides the harness reminder.
- Only implement once the user says "implement".

## Verification
1. `dotnet test --solution eru.slnx` passes, including the updated `discover` test.
2. `eru theme list` shows `terminal`, `ledger`, `matrix`.
3. Generate a site with `site.theme: matrix` and open it with `eru site serve`. Check light, dark and
   auto (OS preference), the toggle, facet/table/graph pages, badges and the stale-doc warning for
   contrast.
4. Compare against tab G in the mock artifact.
5. Capture the doc screenshots from that site.

## Out of scope
Per-theme JS (such as a digital-rain canvas): there is no hook for it today (`HtmlTemplates.fs:69-72`).
