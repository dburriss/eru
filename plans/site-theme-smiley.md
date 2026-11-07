---
status: done
---

# Plan: add the `smiley` built-in site theme

## Context
`eru site` ships three built-in themes: `terminal` (base stylesheet), `ledger` and `matrix`
(commit 443d5dc). The "Eru Site Mocks" artifact has direction H · Hot & Cold: the terminal layout
(F) in Coolblue blue (#0090e3) and orange (#ff6600), with friendlier sans type and rounded pills.
The user named it **`smiley`** (the Coolblue smile) and wants it as a built-in selectable with
`site.theme: smiley`. It is CSS-only, like `matrix`: no domain, CLI or config changes.

## Approach (copy the pattern of commit 443d5dc)

### 1. `src/Eru.Site/Themes.fs`
- `smileyJson`: description only, no `site` defaults (terminal layout kept).
- `smileyLight` / `smileyDark` token strings covering the same tokens as `matrixLight`/`matrixDark`
  (`--color-bg|surface|border|text|muted|primary|accent-soft|ok|warn|badge-bg|badge-text|graph-*`).
  Values from the mock's `.CB` block:
  - light: bg `#ffffff`, surface `#f5f7f9`, border `#dfe4e8`, text `#1a1a1a`, muted `#6b7480`,
    primary (blue) `#0090e3`, ok `#00a651`, warn/hot (orange) `#ff6600`, accent-soft `#e6f4fc`
  - dark: bg `#0a1724`, surface `#112233`, border `#21384e`, text `#e9f2f9`, muted `#8ea6bb`,
    primary `#4cb8ff`, ok `#3fd47a`, warn/hot `#ff8a3d`, accent-soft `#0f2d45`
- `smileyCss`, marker comment `/* smiley theme */`, same four palette selectors in the same order as
  `matrixCss` (`:root`, OS-dark media query, `:root:where(.theme-light)`, `:root:where(.theme-dark)`):
  - `--font-family` to the sans stack used by `ledgerCss` (mono stays via `--font-mono` for paths,
    tokens, keys); `--radius: 6px`
  - nav: solid blue band (`--color-primary` in light; navy `#0a1724` with border in dark) with white
    text, a round orange dot before the brand (`.nav-brand::before`; check the nav/brand selectors at
    `SiteGenerator.fs:132` and `HtmlTemplates.fs:53`), active link white
  - query line: 2px blue border, 8px radius; `.q-tok` as pills with the soft-blue fill; `.q-key`
    pills, `.q-key.q-on` filled blue; `.q-pop` rounded with soft shadow; `.q-opt.q-on` blue with
    white text (reuse the ledger rules at `Themes.fs:95-104` as the starting point)
  - selected row: `.q-sel` inset bar in orange (`--hot`); active tag buttons filled orange
  - `th` letter-spacing; row/card names slightly larger
  - badges: override `.badge-manifest`, `.badge-okf`, `.badge-human-reviewed`,
    `.badge-machine-confirmed`, `.doc-stale-warning` so they match the blue/orange palette in both modes
  - `kbd` in the nav band stays readable (white border/text)
- Append `"smiley", smileyJson, smileyCss` to `builtIns` after `matrix`.

### 2. Tests (`tests/Eru.Tests/ThemeTests.fs`)
- `discover` assertion becomes `[ "terminal"; "ledger"; "matrix"; "smiley"; "foo" ]`.
- Add tests mirroring matrix's: CSS follows the base stylesheet and precedes `custom.css`, contains
  the marker and `#0090e3`; theme keeps terminal layout defaults (`Themes.resolve` equal to terminal's
  `Defaults`).

### 3. Docs, changelog, screenshots
- `docs/reference/theme-list.md`: table row, `## smiley` section (blue header, orange accents, sans type
  with mono for paths/tokens, rounded pills; inherits terminal layout), screenshot table.
- `docs/reference/images/themes/smiley-{light,dark}.png`: 1280×720, via the "Regenerating the
  screenshots" steps in that file (`site.theme smiley` + `site.showSyncStatus: true`).
- Mention `smiley` in: `docs/reference/site-generation.md` (themes table), `docs/reference/config-file.md`
  (`site.theme` row), `docs/how-to/write-a-site-theme.md` (`eru theme list` comment),
  `skills/eru/references/site-customization.md` (two places).
- `CHANGELOG.md`: bullet under Unreleased → Added after the `matrix` one.

## Repo rules
- Stay on `main`: no branches or worktrees; run `git branch --show-current` before committing.
- Conventional Commit: `feat: Add the smiley site theme`. No `Co-Authored-By` or other attribution
  trailers (AGENTS.md and memory override the harness reminder).

## Verification
1. `dotnet test --solution eru.slnx` passes, including the updated `discover` test.
2. `eru theme list` shows `terminal`, `ledger`, `matrix`, `smiley`.
3. `eru config set site.theme smiley`, `eru site generate`, `eru site serve`: check light, dark and
   auto, the toggle, Browse query line/popup, table, graph and doc pages, badges and the stale warning
   for contrast.
4. Compare against tab H in the mock artifact; capture the doc screenshots from that site.

## Out of scope
Per-theme JS or web-font loading (the mock's Instrument Sans/Geist fonts): the system sans stack is used.
