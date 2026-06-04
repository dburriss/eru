# Plan: `eru site generate` — Static HTML site for browsing the local cache

## Context

`eru` accumulates a rich index of knowledge files across all configured sources in `~/.cache/eru/sources/<name>/index.json`. There is currently no way to browse this catalogue outside the TUI or CLI. This plan adds `eru site generate` — a command that emits a fully self-contained static HTML site for browsing and searching the full source index, with facet filtering by source, file type, and tags, and full-text search powered by FlexSearch.

The site is built from the **source indices** (not just the lock file) so it reflects the complete advertised catalogue. Files that have been pulled or cached locally get rendered content pages; index-only entries show metadata only.

**Progressive enhancement** is the guiding principle: every page is fully navigable as plain HTML with no JavaScript. JS is loaded as an optional layer that adds in-place search (FlexSearch) and client-side facet filtering on top of the static structure. Dark/light theming is handled by CSS `prefers-color-scheme` without JS; the theme toggle button is a JS enhancement only.

---

## Command

```
eru site generate [--output <dir>] [--open]
```

| Flag | Default | Description |
|---|---|---|
| `--output` / `-o` | `./cache-site/` | Directory to write the generated site into |
| `--open` | off | Launch the default browser at `index.html` after generation |

---

## New project: `src/Eru.Site/`

Mirrors the pattern established by `Eru.Tui` — a separate project to isolate the Markdig dependency and HTML generation concern.

### `Eru.Site.fsproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Eru.Site</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Eru.Domain/Eru.Domain.fsproj" />
    <ProjectReference Include="../Eru.Adapters/Eru.Adapters.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Markdig" Version="0.*" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="SiteModel.fs" />
    <Compile Include="MarkdownRenderer.fs" />
    <Compile Include="IndexBuilder.fs" />
    <Compile Include="HtmlTemplates.fs" />
    <Compile Include="SiteGenerator.fs" />
  </ItemGroup>
</Project>
```

Add to `eru.slnx` inside `/src/`:

```xml
<Project Path="src/Eru.Site/Eru.Site.fsproj" />
```

---

## Data pipeline

1. Read all `~/.cache/eru/sources/<name>/index.json` files → `Map<remotePath, IndexEntry>` per source
2. Read `EffectiveConfig` → `SourceConfig list`
3. For each source, check whether `~/.cache/eru/sources/<name>/manifest.json` exists → `hasManifest: bool`
4. For each `(sourceName, remotePath, IndexEntry)` triple:
   - Derive `extension` from `remotePath`
   - Determine `status`: `Pulled` (LocalPath set) | `Cached` (CacheRelPath set, no LocalPath) | `IndexOnly`
   - For `Cached` / `Pulled`: read blob from `~/.cache/eru/sources/<name>/files/<sha256hex>`, take first 500 chars as `body` for FlexSearch
5. Emit `data/documents.json` — full document list for FlexSearch and client-side facet filtering
6. Emit `data/sources.json` — source list with manifest flag and file counts
7. For each `Cached` / `Pulled` entry where extension is `.md`: render full content via Markdig → `files/<source>/<escaped-path>.html`

---

## Site model (`SiteModel.fs`)

```fsharp
type FileStatus = Pulled | Cached | IndexOnly

type SiteDocument = {
    Id          : string            // "<sourceName>:<remotePath>"
    Source      : string
    RemotePath  : string
    Title       : string            // filename part of remotePath
    Extension   : string            // ".md", ".yaml", etc.
    Tags        : string list
    Description : string option
    Status      : FileStatus
    Body        : string option     // first ~500 chars of content (Cached/Pulled only)
    PageUrl     : string option     // relative URL to rendered file page (Cached/Pulled .md only)
}

type SiteSource = {
    Name        : string
    HasManifest : bool              // true if ~/.cache/eru/sources/<name>/manifest.json exists
    FileCount   : int
    Files       : SiteDocument list
}

type SiteTag = {
    Name      : string
    FileCount : int
    Files     : SiteDocument list
}

