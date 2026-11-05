# Plan: Dynamic facet counts on the Browse page

## Context

User feedback on the generated site: the Browse sidebar's facet counts never change when filters are applied. Picking a value such as the "pricing" service should leave fewer tags relevant, but every tag keeps its total count.

Today counts are server-rendered totals (`facetsWidget` in `src/Eru.Site/Widgets.fs`). The client script (`appJs`, an embedded string in `src/Eru.Site/SiteGenerator.fs:565-749`) only shows and hides document rows.

Goal: standard faceted-search counts on the Browse page.

Decisions already made with the user:
- Zero-count values are **dimmed**, not hidden, so the list does not jump around. A checked value is never dimmed.
- The **File Extensions** facet gets counts too (it has none today).
- Out of scope: hiding zero-count values, URL/hash filter state (none exists today), a Status facet.

## Current setup

- `render()` (`SiteGenerator.fs:588-617`) loops over `[data-doc]` rows. Facet selections are OR within a facet and AND across facets, and AND with the search text.
- Search text is async: `/api/search` (sets `matchedPaths`), falling back to `documents.json`, falling back to `textContent` matching on `file:`. `render()` is re-run when results arrive.
- Rows carry `data-source|ext|tags|type|bundle|status` as JSON arrays (`docAttrs`, `Views.fs:121-122`). `valuesOf(el, facet)` parses them. Facet keys equal the `data-*` names.
- `upgradeFacet(ul)` (`:676-701`) turns `a[data-value]` links into checkboxes and re-appends the existing `.count` span. Extension items have no count span (`Widgets.fs:119-120`, `item e "#" None`).
- `.count` CSS (`SiteGenerator.fs:159`) is shared with the title count `[data-doc-count]`. There is no dimmed-facet style.
- Facet-link mode (every non-Browse page) is static and unaffected.

## Design

For each facet `f` and value `v`, the count is the number of docs where the search text matches, every facet **other than `f`** passes, and `v` is among the doc's `f` values.

A facet's own selection therefore does not shrink its own counts, so sibling values stay comparable. A multi-valued facet (tags) counts a doc once per tag.

## Approach

1. **`appJs` in `src/Eru.Site/SiteGenerator.fs`**
   - In `render()`, compute `textOk` once per doc and `othersOk(f)` per facet. Accumulate `counts[f][v]` in the same loop as the existing visibility logic.
   - After the loop, write the counts into each facet `li .count`. Toggle a `facet-empty` class where the count is 0 and the checkbox is unchecked.
   - In `upgradeFacet`, create a `.count` span when the `li` has none (extensions). Mark facet spans so they can't be confused with `[data-doc-count]`.
   - Call `render()` once on load so initial counts come from the DOM (matching today's server totals, plus extensions).
2. **CSS** near `SiteGenerator.fs:159`: `.sidebar-section li.facet-empty { opacity: 0.45; }`, checked against the existing `body.theme-dark` pattern.
3. **`Widgets.fs`**: no required change. Extension counts are filled by JS, so no per-extension count is added to the model.
4. **Check extension values**: confirm `AllExtensions` (`IndexBuilder.fs`) and `data-ext` use the same format (leading dot), since matching is exact against `data-value`. Fix at the source if they differ.
5. **Docs**: `docs/reference/site-generation.md` (app.js description, facets widget, filter contract and feature table sections). Describe the faceted-count rule and that extensions now have counts. `CHANGELOG.md` `[Unreleased]` → `### Changed`, one prose bullet in the existing style with a docs link.

## Critical files

- `src/Eru.Site/SiteGenerator.fs` (`appJs`, CSS)
- `tests/Eru.Tests/SiteGeneratorPagesTests.fs`
- `docs/reference/site-generation.md`, `CHANGELOG.md`

## Tests

Existing tests assert substrings of the generated HTML and JS; there is no JS runtime in the test project.

- Extend "browse documents carry the filter contract and facets become filters" (`SiteGeneratorPagesTests.fs:264-276`) to assert `js/app.js` contains the count-update code (e.g. `facet-empty`) and the CSS contains the dim rule.
- `SiteWidgetTests.fs:120-137` stays unchanged unless the widget markup changes.
- The counting logic cannot be unit tested from F#. Keep the JS function small and verify it manually (below).

## Verification

1. `dotnet build`, then `dotnet test --solution eru.slnx`.
2. Generate and serve a site with several sources and tags, then check on Browse:
   - Initial counts equal the old totals, and extensions now show counts.
   - Selecting a source reduces tag, type and bundle counts, while that source's siblings keep their counts.
   - Selecting two values in one facet (OR) widens results, and counts in other facets update.
   - Typing a search term narrows all counts, and clearing it restores them.
   - Zero-count values are dimmed, and a checked value is never dimmed.
   - Both the `file:` build and the served build (search via `/api/search`), in both themes.
3. Confirm non-Browse pages still show static counts.
