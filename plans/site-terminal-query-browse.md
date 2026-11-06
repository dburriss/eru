---
status: done
---

# Plan: Terminal site look and query-line Browse

## Context
The generated site currently uses a GitHub-like look with a sidebar of checkbox facets and card
lists. Mock F (published artifact "Eru Site Mocks") replaces that with a terminal look: monospace,
hairline grid, square corners, and a single typed query line where source, status and tag filters
are tokens (`tag:dotnet status:pulled -tag:adr`). Decisions from the user:
- Replaces the default look (no opt-in theme). All pages get the new look.
- The query line is a new widget; the `facets` widget stays available through layout config.
- Mock F details: key row (source / status / tag) that opens a completion popup with live counts,
  full-width table, status shown as a glyph before the name (● pulled, ◐ cached, ○ index-only) with a
  legend, clickable source and tag cells that add themselves as tokens, keyboard hint footer.

## Findings that shape the plan
- All CSS is one string, `css` in `src/Eru.Site/SiteGenerator.fs:45`. JS is `appJs` (line 568) in the
  same file. `custom.css` loads after `style.css` and is user-owned.
- Filtering is client-side in `appJs` `render()`: iterates `[data-doc]`, OR within a facet, AND across
  facets, per-facet counts ignoring own selection. No negation, no token parser, no URL state.
- Rows expose `data-source/ext/tags/type/bundle/status` via `docAttrs` (`Views.fs:122`). `data-status`
  is the OKF lifecycle value. The pulled/cached/index-only value is `SyncStatus` and is not emitted
  on rows today (only in `data/documents.json`). The `status:` token therefore needs a new attribute.
- Layout slots are `Top | Left | Right | Main` (`SiteConfig.fs:12`). `Top` holds nav page links, not
  widgets, so the query line goes at the top of `Main`, not in `Top`. Widget registry is at
  `SiteConfig.fs:~143`; every registered widget needs a renderer (registry test).
- Browse defaults: left `facets`, right `toc`, main `title, list` (`PageLayout.fs:25-42`).
  Table view already exists (`Views.fs:225`), columns configurable via `tableColumns`.

## Steps
1. **Data contract** (`Views.fs`): add `data-sync` (pulled / cached / index-only) to `docAttrs`
   from `d.SyncStatus` (`DocFacts.statusLabel`). Keep `data-status` as lifecycle.
2. **Query widget** (`SiteConfig.fs`, `Widgets.fs`): register `query` as a main-only widget. It renders
   the query bar markup (token container, input, key buttons with value counts, popup container,
   help line) and relies on the same `Searchable` flag as `list`. Legacy `#search-input` behaviour
   (server `/api/search`, `documents.json` fallback) is kept as the free-text part of the query.
3. **Browse defaults** (`PageLayout.fs`): browse `main` = `title, query, list`; `left` = false;
   `right` = false. Other pages keep their left facet links and right toc, restyled.
   Users can restore `left: ["facets"]` or drop `query` via layout config, as today.
4. **Default view for Browse**: table with columns name, source, tags; status glyph rendered inside
   the name cell, source and tag cells rendered as buttons carrying `data-add="key:value"`
   (`Views.fs` `tableView`, glyph helper alongside `tagList`). Add legend to the list header.
   Cards view stays available via `defaultView`/`pages.<id>.view`.
5. **Client JS** (`appJs` in `SiteGenerator.fs`):
   - Parse query: `key:value`, `-key:value`, quoted phrases, free text. Keys: `source`, `status`,
     `tag`, plus existing `type`, `ext`, `bundle`.
   - Reuse the existing `render()` pipeline: build `facetState` from tokens, add negation set, keep
     per-facet counts (counts ignore that facet's own selection, as now) and feed them to the popup
     and key buttons. Existing sidebar facet checkboxes and the query line share one state so both
     work if a user enables `facets`.
   - Popup: opens on key button or typing `key:`; arrow/enter/tab/backspace/esc; counts live.
   - Click-to-add from cells; token `×` removes.
   - URL state: sync query to `?q=` with `history.replaceState`, read on load (new; deep-linkable).
   - Keyboard: `/` focuses query, `j`/`k` move, `↵` open, `a` copies `eru add`.
   - Works on `file:` (no server needed); keep `/api/search` path only for free text.
6. **CSS** (`css` string): rework tokens for the terminal look: `--font-family` becomes the mono
   stack, `--radius: 0`, hairline borders, remove shadows. Keep the existing token names
   (`--color-*`) so `custom.css` and `Theme.PrimaryColor/FontFamily` keep working; add
   `--color-ok`, `--color-warn`, `--color-accent-soft`. Restyle nav, breadcrumbs, doc-meta, source cards,
   tag list, markdown body (mono headings, readable body width kept), badges → glyphs/brackets.
   Keep `theme-light`/`theme-dark`/`prefers-color-scheme` triple intact and give both palettes the
   same care. Add styles for query bar, tokens, popup, key buttons, legend, footer hints.
7. **Responsive**: query bar wraps, table keeps name + tags and hides source below ~640px.
8. **Docs/changelog**: update `docs/reference/site-generation.md` (widgets, views, filter contract,
   client features, serve-vs-static table), `docs/how-to/customize-the-generated-site.md` (new
   variables, restoring the sidebar), `skills/eru/references/site-customization.md`;
   `CHANGELOG.md` Unreleased with a breaking-change note (new default look and Browse layout).
   Docs changes only after the implement signal is given, as per project rule.

## Tests (`tests/Eru.Tests/`)
- Update `SiteGeneratorPagesTests.fs:41-50` (Browse no longer has `data-facet-mode="filter"` by
  default; add a case with `left: ["facets"]` that does), and `:255-277` (`data-sync` present,
  `query` markup present, `app.js` contains the token parser markers, `style.css` still has
  `facet-empty`).
- `SiteWidgetTests.fs`: render test for `query`; registry test covers renderer presence;
  `SiteConfigTests.fs`: `query` valid in `main`, rejected in `top`/`left`/`right`.
- `SiteViewTests.fs`: `data-sync` and glyph in the name cell, clickable `data-add` cells.
- Keep existing facet-widget tests as-is (widget unchanged).

## Verification
1. `dotnet build` and `dotnet test --solution eru.slnx`.
2. Generate a site against a real config (`dotnet run --project src/Eru.Cli -- site generate`) and
   open `index.html` over `file:` and via `eru site serve`; exercise: typing `tag:`, popup counts,
   `-tag:` exclusion, key buttons, click a tag cell, `×` on a token, `?q=` reload, `/` and `j/k`.
3. Check light and dark (OS setting and toggle), phone width, a document page, Sources, Tags, Graph.
4. Confirm an existing `custom.css` with `--color-primary` still applies.

## Open risks
- Replacing the default affects every user's `custom.css`; mitigated by keeping token names.
- Query grammar is new surface; keep it small (no OR, no parentheses) and document it.