type SiteModel = {
    Documents     : SiteDocument list
    Sources       : SiteSource list
    Tags          : SiteTag list    // distinct tags, sorted by name
    AllExtensions : string list     // distinct extensions, sorted
}
```

---

## FlexSearch index (`IndexBuilder.fs`)

Emits two files consumed by `js/search.js` and `js/app.js`:

- `data/documents.json` — full `SiteDocument list` serialised (camelCase, `System.Text.Json`)
- `data/sources.json` — full `SiteSource list` serialised (name, hasManifest, fileCount)

The `search.js` builds the FlexSearch `Document` index client-side from `documents.json`, searching:
- `title` field — 2× boost
- `body` field — standard weight
- `tags` field — flattened to space-separated string

Facet filtering (source / extension / tag) is applied in JS against the pre-loaded `documents.json` array, independent of the FlexSearch query.

---

## Pages generated

| Output path | JS required? | Description |
|---|---|---|
| `index.html` | No (enhanced with JS) | Full file list as static HTML; JS adds in-place search + filter |
| `sources/index.html` | No | All sources with manifest indicator, file count, links to per-source pages |
| `sources/<name>/index.html` | No | All files for one source, as static HTML |
| `tags/index.html` | No | All tags with file counts, linked to per-tag pages |
| `tags/<tag>/index.html` | No | All files carrying a given tag |
| `files/<source>/<slug>.html` | No | Individual file page with rendered HTML content (Cached/Pulled `.md` files) |

### `index.html` layout

Without JS: a plain file listing with sidebar links to static filter pages. With JS: sidebar links become checkboxes for in-place filtering and a search box appears.

```
┌──────────────────────────────────────────────────────────────┐
│  eru knowledge browser              [search box — JS only]   │
├──────────────┬───────────────────────────────────────────────┤
│ Sources      │  All files (N)                                │
│  source-a →  │                                               │
│  source-b →  │  ┌─────────────────────────────────────────┐ │
│              │  │ filename.md          [source-a] [pulled] │ │
│ Types        │  │ Short description or first body line…   │ │
│  .md →       │  │ #tag1  #tag2                             │ │
│  .yaml →     │  └─────────────────────────────────────────┘ │
│              │                                               │
│ Tags         │  ┌─────────────────────────────────────────┐ │
│  dotnet →    │  │ other-file.yaml     [source-b] [cached]  │ │
│  patterns →  │  │ …                                        │ │
└──────────────┴───────────────────────────────────────────────┘
```

Sidebar entries are `<a href="sources/<name>/index.html">` etc. JS replaces them with `<input type="checkbox">` and intercepts changes to filter the card list in place. File cards each carry `data-source`, `data-ext`, and `data-tags` attributes used by the JS filter.

`<noscript>` note at top of page: "Search requires JavaScript. Browse by source or tag using the sidebar links."

### `sources/index.html` layout

```
eru sources

  source-a   [manifest]   42 files   →
  source-b   [no manifest]   7 files   →
```

Each source name links to `sources/<name>/index.html`. `[manifest]` / `[no manifest]` indicates whether a cached `manifest.json` exists. Sources with a manifest explicitly advertise their files; sources without one were indexed by other means (glob patterns, direct add).

### `sources/<name>/index.html` layout

```
source-a  [manifest]  ← back to sources

  filename.md       #dotnet #patterns   [pulled]   →
  another-file.yaml #config             [cached]   →
  third.md          #patterns           [index-only]
```

File entries link to their rendered `files/<source>/<slug>.html` page if one exists (Cached/Pulled `.md`), otherwise no link.

### `tags/index.html` layout

```
eru tags

  #dotnet      (14 files)   →
  #patterns    (9 files)    →
  #config      (3 files)    →
  …
```

### `tags/<tag>/index.html` layout

```
#dotnet  ← back to tags

  filename.md        [source-a]   [pulled]   →
  another-guide.md   [source-b]   [cached]   →
  …
```


---

## Markdown rendering (`MarkdownRenderer.fs`)

```fsharp
module Eru.Site.MarkdownRenderer

open Markdig

let pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

let render (markdown: string) : string =
    Markdown.ToHtml(markdown, pipeline)
```

Used by `SiteGenerator` when writing `files/<source>/<slug>.html` pages.

---

## HTML templates (`HtmlTemplates.fs`)

All HTML is generated as F# strings (no templating library). One shared `layout` function wraps page content in a common shell. JS is loaded at the end of `<body>` as a module so it never blocks rendering; the page is fully usable before it executes.

```fsharp
let layout (title: string) (body: string) : string = $"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>{title} — eru</title>
  <link rel="stylesheet" href="/assets/css/style.css" />
