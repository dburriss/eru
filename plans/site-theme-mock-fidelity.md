---
status: done
---

# Plan: refine site themes to match the mocks

## Context
The "Eru Site Mocks" artifact (https://claude.ai/artifact/RrsoEuCM2mbpQ8ST7ELx5T) shows the filled "on" states and bordered buttons that the real themes lack. The largest gap is that buttons and labels never fill. In the mocks:
- Active row tags (`.tg button.on`) are filled.
- The key button for the facet being completed (`.keys button.on`) is filled.
- Row tags and source values are bordered buttons at rest.

The real site has none of this. `.add-filter` is borderless text, `.q-key` has no active state, and nothing marks a tag as already in the query. The header brand marks also differ from the mocks. Scope, as confirmed: look plus logo marks, and all three on-state items.

## Findings
- Themes are CSS strings in `src/Eru.Site/Themes.fs` (matrix, smiley, cyberpunk, ledger). The shared base is the `css` string in `src/Eru.Site/SiteGenerator.fs:59-689`. Base `.add-filter` is at `:661`, `.q-key` at `:632`, `.q-tok` at `:603`.
- The generated JS in `SiteGenerator.fs` (around `:1045` and `:1084-1150`) builds the `.q-key` buttons and handles `[data-add]` clicks. There is no code that marks a button as on.
- Brand: `.nav-brand` in `HtmlTemplates.fs:54` is plain text "eru". The `_` is a `::after`. Smiley has a `::before` mark (`Themes.fs:203`). Cyberpunk has the chromatic text-shadow.
- `tests/Eru.Tests/ThemeTests.fs` asserts on CSS substrings, so it needs updating alongside.

## Plan

### 1. Add on-state hooks (small JS and base CSS, `SiteGenerator.fs`)
- In the query render step, after tokens are parsed, toggle `.q-on` on each `[data-add]` button whose `key:value` matches a non-negated token. Set `aria-pressed` too. Re-run it whenever the query changes.
- Toggle `.q-on` on the `.q-key` button whose key matches the one currently being typed. This includes the existing click handler path.
- Base CSS: give `.add-filter` a resting 1px `--color-border` outline. Add `.add-filter.q-on` and `.q-key.q-on` with a filled `--color-primary` background and `--color-bg` text. Keep hover as is.
- Check whether `Views.fs` (`addButton`, `:150`) needs any markup change. It probably doesn't.

### 2. Per-theme fills (`Themes.fs`), matching mock colours
- **matrix:** on-state is inverted (`--color-primary` fill, `--color-bg` text, no text-shadow). Resting tags are bordered boxes.
- **smiley:** `.add-filter` pill. `.add-filter.q-on` is orange-filled with white text (the mock's `--hot`), and `.q-key.q-on` is blue-filled. This theme already has pill styling at `:230-231` and `.q-key.q-on` at `:222`, so reuse those rules.
- **cyberpunk:** `.add-filter.q-on` is magenta-filled with white text. `.q-key.q-on` is cyan-filled (`:316` exists), and hover is cyan (`:319`). Keep the clipped corners.
- **ledger:** soften to a tinted `--color-accent-soft` on-state with a primary border. It isn't in the F-based mocks, so this is a judgment call, and I will flag it.
- **terminal (base):** the base rules above cover it.

### 3. Logo marks as real HTML (overridable)
Per your idea, the brand becomes markup instead of CSS pseudo-elements (`::before`/`::after`, which a user can't replace). The footer already works this way: it takes html/text items from the layout config (`PageLayout.fs:32-60`, `HtmlTemplates.fs:42`). The brand follows the same pattern.
- `HtmlTemplates.fs:54`: replace the hard-coded `<a class="nav-brand">eru</a>` with a brand slot. The default html is
  `<a class="nav-brand" href="{p}index.html"><span class="brand-mark" aria-hidden="true"></span><span class="brand-name">eru</span><span class="brand-cursor" aria-hidden="true">_</span></a>`.
  Anyone can swap it through layout config (the same html/text item mechanism as the footer) or hide the mark parts with `custom.css`.
- Drop the brand `::before` and `::after` rules from the base (`SiteGenerator.fs:142`) and from ledger, smiley and cyberpunk (`Themes.fs:78-82`, `:203-207`, `:294`). Each theme then styles `.brand-mark`, `.brand-name` and `.brand-cursor` with ordinary selectors.
- Per theme, matching the mocks:
  - **terminal:** the cursor is shown, the mark is hidden.
  - **matrix:** the mark shows `>` in the primary colour and the cursor is shown.
  - **smiley:** the mark is the round orange smiley (22px, white eyes and smile, CSS background SVG as in the mock) and the cursor is hidden.
  - **cyberpunk:** uppercase Chakra Petch name with the magenta/cyan split on `.brand-name` only, and a yellow cursor.
  - **ledger:** the mark is a small primary-coloured square and the cursor is hidden.
- Decided: the brand is a layout-config item, like the footer. When none is set, the default html above is used. Themes only style the spans.

### 4. Tests and docs
- Update the `.nav-brand` assertions (`ThemeTests.fs:182`) and add a template test that the default brand html and a configured override are emitted.
- Extend `ThemeTests.fs` with substring assertions for `.add-filter.q-on` and `.q-key.q-on` in the base CSS and in each theme's CSS. Add a JS assertion that `q-on` is toggled on `[data-add]` buttons.
- Regenerate the screenshots in `docs/reference/images/themes/` via `samples/theme-site/run.sh`. Update `docs/reference/theme-list.md` if the descriptions change. Add a CHANGELOG entry if the repo keeps one.

## Verification
- `dotnet test tests/Eru.Tests --filter ThemeTests` (and the full suite).
- `samples/theme-site/run.sh`, then `eru site serve` for each theme in light and dark:
  - Add `tag:dotnet`. The `#dotnet` row buttons fill and the `tag` key fills while typing `tag:`.
  - Remove the token. The fills clear.
  - Check hover and focus states, and compare against the mock tabs G, H and I.
- Compare the header brand against the mocks and refresh the screenshots.

## Outcome
- `Views.fs` needed no change. Ledger rows keep borderless `#tag` text at rest (as in mock A) and only gain the soft tinted on-state.
- The theme-list screenshots were regenerated (1280×720, light and dark for all five themes) from `samples/theme-site`; the tag column is 28ch wide so bordered tags do not wrap as much.

- Follow-up: `ledger` sets `layout.toolbar: false` (facets replace the query line; the plain search box stays), and `Filterable` is now true only on a Browse page that places `query`, so row tags and sources are not dead buttons without it.

- Follow-up: `smiley` ships `defaultView: "cards"`, so Browse is a card grid under the query line, with the `facets` sidebar on the left.

## Notes
- Per AGENTS.md and the user's memory: no code until you say "implement", and no branch changes. Work stays on `main`. Commits carry no attribution trailers.
