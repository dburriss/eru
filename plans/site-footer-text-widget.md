---
status: done
---

# Plan: Footer text and html widgets

## Context
The footer only takes widgets and `{label,url}` links; the docs/changelog links are hard-coded `LinkRef` constants in `PageLayout.fs`. Add a Markdown `text` footer item (inline or file-based, HTML escaped) and make the default footer just a text item, so the docs link is no longer special.
Also add a raw `html` footer item (inline or file), emitted unescaped since config is trusted. Decided with user.
Implementation: generalize the new case to `TextRef of TextKind * TextSource` where `TextKind = Markdown | Html`. Parse `{ "html": "<b>..</b>" }` / `{ "html": { "file": "footer.html" } }` alongside `text`. `html` is footer-only, file paths validated with `normalizeRelPath`, read/watched exactly like `text` files; rendered as `<div class="widget widget-html">`. Extra tests: html passes through unescaped, html file, bad shape.

## Changes
1. **Domain** `src/Eru.Domain/SiteConfig.fs`
   - New `LayoutItem` case `TextRef of TextSource` where `TextSource = TextInline of string | TextFile of string` (repo-relative path).
   - `parseItem`: accept `{ "text": "md" }` and `{ "text": { "file": "path" } }` (reuse `expectKeys`; error if neither/other shape).
   - `validateLayout`: `TextRef` valid only in footer (error `text is not valid in <slot>` otherwise); `TextFile` path checked with `normalizeRelPath` (same message as `content.file`).
2. **Renderer** `src/Eru.Site/MarkdownRenderer.fs`
   - Add `renderEscaped` (pipeline with `DisableHtml()`), reusing `renderWith` link rewriting where files are relative.
3. **Generator** `src/Eru.Site/SiteGenerator.fs`
   - Resolve footer `TextFile`s up front like `loadContent` (1716-1739): read via `deps.ReadLocalFile`, same missing/unreadable errors (`site.layout.footer … does not exist`), fail before writing anything. Pre-render text to HTML into `Env`.
   - Include footer text files in `referencedContentFiles` (1569) so `eru site serve` watches them.
4. **Layout/render** `src/Eru.Site/Widgets.fs` (`renderSide`, 332-358) and `PageLayout.fs`
   - `renderSide` emits `<div class="widget widget-text">…html…</div>` for `TextRef`.
   - `baseLayout.Footer` becomes `Items [ TextRef (TextInline "[docs](…) · [changelog](…)"); WidgetRef "version" ]`; delete `docsLink`/`changelogLink`.
   - Adjust footer CSS (`SiteGenerator.fs:502-519`) for `.widget-text` (centered, link separators no longer via `li + li::before`, links open in a new tab not required).
5. **Docs**: `docs/reference/site-generation.md` (slots table line 156, new `text` item), `docs/how-to/customize-the-generated-site.md` ("Change the footer"), `skills/eru/references/site-customization.md` (line 60, fix stale default), `CHANGELOG.md`.

## Tests
- `SiteConfigTests.fs`: parse inline and file `text`; reject bad shapes, `../` path, text outside footer.
- `SiteGeneratorPagesTests.fs`: default footer still shows docs/changelog links and `eru-version` (362-369); custom text renders Markdown; raw `<script>` is escaped; file text renders; missing file errors; `referencedContentFiles` includes it.

## Verification
`dotnet build` and `dotnet test`; run `eru site` on a sample config with a custom footer and inspect the generated HTML for the footer region.

## Implementation note
File-based items are read once up front by the generator into `SiteEnv.TextFiles : Map<string, string>` (key: normalized repo-relative path), so `Widgets.renderSide` stays pure. A `referencedTextFiles` helper walks every layout (site, document, rules, pages) for footer file items; `referencedContentFiles` includes its result so `eru site serve` watches them.
