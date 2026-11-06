---
status: done
---

# Plan: Page sections for the generated site (nav, top, toolbar, footer) and a movable query line

## Context
The query line is a `main`-only widget, so Browse lists it between `title` and `list` and it cannot be
moved. Putting it in a band above the columns would place it above the page title, which reads wrong.
The layout needs more sections, in this order from the top of the page:

```
nav        page links (today's `top`)
top        full-width widget band: breadcrumbs, title, ...
toolbar    the query line
left | main | right
footer     full-width widget band
```

## Decisions
- `top` is renamed `nav`. A new `top` slot takes widgets only. **Hard rename**: an old `top` holding
  page ids or links fails with a message that points to `nav`. No alias.
- New slots: `top`, `toolbar` (general, query only for now) and `footer`. `main` stays and is still
  set per page or under `site.document`.
- Defaults: **Browse only** uses the new bands: `top = [title]`, `toolbar = [query]`,
  `main = [list]`. Every other page keeps breadcrumbs and title in `main`; its `top` and `toolbar`
  are empty.
- `footer` holds a link to the eru docs on **every page** by default:
  `{ "label": "eru docs", "url": "https://github.com/dburriss/eru/tree/main/docs" }`. Like the side
  slots, it takes widgets and links. Replace it by setting `footer`, or remove it with
  `"footer": false`. (The URL is the docs folder in the repository; change it if the docs are
  published elsewhere.)
- `query` is valid in `toolbar`, `left` and `right`, not in `main`, `top`, `nav` or `footer`. At most
  one `query` per page, because the widget uses fixed ids (`search-input`, `search-container`).
- Widget validity (new registry):
  - `breadcrumbs`, `title`, `stale`: `top`, `main`.
  - `meta`, `verified`, `generated`, `description`, `tags`, `cli`, `links`: `top`, `main`, `left`,
    `right`, `footer`.
  - `content`, `list`: `main`. `toc`, `facets`: `left`, `right`. `related`, `graph`: `main`, `left`,
    `right` (unchanged).
  - `query`: `toolbar`, `left`, `right`.

## Steps
1. **Slot model** (`src/Eru.Domain/SiteConfig.fs`)
   - `SlotName` becomes `Nav | Top | Toolbar | Left | Main | Right | Footer`. Add the new fields to
     `Layout`, and to `empty`, `get`, `set`, `overlay` and `slotNames`.
   - Parse and allow `nav`, `top`, `toolbar` and `footer` under `site.layout`, `pages.<id>.layout`,
     `document.layout` and rules. `nav` takes page ids and links as `top` did; `footer` takes widget
     names and links, like the side slots; `top` and `toolbar` take widget names only; `false`
     disables any slot.
   - An old `top` entry that is a page id or link fails with "page ids and links moved to 'nav'".
2. **Widget rules**: replace the registry as above. Reject `query` in the wrong slot with the
   existing "not valid in" message, and reject a layout that places `query` more than once, naming
   both slots.
3. **Defaults** (`src/Eru.Site/PageLayout.fs`): `nav` gets the built-in pages as `top` did. Browse gets
   the band defaults above. The page-kind `defaultMain` for Browse drops `title` and `query`. Every
   page kind gets the docs link in `footer`.
4. **Rendering** (`src/Eru.Site/HtmlTemplates.fs`, `Widgets.fs`)
   - Render the nav, then a `region-top` band, then a `region-toolbar` band, then the columns, then a
     `region-footer` band. Omit any empty band. Links in the footer render as a plain horizontal
     list; the link item rendering already used by the side slots is reused.
   - Rename the nav slot use in `Widgets.fs` (`ctx.Slots.Top` becomes `ctx.Slots.Nav`).
   - `listWidget` shows its plain search box only when no slot contains `query`. Today it checks
     `Main`.
5. **CSS** (`css` string in `src/Eru.Site/SiteGenerator.fs`): full-width styling for the three bands
   (the footer is a quiet muted line with a top hairline),
   with spacing that matches the columns, and a compact variant of the query for `left` and `right`
   (tokens and key buttons wrap, the popup fills the column, the legend and hints stack).
6. **Docs**
   - `docs/reference/site-generation.md`: slots table, widgets table, query-line section.
   - `docs/how-to/customize-the-generated-site.md`: rename `top` to `nav` in the examples; how to
     move the query line.
   - `docs/reference/config-file.md`, `docs/reference/environment-variables.md` and
     `skills/eru/SKILL.md` where they mention `top`; `skills/eru/references/site-customization.md`.
   - `CHANGELOG.md` Unreleased: Changed (breaking) for `top` becoming `nav`; Added for the new slots
     and the movable query line.
7. **Tests** (`tests/Eru.Tests/`)
   - Slot parse and overlay for every new slot.
   - The old `top` page-id and link config is rejected with the `nav` pointer.
   - Widget validity per slot, including `query` accepted in `toolbar`, `left` and `right` and
     rejected elsewhere; duplicate `query` rejected.
   - Browse defaults: title above the query, query above the list, and the rendered band order.
   - Opt-in on a custom page; `toolbar: false` restores the plain search box.
   - Every page renders the docs link in the footer by default; `footer: false` removes it and a
     custom `footer` replaces it.
   - Update the existing tests that use `"top"` (`SiteConfigTests`, `SiteGeneratorPagesTests`,
     `SiteGeneratorGraphTests`, `OkfLinksTests`, `SiteWidgetTests`) and those that assume `query` in
     Browse's `main`.

## Verification
1. `dotnet test --solution eru.slnx`.
2. Generate sites and screenshot Browse (title, then query, then table), a document page (unchanged),
   the query in `left`, `toolbar: false`, and a config using the old `top` (expect the error), in light
   and dark mode and at phone width.

## Commit
One `feat:` commit with a breaking-change note for the `top` to `nav` rename and the move of `query`
out of `main`.
