module Eru.Site.SiteGenerator

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Eru

// ── options ──────────────────────────────────────────────────────────────────

type SiteFeatures = {
    TagPages    : bool
    SourcePages : bool
    FilePages   : bool
    Search      : bool
    ThemeToggle : bool
}

type ThemeOverrides = {
    PrimaryColor  : string option
    FontFamily    : string option
    CustomCssPath : string option
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

// ── embedded assets ───────────────────────────────────────────────────────────

let private css = """
:root {
  --color-primary: #0366d6;
  --color-text: #24292e;
  --color-bg: #ffffff;
  --color-surface: #f6f8fa;
  --color-border: #e1e4e8;
  --color-badge-bg: #eef2ff;
  --color-badge-text: #3730a3;
  --font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
  --font-mono: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
  --radius: 6px;
}
@media (prefers-color-scheme: dark) {
  :root {
    --color-text: #c9d1d9;
    --color-bg: #0d1117;
    --color-surface: #161b22;
    --color-border: #30363d;
    --color-primary: #58a6ff;
    --color-badge-bg: #1e2a3a;
    --color-badge-text: #79b8ff;
  }
}
body.theme-light {
  --color-text: #24292e; --color-bg: #ffffff; --color-surface: #f6f8fa;
  --color-border: #e1e4e8; --color-primary: #0366d6;
  --color-badge-bg: #eef2ff; --color-badge-text: #3730a3;
}
body.theme-dark {
  --color-text: #c9d1d9; --color-bg: #0d1117; --color-surface: #161b22;
  --color-border: #30363d; --color-primary: #58a6ff;
  --color-badge-bg: #1e2a3a; --color-badge-text: #79b8ff;
}

*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

body {
  font-family: var(--font-family);
  color: var(--color-text);
  background: var(--color-bg);
  line-height: 1.5;
  font-size: 14px;
}

a { color: var(--color-primary); text-decoration: none; transition: color 0.15s ease; }
a:hover { text-decoration: underline; }

h1 { font-size: 1.4rem; font-weight: 600; margin-bottom: 1rem; }
h3 { font-size: 0.85rem; font-weight: 600; text-transform: uppercase;
     letter-spacing: 0.05em; color: var(--color-text); opacity: 0.7; margin-bottom: 0.5rem; }

/* nav */
.site-nav {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 1.5rem;
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  flex-wrap: wrap;
}
.nav-brand { font-weight: 700; font-size: 1.1rem; }
#theme-toggle {
  margin-left: auto;
  background: transparent;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 0.25rem 0.5rem;
  cursor: pointer;
  color: var(--color-text);
  font-size: 1rem;
  transition: border-color 0.15s ease, color 0.15s ease;
}

/* noscript note */
.noscript-note {
  background: var(--color-surface);
  border-left: 3px solid var(--color-primary);
  padding: 0.5rem 1rem;
  margin: 0.5rem 1.5rem;
  font-size: 0.85rem;
}

/* main layout */
main { padding: 1.5rem; }

.page-layout {
  display: grid;
  grid-template-columns: 200px 1fr;
  gap: 1.5rem;
  align-items: start;
}
@media (max-width: 640px) {
  .page-layout { grid-template-columns: 1fr; }
}

/* sidebar */
.sidebar { position: sticky; top: 1rem; }
.sidebar-section { margin-bottom: 1.5rem; }
.sidebar-section ul { list-style: none; }
.sidebar-section li { padding: 0.2rem 0; display: flex; align-items: center; gap: 0.4rem; }
.sidebar-section li label { cursor: pointer; }
.sidebar-section li input[type="checkbox"] { cursor: pointer; }
.sidebar-section h3 { color: var(--color-text); opacity: 0.7; }
body.theme-dark .sidebar-section h3 { opacity: 0.6; }
.count { font-size: 0.8rem; opacity: 0.6; }

/* content header */
.content-header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
.content-header h1 { margin-bottom: 0; }
#search-input {
  padding: 0.4rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  background: var(--color-bg);
  color: var(--color-text);
  font-family: var(--font-family);
  font-size: 0.9rem;
  min-width: 220px;
}

/* file cards */
.file-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 0.75rem 1rem;
  margin-bottom: 0.75rem;
  transition: box-shadow 0.15s ease;
}
.card-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 0.3rem;
}
.card-title { font-weight: 600; }
.card-body { font-size: 0.85rem; opacity: 0.8; margin-bottom: 0.4rem; white-space: pre-wrap; word-break: break-word; }
.card-tags { display: flex; gap: 0.35rem; flex-wrap: wrap; }

/* badges */
.badge {
  display: inline-block;
  font-size: 0.72rem;
  padding: 0.15rem 0.45rem;
  border-radius: 999px;
  font-weight: 500;
  white-space: nowrap;
  transition: background-color 0.15s ease, color 0.15s ease;
}
.badge-source { background: var(--color-badge-bg); color: var(--color-badge-text); }
.badge-status    { background: #ddf4ff; color: #0969da; }
.badge-pulled    { background: #d1fae5; color: #065f46; }
.badge-cached    { background: #fef3c7; color: #92400e; }
.badge-index-only { background: var(--color-surface); color: var(--color-text); border: 1px solid var(--color-border); }
body.theme-dark .badge-status { background: #0c2d6b; color: #79b8ff; }
body.theme-dark .badge-pulled { background: #064e3b; color: #6ee7b7; }
body.theme-dark .badge-cached { background: #451a03; color: #fcd34d; }
.tag {
  display: inline-block;
  font-size: 0.75rem;
  padding: 0.1rem 0.4rem;
  border-radius: var(--radius);
  background: var(--color-badge-bg);
  color: var(--color-badge-text);
}

/* source grid (sources/index.html) */
.source-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(480px, 1fr));
  gap: 1rem;
  margin-top: 1rem;
}
.source-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 1.4rem 1.6rem;
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}
.source-card-header {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex-wrap: wrap;
}
.source-card-name { font-weight: 600; font-size: 1.1rem; }
.source-card-url { font-size: 0.82rem; word-break: break-all; opacity: 0.75; }
.source-card-meta { font-size: 0.88rem; }
.source-card-tip { margin-top: auto; padding-top: 0.75rem; min-width: 0; }
.source-card-tip .cli-tip { width: 100%; min-width: 0; }
.source-card-tip .cli-tip code { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; flex: 1; }

/* tag list page */
.tag-list { list-style: none; }
.tag-list li {
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--color-border);
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.badge-manifest { background: #d1fae5; color: #065f46; }
.badge-no-manifest { background: var(--color-surface); color: var(--color-text); border: 1px solid var(--color-border); }
body.theme-dark .badge-manifest { background: #064e3b; color: #6ee7b7; }

/* page header */
.page-header { display: flex; flex-direction: column; align-items: flex-start; gap: 0.25rem; margin-bottom: 1.5rem; }
.page-header h1 { margin-bottom: 0; }

/* markdown body */
.markdown-body {
  max-width: 860px;
  font-size: 15px;
  line-height: 1.7;
}
.markdown-body h1, .markdown-body h2, .markdown-body h3,
.markdown-body h4, .markdown-body h5, .markdown-body h6 {
  margin-top: 1.5rem; margin-bottom: 0.5rem; font-weight: 600;
}
.markdown-body p { margin-bottom: 1rem; }
.markdown-body pre {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 1rem;
  overflow-x: auto;
  margin-bottom: 1rem;
  font-family: var(--font-mono);
  font-size: 0.85rem;
}
.markdown-body code {
  font-family: var(--font-mono);
  font-size: 0.85em;
  background: var(--color-surface);
  padding: 0.1em 0.3em;
  border-radius: 3px;
}
.markdown-body pre code { background: none; padding: 0; }
.markdown-body ul, .markdown-body ol { margin-bottom: 1rem; padding-left: 1.5rem; }
.markdown-body li { margin-bottom: 0.25rem; }
.markdown-body blockquote {
  border-left: 3px solid var(--color-border);
  padding-left: 1rem;
  opacity: 0.8;
  margin-bottom: 1rem;
}
.markdown-body table { border-collapse: collapse; margin-bottom: 1rem; width: 100%; }
.markdown-body th, .markdown-body td {
  border: 1px solid var(--color-border);
  padding: 0.4rem 0.75rem;
  text-align: left;
}
.markdown-body th { background: var(--color-surface); font-weight: 600; }
.markdown-body a { color: var(--color-primary); }
.markdown-body img { max-width: 100%; }

/* breadcrumbs */
.breadcrumbs {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.82rem;
  opacity: 0.65;
  margin-bottom: 0.4rem;
}
.bc-sep { opacity: 0.5; }
.bc-current { opacity: 0.75; }

/* document metadata box */
.doc-meta {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  max-width: 860px;
  padding: 0.9rem 1.1rem;
  margin-bottom: 1.25rem;
  background: var(--color-surface);
  border-radius: var(--radius);
  border: 1px solid var(--color-border);
}
.doc-description {
  font-size: 0.88rem;
  line-height: 1.6;
  opacity: 0.85;
  margin: 0;
}
.doc-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
}

/* cli tips */
.cli-tips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-bottom: 1rem;
}
.cli-tip {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 0.2rem 0.6rem;
  font-size: 0.8rem;
}
.cli-tip-label {
  font-size: 0.68rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  opacity: 0.5;
  white-space: nowrap;
}
.cli-tip code {
  font-family: var(--font-mono);
  font-size: 0.8rem;
}
:root {
  --copy-icon: url("data:image/svg+xml,%3Csvg width='24' height='24' viewBox='0 0 24 24' fill='none' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M13 7H7V5H13V7Z' fill='currentColor'/%3E%3Cpath d='M13 11H7V9H13V11Z' fill='currentColor'/%3E%3Cpath d='M7 15H13V13H7V15Z' fill='currentColor'/%3E%3Cpath fill-rule='evenodd' clip-rule='evenodd' d='M3 19V1H17V5H21V23H7V19H3ZM15 17V3H5V17H15ZM17 7V19H9V21H19V7H17Z' fill='currentColor'/%3E%3C/svg%3E");
}
.copy-btn {
  display: inline-block;
  flex-shrink: 0;
  width: 0.85rem;
  height: 0.85rem;
  background-color: var(--color-primary);
  -webkit-mask-image: var(--copy-icon);
  mask-image: var(--copy-icon);
  -webkit-mask-size: contain;
  mask-size: contain;
  -webkit-mask-repeat: no-repeat;
  mask-repeat: no-repeat;
  border: none;
  padding: 0;
  cursor: pointer;
  opacity: 0.5;
  transition: opacity 0.15s ease, background-color 0.15s ease;
}
.copy-btn:hover { opacity: 1; }
.copy-btn.copied { background-color: #22c55e; opacity: 1; }
"""

let private themeJs = """
(function () {
  // Always stamp one class so body.theme-dark / body.theme-light CSS rules
  // fully control all component overrides — @media only handles the no-JS fallback.
  var stored = localStorage.getItem('eru-theme');
  var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  var dark = stored === 'dark' || (stored === null && prefersDark);
  document.body.classList.add(dark ? 'theme-dark' : 'theme-light');

  document.addEventListener('DOMContentLoaded', function () {
    var btn = document.getElementById('theme-toggle');
    if (!btn) return;
    btn.style.display = '';
    function isDark() {
      return document.body.classList.contains('theme-dark');
    }
    function updateLabel() {
      btn.textContent = isDark() ? '☀' : '☽';
      btn.setAttribute('aria-label', isDark() ? 'Switch to light mode' : 'Switch to dark mode');
    }
    updateLabel();
    btn.addEventListener('click', function () {
      var next = isDark() ? 'light' : 'dark';
      document.body.classList.remove('theme-light', 'theme-dark');
      document.body.classList.add('theme-' + next);
      localStorage.setItem('eru-theme', next);
      updateLabel();
    });
  });
})();
"""

let private appJs = """
(function () {
  var filterState = { sources: {}, exts: {}, tags: {}, query: '' };

  function applyFilters() {
    var srcActive = Object.keys(filterState.sources).filter(function (k) { return filterState.sources[k]; });
    var extActive = Object.keys(filterState.exts).filter(function (k) { return filterState.exts[k]; });
    var tagActive = Object.keys(filterState.tags).filter(function (k) { return filterState.tags[k]; });
    var q = filterState.query.toLowerCase();
    var cards = document.querySelectorAll('.file-card');
    var visible = 0;
    cards.forEach(function (card) {
      var src = card.dataset.source || '';
      var ext = card.dataset.ext || '';
      var cardTags = (card.dataset.tags || '').split(' ').filter(Boolean);
      var srcOk = srcActive.length === 0 || srcActive.indexOf(src) >= 0;
      var extOk = extActive.length === 0 || extActive.indexOf(ext) >= 0;
      var tagOk = tagActive.length === 0 || tagActive.some(function (t) { return cardTags.indexOf(t) >= 0; });
      var textOk = !q || card.textContent.toLowerCase().indexOf(q) >= 0;
      var show = srcOk && extOk && tagOk && textOk;
      card.style.display = show ? '' : 'none';
      if (show) visible++;
    });
    var counter = document.getElementById('file-count');
    if (counter) counter.textContent = '(' + visible + ')';
  }

  function replaceLinksWithCheckboxes(listId, filterKey) {
    var ul = document.getElementById(listId);
    if (!ul) return;
    ul.querySelectorAll('li').forEach(function (li) {
      var a = li.querySelector('a');
      if (!a) return;
      var countEl = li.querySelector('.count');
      var rawText = a.textContent.trim();
      var value = rawText;
      var id = 'chk-' + filterKey + '-' + value.replace(/[^a-zA-Z0-9]/g, '_');
      var cb = document.createElement('input');
      cb.type = 'checkbox';
      cb.id = id;
      cb.value = value;
      var label = document.createElement('label');
      label.setAttribute('for', id);
      label.textContent = value;
      while (li.firstChild) li.removeChild(li.firstChild);
      li.appendChild(cb);
      li.appendChild(label);
      if (countEl) li.appendChild(countEl);
      cb.addEventListener('change', function () {
        filterState[filterKey][value] = cb.checked;
        applyFilters();
      });
    });
  }

  document.addEventListener('click', function (e) {
    var btn = e.target.closest('.copy-btn');
    if (!btn || !navigator.clipboard) return;
    navigator.clipboard.writeText(btn.getAttribute('data-copy')).then(function () {
      btn.classList.add('copied');
      setTimeout(function () { btn.classList.remove('copied'); }, 1500);
    });
  });

  document.addEventListener('DOMContentLoaded', function () {
    replaceLinksWithCheckboxes('source-filters', 'sources');
    replaceLinksWithCheckboxes('ext-filters', 'exts');
    replaceLinksWithCheckboxes('tag-filters', 'tags');

    var searchContainer = document.getElementById('search-container');
    if (searchContainer) searchContainer.style.display = '';

    var searchInput = document.getElementById('search-input');
    if (searchInput) {
      searchInput.addEventListener('input', function () {
        filterState.query = searchInput.value;
        applyFilters();
      });
    }
  });
})();
"""

// ── JSON serialisation ────────────────────────────────────────────────────────

type private StatusDto = { case_: string }

[<CLIMutable>]
type private DocDto = {
    id          : string
    source      : string
    remotePath  : string
    title       : string
    extension   : string
    tags        : string array
    description : string option
    status      : string
    body        : string option
    pageUrl     : string option
}

[<CLIMutable>]
type private SourceDto = {
    name        : string
    hasManifest : bool
    fileCount   : int
}

let private jsonOpts =
    let o = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true)
    o.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
    o