</head>
<body>
  <nav>
    <a href="/index.html">Browse</a>
    <a href="/sources/index.html">Sources</a>
    <a href="/tags/index.html">Tags</a>
    <button id="theme-toggle" aria-label="Toggle theme"></button>
  </nav>
  <noscript><p class="note">Search requires JavaScript. Browse by source or tag using the navigation links.</p></noscript>
  <main>{body}</main>
  <script type="module" src="/js/theme.js"></script>
  <script type="module" src="/js/app.js"></script>
</body>
</html>"""
```

File cards on `index.html` carry data attributes for JS filtering:
```html
<article class="file-card"
         data-source="source-a"
         data-ext=".md"
         data-tags="dotnet patterns">
  …
</article>
```

Individual page functions: `indexPage`, `sourcesPage`, `sourceFilesPage`, `tagsPage`, `tagFilesPage`, `filePage`.

---

## Assets (self-contained)

All assets are embedded as strings in `HtmlTemplates.fs` or `SiteGenerator.fs` and written to disk at generate time. No network access required to use the generated site.

| Output path | Reuse from devonburriss.me | Notes |
|---|---|---|
| `assets/css/style.css` | `/assets/css/style.css` — copy verbatim | Normalize v4.1.1 + GitHub utility framework (MIT) |
| `js/vendor/flexsearch.bundle.module.min.js` | `/js/vendor/flexsearch.bundle.module.min.js` — copy verbatim | v0.8.212 |
| `js/search-index-config.js` | `/js/search-index-config.js` — copy verbatim | `tokenize:"forward"`, `encoder:LatinBalance`, title res 9, body res 5 |
| `js/search.js` | `/js/search.js` — copy, change data paths to `/data/` | Lazy-load, title 2× boost, 20 results |
| `js/theme.js` | `/js/theme.js` — copy verbatim | localStorage + `prefers-color-scheme` toggle |
| `js/app.js` | Written from scratch | Replaces sidebar links with checkboxes; filters `.file-card` elements by `data-source`/`data-ext`/`data-tags`; wires up search box |

**Theme without JS:** `assets/css/style.css` includes a `@media (prefers-color-scheme: dark)` block so dark mode works without the toggle button. `theme.js` is additive — it only enables the manual override.

**Data files** (generated per-run, not embedded):

| Output path | Content |
|---|---|
| `data/docs.json` | `SiteDocument list` — matches the `{id,url,title,body,excerpt,keywords,topics}` schema from devonburriss.me |
| `data/sources.json` | `SiteSource list` |
| `data/manifest.json` | `{schemaVersion,documentCount,flexsearchVersion,exportKeys,…}` — matches devonburriss.me manifest shape |
| `data/index.json` | FlexSearch export (written by running the index build in-process via the JS engine, or pre-built by the F# generator using the same export key format) |

FlexSearch version: **0.8.212**.

---

## Theming and feature flags

### Theming

The vendored `assets/css/style.css` defines CSS custom properties at `:root` so any value can be overridden without touching the base stylesheet:

```css
:root {
  --color-primary: #0366d6;
  --color-text: #24292e;
  --color-bg: #fff;
  --color-surface: #f6f8fa;
  --color-border: #e1e4e8;
  --font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
  --font-mono: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
}
@media (prefers-color-scheme: dark) {
  :root { --color-text: #c9d1d9; --color-bg: #0d1117; /* … */ }
}
```

`ThemeOverrides` values are written into a small `assets/css/theme-overrides.css` appended after `style.css`. If `CustomCssPath` is set, that file's contents are appended last, giving full escape-hatch control.

### Feature flags

`SiteFeatures` gates entire sections of output. When a feature is off, the generator skips those files and the nav link for that section is omitted from the layout. This means:

- `TagPages = false` → no `tags/` directory, no Tags nav link
- `SourcePages = false` → `sources/index.html` still generated (source overview is core); per-source `sources/<name>/index.html` pages are skipped
- `FilePages = false` → `.md` blobs are not rendered; file titles are plain text, not links
- `Search = false` → no `data/` directory, no FlexSearch JS, no search box (even with JS)
- `ThemeToggle = false` → `js/theme.js` not written, button omitted from layout; dark mode still works via `prefers-color-scheme` CSS

These flags are not yet exposed as CLI arguments in the initial implementation — defaults cover the common case. They are designed as an opt-in configuration layer (e.g. a future `--config site.json` flag) rather than a wall of `--no-tags` CLI flags.

---

## `SiteGenerator.fs` — entry point

```fsharp
module Eru.Site.SiteGenerator

type SiteFeatures = {
    TagPages        : bool   // generate tags/index.html + tags/<tag>/index.html
    SourcePages     : bool   // generate sources/<name>/index.html
    FilePages       : bool   // render .md blobs to files/<source>/<slug>.html
    Search          : bool   // emit FlexSearch data files + js/search.js
    ThemeToggle     : bool   // include js/theme.js and the toggle button
}

type ThemeOverrides = {
    PrimaryColor    : string option   // overrides --color-primary CSS var
    FontFamily      : string option   // overrides --font-family CSS var
    CustomCssPath   : string option   // path to a user CSS file appended after style.css
}

type GenerateOptions = {
    OutputDir   : string
    OpenBrowser : bool
    Features    : SiteFeatures
    Theme       : ThemeOverrides
}

module GenerateOptions =
    let defaults = {
        OutputDir   = "./cache-site/"
        OpenBrowser = false
        Features    = { TagPages = true; SourcePages = true; FilePages = true; Search = true; ThemeToggle = true }
        Theme       = { PrimaryColor = None; FontFamily = None; CustomCssPath = None }
    }

val generate : deps: Deps -> cfg: EffectiveConfig -> opts: GenerateOptions -> Result<unit, string>
```

Steps:
1. Build `SiteModel` from all source indices + config (checking manifest presence per source)
2. Write vendor assets (`assets/css/style.css`, `js/vendor/…`, `js/search.js`, `js/search-index-config.js`, `js/theme.js`, `js/app.js`)
3. Write `data/docs.json`, `data/sources.json`, `data/manifest.json`, `data/index.json`
4. Write `index.html` — full file card list as static HTML + data attributes for JS
5. Write `sources/index.html` — source list with manifest badges
6. For each source: write `sources/<name>/index.html`
7. Write `tags/index.html` — tag index with counts
8. For each tag: write `tags/<slug>/index.html`
9. For each Cached/Pulled `.md` document: render via Markdig, write `files/<source>/<slug>.html`
10. If `opts.OpenBrowser`: call `Process.Start` with the `index.html` absolute path

---

## Changes to `Eru.Cli`

### `Eru.Cli.fsproj`

```xml
<ProjectReference Include="../Eru.Site/Eru.Site.fsproj" />
```

Add compile entries:
```xml
<Compile Include="SiteGenerateCli.fs" />
```

### `Args.fs`

```fsharp
type SiteGenerateArgs =
    | [<AltCommandLine("-o")>] Output of string
    | Open
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _ -> "Output directory (default: ./cache-site/)"
            | Open     -> "Open the generated site in the default browser"

type SiteArgs =
    | [<CliPrefix(CliPrefix.None)>] Generate of ParseResults<SiteGenerateArgs>
    interface IArgParserTemplate with
        member a.Usage = match a with Generate _ -> "Generate a static HTML site from the local cache index"

// In EruArgs:
| [<SubCommand>] Site of ParseResults<SiteArgs>
| Site _ -> "Generate a static HTML site for browsing the knowledge cache."
```

### `SiteGenerateCli.fs`

```fsharp
module Eru.Cli.SiteGenerateCli

let (|SiteGenerateCmd|_|) (r: ParseResults<EruArgs>) = ...

let run (deps: Deps) (args: ParseResults<SiteGenerateArgs>) =
    let outputDir  = args.TryGetResult Output |> Option.defaultValue "./cache-site/"
    let openBrowser = args.Contains Open
    match SiteGenerator.generate deps eff { OutputDir = outputDir; OpenBrowser = openBrowser } with
    | Ok ()   -> printfn "Site generated at %s" outputDir; 0
    | Error e -> eprintfn "Error: %s" e; 1
```

### `Program.fs`

```fsharp
| SiteGenerateCmd args -> SiteGenerateCli.run deps args
```

---

## Files to create / modify

| File | Change |
|---|---|
| `src/Eru.Site/Eru.Site.fsproj` | NEW |
| `src/Eru.Site/SiteModel.fs` | NEW — `SiteDocument`, `SiteSource`, `SiteTag`, `SiteModel` types |
| `src/Eru.Site/MarkdownRenderer.fs` | NEW — Markdig wrapper |
| `src/Eru.Site/IndexBuilder.fs` | NEW — builds `SiteModel` from source indices + config; checks manifest presence; groups by source and tag |
| `src/Eru.Site/HtmlTemplates.fs` | NEW — `layout`, `indexPage`, `sourcesPage`, `sourceFilesPage`, `tagsPage`, `tagFilesPage`, `filePage` |
| `src/Eru.Site/SiteGenerator.fs` | NEW — `generate` orchestrator; writes all output files |
| `eru.slnx` | Add `Eru.Site` project |
| `src/Eru.Cli/Eru.Cli.fsproj` | Add project reference to `Eru.Site`; add `SiteGenerateCli.fs` compile entry |
| `src/Eru.Cli/Args.fs` | Add `SiteGenerateArgs`, `SiteArgs`, `Site` case in `EruArgs` |
| `src/Eru.Cli/SiteGenerateCli.fs` | NEW — `(|SiteGenerateCmd|_|)` and `run` |
| `src/Eru.Cli/Program.fs` | Add `SiteGenerateCmd` dispatch arm |

---

## Implementation sequence

1. **Project scaffold** — `Eru.Site.fsproj`, add to solution, add reference from `Eru.Cli`. Confirm `dotnet build`.
2. **Args + stub** — `SiteArgs`/`SiteGenerateArgs` in `Args.fs`, stub `SiteGenerateCli.run` returning 0. Confirm command parses.
3. **Domain types** — `SiteModel.fs` with `SiteDocument`, `SiteSource`, `SiteTag`, `SiteModel`.
4. **Index builder** — `IndexBuilder.fs`: reads source indices, checks manifest presence, groups by source and tag, builds `SiteModel`. Unit test with a fixture index.
5. **Markdown renderer** — `MarkdownRenderer.fs` (trivial Markdig wrapper).
6. **Static pages** — `HtmlTemplates.fs`: layout shell + all page functions. Verify all pages render valid HTML with JS disabled in browser (no broken experience).
7. **Assets** — vendor `style.css`, FlexSearch, `search.js`, `search-index-config.js`, `theme.js` as embedded strings; write `app.js` from scratch for checkbox filtering + search wiring.
8. **Generator** — `SiteGenerator.fs`: orchestrate all steps including per-source and per-tag pages.
9. **CLI wiring** — complete `SiteGenerateCli.run`.
10. **`--open` flag** — `Process.Start` the output `index.html`.

---

## Verification

```bash
dotnet build
dotnet test

# Generate site into default dir
dotnet run --project src/Eru -- site generate

# Generate into custom dir and open browser
dotnet run --project src/Eru -- site generate --output /tmp/eru-site --open

# Manual checks (JS enabled):
# - index.html loads with full file list; FlexSearch returns relevant results for a known tag/filename
# - Source / type / tag filter checkboxes narrow the card list in place
# - sources/index.html lists all sources with manifest indicator and file count
# - sources/<name>/index.html shows only files from that source
# - tags/index.html lists all tags with counts
# - tags/<tag>/index.html shows only files with that tag
# - A .md file link opens a rendered HTML page
# - Theme toggle switches dark/light and persists across page loads
# - All assets load locally (no CDN requests, no 404s in browser devtools)
#
# Manual checks (JS disabled in browser devtools):
# - index.html shows the full file list (no blank page)
# - <noscript> note is visible
# - Sidebar shows plain links to sources/<name>/ and tags/<tag>/ pages, not checkboxes
# - sources/index.html, sources/<name>/index.html, tags/index.html, tags/<tag>/index.html all render fully
# - Navigation between all pages works via plain <a href> links
# - .md file pages render with no JS
#
# Offline check:
# - Copy output dir to a temp location with no network; open index.html — everything works
```
