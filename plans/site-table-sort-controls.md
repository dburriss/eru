# Plan: Visible, accessible, value-aware sort controls on site tables

## Context
Generated-site tables (`table.doc-table`) already sort on header click, but: no affordance until first click, mouse-only,
and the JS compares rendered cell text (so dates, trust tiers, tag lists and sync glyph cells sort wrongly). User wants
column sort controls on all table views, closing three gaps: visible controls, keyboard/a11y, smarter sort values.

## Changes (all under src/Eru.Site)

1. **Views.fs `tableView` (~line 263)**
   - Header: render `<th scope="col" data-col data-sortable aria-sort="none">` wrapping a `<button type="button" class="sort-btn">label<span class="sort-ind" aria-hidden="true"></span></button>`.
   - Cells: add `data-sort="<key>"` to each `<td>` using `FilterEngine.sortKey c d` (HTML-escaped). Omit the attribute when `None`.
     This reuses the existing server-side key logic (UTC timestamps, verified tiers, status labels), so the page and client agree.
     Columns `sortKey` doesn't know fall back to text in JS. Check Views.fs compiles after FilterEngine in the fsproj (it is listed before Views.fs? verify order).

2. **SiteGenerator.fs JS `enableTableSort` (~line 1159)**
   - Bind click to the `.sort-btn` (native Enter/Space support), not the `th`.
   - Compare `td.dataset.sort ?? textContent`; rows with no `data-sort` on a column that has any sort keys go last in both directions (mirrors `FilterEngine.sort`).
   - Set `aria-sort` to `ascending`/`descending`/`none` on the `th`; keep `data-dir` for CSS.
   - Use a stable sort with original-index tiebreak; cycle asc → desc on repeat click.
   - Replace `th.style.cursor` with CSS.

3. **SiteGenerator.fs CSS (~line 543) and Themes.fs (~123)**
   - `.sort-btn`: unstyled button inheriting th typography, cursor pointer, focus-visible outline.
   - `.sort-ind` shows a muted `⇅` by default, `▲`/`▼` when `th[aria-sort=ascending|descending]`; hover raises contrast.
   - Remove the old `th[data-dir]::after` rules. Check the ledger theme's uppercase th styles still apply to the button (Themes.fs and SiteGenerator.fs ~660).

4. **Docs**: `docs/reference/site-generation.md` (line ~67 sortable headers) — note visible indicators, keyboard use, value-aware sort. Add a CHANGELOG entry under Unreleased (Keep a Changelog).

## Tests (tests/Eru.Tests)
- `SiteViewTests.fs`: table output contains `.sort-btn` and `aria-sort="none"` per header, and `data-sort` on cells (e.g. `staleAfter` gives an ISO timestamp, `verified` gives tier digit, missing key gives no attribute).
- `SiteGeneratorPagesTests.fs`: generated `app.js` contains the `aria-sort` handling.
- Update existing assertions that match the exact `<th ... data-sortable>` string, if any.

## Verification
- `dotnet test --solution eru.slnx`
- Generate a site (`dotnet run --project src/Eru.Cli -- site ...`), open index.html in a browser: indicators visible on load; click/Tab+Enter sorts; dates and verified/status columns order correctly; blank cells last; dark and light themes OK.

## Notes
- Implementation starts only when the user says "implement". Stay on the current branch (main); no new branches.