let private toDocDto (d: SiteDocument) : DocDto = {
    id          = d.Id
    source      = d.Source
    remotePath  = d.RemotePath
    title       = d.Title
    extension   = d.Extension
    tags        = d.Tags |> List.toArray
    description = d.Description
    status      = match d.Status with Pulled -> "pulled" | Cached -> "cached" | IndexOnly -> "index-only"
    body        = d.Body
    pageUrl     = d.PageUrl
}

// ── file helpers ──────────────────────────────────────────────────────────────

let private writeFile (path: string) (content: string) : Result<unit, string> =
    try
        let dir = Path.GetDirectoryName path
        if not (isNull dir) && dir <> "" then Directory.CreateDirectory dir |> ignore
        File.WriteAllText(path, content)
        Ok ()
    with ex -> Error ex.Message

let private writeFileR (path: string) (content: string) : unit =
    writeFile path content |> ignore

// ── generate ─────────────────────────────────────────────────────────────────

let generate (deps: Deps) (cfg: EffectiveConfig) (opts: GenerateOptions) : Result<unit, string> =
    let out = opts.OutputDir

    // build model
    let modelResult = IndexBuilder.buildModel deps cfg
    match modelResult with
    | Error e -> Error e
    | Ok model ->

    // write CSS — style.css is always regenerated; custom.css is user-owned
    let customCssExtra =
        let overrides =
            [
                opts.Theme.PrimaryColor |> Option.map (fun c -> $"  --color-primary: {c};")
                opts.Theme.FontFamily   |> Option.map (fun f -> $"  --font-family: {f};")
            ]
            |> List.choose id
        if overrides.IsEmpty then ""
        else ":root {\n" + (overrides |> String.concat "\n") + "\n}\n"
    writeFileR (Path.Combine(out, "assets/css/style.css")) (css + customCssExtra)

    let customCssPath = Path.Combine(out, "assets/css/custom.css")
    match opts.Theme.CustomCssPath with
    | Some src when File.Exists src ->
        // explicit source file — always sync it into the output
        writeFileR customCssPath (try File.ReadAllText src with _ -> "")
    | _ ->
        // no source file — create a blank placeholder on first run, preserve on subsequent runs
        if not (File.Exists customCssPath) then
            writeFileR customCssPath "/* Add site-specific CSS overrides here. This file is never overwritten by eru. */"

    // write JS
    if opts.Features.ThemeToggle then
        writeFileR (Path.Combine(out, "js/theme.js")) themeJs
    writeFileR (Path.Combine(out, "js/app.js")) appJs

    // write data files
    if opts.Features.Search then
        let docs = model.Documents |> List.map toDocDto |> List.toArray
        writeFileR (Path.Combine(out, "data/documents.json")) (JsonSerializer.Serialize(docs, jsonOpts))
        let srcs = model.Sources |> List.map (fun s -> { name = s.Name; hasManifest = s.HasManifest; fileCount = s.FileCount }) |> List.toArray
        writeFileR (Path.Combine(out, "data/sources.json")) (JsonSerializer.Serialize(srcs, jsonOpts))
        let manifest = $"""{{ "schemaVersion": 1, "documentCount": {model.Documents.Length} }}"""
        writeFileR (Path.Combine(out, "data/manifest.json")) manifest

    // index.html
    writeFileR (Path.Combine(out, "index.html")) (HtmlTemplates.indexPage model)

    // sources/index.html
    writeFileR (Path.Combine(out, "sources/index.html")) (HtmlTemplates.sourcesPage model.Sources)

    // sources/<name>/index.html
    if opts.Features.SourcePages then
        for source in model.Sources do
            let nameSlug = Uri.EscapeDataString source.Name
            writeFileR (Path.Combine(out, $"sources/{nameSlug}/index.html")) (HtmlTemplates.sourceFilesPage source)

    // tags/index.html + tags/<tag>/index.html
    if opts.Features.TagPages then
        writeFileR (Path.Combine(out, "tags/index.html")) (HtmlTemplates.tagsPage model.Tags)
        for tag in model.Tags do
            let tagSlug = Uri.EscapeDataString tag.Name
            writeFileR (Path.Combine(out, $"tags/{tagSlug}/index.html")) (HtmlTemplates.tagFilesPage tag)

    // files/<source>/<slug>.html
    if opts.Features.FilePages then
        for doc in model.Documents do
            match doc.PageUrl, doc.Status with
            | Some _, (Pulled | Cached) ->
                let contentOpt =
                    match doc.Status with
                    | Pulled | Cached ->
                        // find the IndexEntry to get the cacheRelPath
                        match deps.ReadSourceIndex doc.Source with
                        | Ok (Some idx) ->
                            idx
                            |> Map.tryFind doc.RemotePath
                            |> Option.bind (fun e -> e.CacheRelPath)
                            |> Option.bind (fun rel ->
                                match deps.ReadCachedSourceContent doc.Source rel with
                                | Ok (Some content) -> Some content
                                | _ -> None)
                        | _ -> None
                    | _ -> None
                match contentOpt with
                | Some content ->
                    let htmlContent = MarkdownRenderer.render content
                    let sourceSlug = Uri.EscapeDataString doc.Source
                    let fileSlug = doc.RemotePath.Replace('/', '_').Replace('\\', '_').Replace(' ', '-')
                    let filePath = Path.Combine(out, $"files/{sourceSlug}/{fileSlug}.html")
                    writeFileR filePath (HtmlTemplates.filePage doc htmlContent)
                | None -> ()
            | _ -> ()

    // open browser
    if opts.OpenBrowser then
        try
            let absIndex = Path.GetFullPath(Path.Combine(out, "index.html"))
            let psi = Diagnostics.ProcessStartInfo(absIndex, UseShellExecute = true)
            Diagnostics.Process.Start(psi) |> ignore
        with _ -> ()

    Ok ()
