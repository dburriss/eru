---
status: done
---

# Plan: Switchable site themes (`theme: terminal | ledger | <custom>`)

## Context
`eru site generate` has one hard-wired terminal look (CSS string `css` in `src/Eru.Site/SiteGenerator.fs`) plus a user `custom.css` override. We want a `site.theme` config key that selects a bundled look, with a second built-in (`ledger`, from the "Eru Site Mocks" artifact, tab A) and the ability to install custom themes. A theme is `theme.json` (default `site` config such as layout slots) plus `theme.css`. The user's own config and `custom.css` always win on top.

## Decisions (confirmed)
- **Install**: custom themes are plain folders. Resolve `theme: x` in order: built-in, then `.eru/themes/x/`, then `~/.config/eru/themes/x/`. No new CLI at first.
- **Bundle**: `theme.json` (site-config defaults) + `theme.css`.
- **Rename internals**: `ThemeOverrides` / `GenerateOptions.Theme` (colour/font overrides) become `StyleOverrides` / `Style`. The `theme-light`/`theme-dark` class names stay.
- **Ledger**: follow artifact tab A. Dense rows instead of cards, dot status, checkbox facet sidebar with dimmed zero counts, ⌘K-style search bar, indigo accent (#4f46e5 / #8b84ff), Geist type, light and dark via `light-dark()`.

## Layering (lowest to highest)
1. Built-in base CSS (structure only: grid, regions, widgets), then the theme's `theme.css`, then generated colour/font overrides, then `custom.css`.
2. Config: `PageLayout.baseLayout` < theme.json `site` < global config < local config < env. Implemented as `SiteSettings.merge themeDefaults userSettings` before `validate`.

## Changes
1. **Domain** (`src/Eru.Domain/SiteConfig.fs`, `Config.fs`)
   - Add `theme: string option` to `SiteSection` and `SiteSettings`, parse it, and merge it (local wins).
   - In `Config.fs` (~793-807), after resolving the name, load the theme's defaults and merge them under user settings. Unknown theme gives a clear error listing the available themes.
   - Register `site.theme` in `ConfigCmd.fs` so `eru config set site.theme ledger` works. `ERU__SITE__THEME` comes through the existing env layer.
2. **Theme resolution** (new `src/Eru.Site/Themes.fs`, plus a `Paths.fs` entry for the user themes dir)
   - `Theme = { Name; Css; Defaults: SiteSection }`. `resolve name` checks built-ins, project, then user.
   - Use `/`-based `PathJoin`/`PathUtil` in domain code (AGENTS.md). File reads go through `Deps`, not `System.IO` directly.
3. **Built-ins**: split the existing `css` literal into base structure and `Terminal.css`. Add `Ledger.css` and `ledger` defaults (e.g. toolbar search, left facets). Embed as resources or literals, matching how `css` is held now.
4. **Generator** (`SiteGenerator.fs:1805-1826`): write `style.css` as base + theme css + style overrides. `HtmlTemplates.fs` link order is unchanged. Default theme is `terminal`, so existing sites render identically.
5. **`eru site serve`** (`src/Eru.Serve/SiteServeServer.fs`): verify it goes through the same CSS path (the explorer found no `--custom-css` handling there). Fix it if not.
6. **Rename** `ThemeOverrides`/`Theme` to `StyleOverrides`/`Style` in `SiteGenerator.fs:21-25` and call sites (`SiteGenerateCli.fs`, `Args.fs`).

## Tests (`tests/Eru.Tests/`, register in `Eru.Tests.fsproj`)
- `SiteConfigTests.fs`: parse, merge and validate `theme`; the error for an unknown theme.
- New `ThemeTests.fs`: resolution order (built-in, project, user), a malformed theme.json, theme defaults sitting under user layout.
- `SiteGeneratorPagesTests.fs`: `style.css` contains the theme CSS in the right order, with `custom.css` still last. Default output is unchanged. Extend the `:root` override test (`:272`) to cover `ledger`.
- `ConfigCmdTests.fs`, `EnvConfigTests.fs`: `site.theme`.

## Docs
- `docs/reference/config-file.md` (add a `site.theme` row), `docs/reference/site-generation.md`, `docs/reference/environment-variables.md`.
- New how-to for writing or installing a theme (`docs/how-to/`), and an update to `customize-the-generated-site.md`, which currently says "theme" for custom CSS.
- `skills/eru/references/site-customization.md`, `CHANGELOG.md` under Unreleased.
- New design note in `plans/` (repo convention), e.g. `plans/site-themes.md`.

## Verification
- `dotnet build` and `dotnet test`.
- Generate a site with each theme, open it, and compare to the mock. Check light and dark, and that `custom.css` and a `:root` override still win.
- Drop a custom theme into `.eru/themes/foo/`, set `theme: foo`, and confirm it applies. Test a missing theme for the error message.

## Added after review
- **Ledger is mostly config + CSS**: the `facets` widget was kept in the terminal-look commit (1d68f54) and is still registered (`SiteConfig.fs:174`, `Widgets.fs:104`, `FacetFilter` mode on Browse, dimmed zero counts via `.facet-empty`). Views `cards`, `table` and `list` also exist. So ledger's theme.json would set `layout.left: ["facets"]`, `layout.toolbar: false` (search sits in the nav bar instead), and `defaultView: list` (or `table`). The CSS supplies the dot status, indigo accent and row styling.
- **The ledger row is a view, not a widget**: widgets are single items placed in page regions (`SiteVocabulary.widgets`), while views (`cards`, `list`, `table`; `Views.fs:263`) render a whole list of documents. The mock's row (title, description line underneath, tags and status on the right) sits between the existing `list` view (title + meta only, `defaultListShow`) and the `table` title cell (glyph + name + description, `Views.fs:233`). Plan: add a `rows` view to `SiteVocabulary.views` and `Views.renderers`, reusing the table title-cell helper, and have ledger's theme.json set `defaultView: rows`. (Confirmed: new `rows` view, not an extension of `list`.)
- **Remaining ledger gaps to check during implementation** (new generator work, only if needed, and you've OK'd it): the ⌘K search box in the top nav (the `query` widget only goes in Toolbar/Left/Right today, so allow it in Nav or restyle the toolbar), and the dot-status markup (the terminal look uses a glyph before the name, `data-sync`, which CSS can restyle into a dot).
- **`eru theme list`**: new small command (follow the `eru site`/`eru source` CLI pattern in `Args.fs`) listing built-in and discovered themes with where each resolves from (built-in, project, user). Document in `docs/reference/cli.md`, add a test.

## Open points
- Order of work: (1) rename, (2) theme mechanism + terminal as built-in, (3) `eru theme list`, (4) ledger CSS and config, (5) any ledger view/widget work.
- AGENTS.md says not to add `Co-Authored-By` trailers and not to write code without the word "implement". Follow it at implementation time.

## Implementation notes (as built)
- **The base stylesheet is the terminal look.** The plan proposed splitting the `css` literal into a structural base and `Terminal.css`. As built, `terminal` is the base itself and adds no CSS, so default output is byte-for-byte unchanged. `ledger` and custom themes override on top (`Themes.fs`).
- **Theme resolution lives in the generator, not in `Config.merge`.** The config merge stays pure; `SiteGenerator.generate` resolves `site.theme` through `Themes.resolve deps` and lays the user's settings over the theme's defaults with `Themes.apply` before validating. `site.theme` itself is a plain string in `SiteSection`/`SiteSettings`, validated as a safe folder name.
- **Ledger's search stays in the toolbar.** The `query` widget was not allowed in `nav`; ledger restyles the toolbar query line instead. Moving it into the nav bar is a follow-up.
- **`rows` view**: added to `SiteVocabulary.views` and `Views.renderers`, with base CSS so it also works under `terminal`.
- `eru site serve` goes through `SiteGenerator.generate`, so themes apply there too; edits to theme files are not watched, only the config.
