module Eru.Site.SiteGenerator

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Eru
open Eru.Site.Views
open Eru.Site.PageLayout

// ── options ──────────────────────────────────────────────────────────────────

// Page-level switches moved to `site.pages.<id>: false` in config; these remain the
// asset-level switches.
type SiteFeatures = {
    FilePages   : bool
    Search      : bool
    ThemeToggle : bool
    Graph       : bool
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
        Features    = { FilePages = true; Search = true; ThemeToggle = true; Graph = true }
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
  --color-graph-incoming: #065f46;
  --color-graph-outgoing: #92400e;
  --color-graph-external: #6b7280;
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
    --color-graph-incoming: #6ee7b7;
    --color-graph-outgoing: #fcd34d;
    --color-graph-external: #9ca3af;
  }
}
body.theme-light {
  --color-text: #24292e; --color-bg: #ffffff; --color-surface: #f6f8fa;
  --color-border: #e1e4e8; --color-primary: #0366d6;
  --color-badge-bg: #eef2ff; --color-badge-text: #3730a3;
  --color-graph-incoming: #065f46; --color-graph-outgoing: #92400e; --color-graph-external: #6b7280;
}
body.theme-dark {
  --color-text: #c9d1d9; --color-bg: #0d1117; --color-surface: #161b22;
  --color-border: #30363d; --color-primary: #58a6ff;
  --color-badge-bg: #1e2a3a; --color-badge-text: #79b8ff;
  --color-graph-incoming: #6ee7b7; --color-graph-outgoing: #fcd34d; --color-graph-external: #9ca3af;
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
.sidebar { position: sticky; top: 1rem; max-height: calc(100vh - 2rem); overflow-y: auto; overscroll-behavior: contain; }
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
.source-card-desc { font-size: 0.85rem; line-height: 1.5; opacity: 0.85; margin: 0; }
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

/* document link graph */
.doc-graph { max-width: 860px; margin-bottom: 1.25rem; position: relative; }
.doc-graph[hidden] { display: none; }
.doc-graph h3 { margin-bottom: 0.5rem; }
.doc-graph-fallback { display: flex; flex-direction: column; gap: 0.75rem; font-size: 0.85rem; }
.doc-graph-fallback-group ul { list-style: none; display: flex; flex-direction: column; gap: 0.2rem; }
.doc-graph-fallback .related-label {
  display: block; font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.04em;
  opacity: 0.6; margin-bottom: 0.25rem;
}
.doc-graph-fallback .related-unresolved { opacity: 0.55; }
.doc-graph-legend { display: flex; gap: 1rem; flex-wrap: wrap; font-size: 0.75rem; opacity: 0.75; margin-top: 0.5rem; }
.legend-item { position: relative; padding-left: 1rem; }
.legend-item::before {
  content: ""; position: absolute; left: 0; top: 50%; transform: translateY(-50%);
  width: 0.55rem; height: 0.55rem; border-radius: 50%;
}
.legend-focus::before { background: var(--color-primary); }
.legend-incoming::before { background: var(--color-graph-incoming); }
.legend-outgoing::before { background: var(--color-graph-outgoing); }
.doc-graph-svg { width: 100%; height: auto; display: block; }
.doc-graph-svg .graph-node-circle, .doc-graph-svg .graph-node-rect { cursor: pointer; }
.doc-graph-svg a:focus .graph-node-circle,
.doc-graph-svg a:focus .graph-node-rect,
.doc-graph-svg .graph-hit:focus + .graph-node-circle,
.doc-graph-svg .graph-hit:focus + .graph-node-rect {
  outline: 2px solid var(--color-primary); outline-offset: 2px;
}
.doc-graph-svg .graph-edge { cursor: default; }
.doc-graph-tooltip {
  position: absolute;
  z-index: 10;
  max-width: 260px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius);
  padding: 0.5rem 0.65rem;
  font-size: 0.78rem;
  line-height: 1.4;
  box-shadow: 0 2px 8px rgba(0,0,0,0.15);
  pointer-events: none;
  display: none;
}
.doc-graph-tooltip.visible { display: block; }
.doc-graph-tooltip strong { display: block; margin-bottom: 0.15rem; }

/* page grid: left / main / right regions; an absent region leaves no gap */
.page-grid {
  display: grid;
  gap: 1.5rem;
  align-items: start;
  grid-template-columns: minmax(0, 1fr);
  grid-template-areas: "main";
}
.page-grid.has-left { grid-template-columns: 220px minmax(0, 1fr); grid-template-areas: "left main"; }
.page-grid.has-right { grid-template-columns: minmax(0, 1fr) 260px; grid-template-areas: "main right"; }
.page-grid.has-left.has-right { grid-template-columns: 220px minmax(0, 1fr) 260px; grid-template-areas: "left main right"; }
.region-left { grid-area: left; }
.region-main { grid-area: main; min-width: 0; }
.region-right { grid-area: right; position: sticky; top: 1rem; max-height: calc(100vh - 2rem); overflow-y: auto; font-size: 0.85rem; }
@media (max-width: 900px) {
  .page-grid, .page-grid.has-left, .page-grid.has-right, .page-grid.has-left.has-right {
    grid-template-columns: minmax(0, 1fr);
    grid-template-areas: "main" "left" "right";
  }
  .region-right, .sidebar { position: static; max-height: none; }
}
.widget { margin-bottom: 1rem; }
.region-main > .widget:empty { display: none; }
.widget-title { margin-bottom: 0.25rem; }
.widget-title h1 { margin-bottom: 0.5rem; }

/* list views */
.card-grid {
  display: grid;
  gap: 0.75rem;
  grid-template-columns: repeat(auto-fill, minmax(max(260px, calc((100% - (var(--cols, 3) - 1) * 0.75rem) / var(--cols, 3) - 0.5px)), 1fr));
}
.card-grid .file-card { margin-bottom: 0; min-width: 0; }
.list-search { margin-bottom: 1rem; }
.doc-group { margin-bottom: 1.5rem; }
.doc-group-title { font-size: 1.05rem; font-weight: 600; margin-bottom: 0.5rem; }
.doc-list { list-style: none; }
.doc-list-item {
  display: flex; align-items: baseline; gap: 0.6rem; flex-wrap: wrap;
  padding: 0.4rem 0; border-bottom: 1px solid var(--color-border);
}
.doc-list-title { font-weight: 600; }
.doc-list-meta { display: inline-flex; gap: 0.4rem; flex-wrap: wrap; align-items: center; font-size: 0.8rem; opacity: 0.9; }
.doc-table { border-collapse: collapse; width: 100%; font-size: 0.88rem; }
.doc-table th, .doc-table td {
  text-align: left; padding: 0.45rem 0.7rem; border-bottom: 1px solid var(--color-border); vertical-align: top;
}
.doc-table th { background: var(--color-surface); font-weight: 600; white-space: nowrap; user-select: none; }
.doc-table th[data-dir="asc"]::after { content: " \25B2"; font-size: 0.7em; }
.doc-table th[data-dir="desc"]::after { content: " \25BC"; font-size: 0.7em; }
.stale-date.is-stale { color: #b45309; font-weight: 600; }

/* widgets */
.doc-meta-badges { display: flex; gap: 0.4rem; flex-wrap: wrap; }
.badge-type { background: var(--color-badge-bg); color: var(--color-badge-text); }
.badge-lifecycle { background: var(--color-surface); border: 1px solid var(--color-border); color: var(--color-text); }
.badge-trust { background: var(--color-surface); border: 1px solid var(--color-border); color: var(--color-text); }
.badge-human-reviewed { background: #d1fae5; color: #065f46; border-color: transparent; }
.badge-machine-confirmed { background: #fef3c7; color: #92400e; border-color: transparent; }
body.theme-dark .badge-human-reviewed { background: #064e3b; color: #6ee7b7; }
body.theme-dark .badge-machine-confirmed { background: #451a03; color: #fcd34d; }
.doc-verified, .doc-generated { display: flex; flex-direction: column; gap: 0.3rem; align-items: flex-start; font-size: 0.85rem; }
.doc-generated { flex-direction: row; flex-wrap: wrap; align-items: baseline; gap: 0.4rem; }
.verifier-list { list-style: none; display: flex; flex-direction: column; gap: 0.15rem; }
.actor-kind { font-size: 0.68rem; text-transform: uppercase; letter-spacing: 0.04em; opacity: 0.6; }
.doc-stale-warning {
  background: #fef3c7; color: #92400e; border-radius: var(--radius); padding: 0.4rem 0.7rem; font-size: 0.85rem;
}
body.theme-dark .doc-stale-warning { background: #451a03; color: #fcd34d; }
.doc-related h3 { margin-bottom: 0.5rem; }
.related-lists { display: flex; flex-direction: column; gap: 0.75rem; font-size: 0.85rem; }
.related-group ul { list-style: none; display: flex; flex-direction: column; gap: 0.2rem; }
.related-group .related-label { display: block; font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.04em; opacity: 0.6; margin-bottom: 0.25rem; }
.related-unresolved { opacity: 0.55; }
.toc h3 { margin-bottom: 0.4rem; }
.toc ul { list-style: none; }
.toc li { padding: 0.15rem 0; }
.toc .toc-l3 { padding-left: 0.8rem; }
.toc .toc-l4 { padding-left: 1.6rem; }
.side-links { list-style: none; }
.side-links li { padding: 0.15rem 0; }
.empty-note { opacity: 0.6; }
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
  // Declarative filter contract (see HtmlTemplates):
  //   [data-doc]                 a filterable document (card, list item or table row)
  //   data-<facet>               JSON array of the document's values for that facet
  //   [data-facet="<name>"]      a sidebar list that filters that facet (inside [data-facet-mode="filter"])
  //   [data-doc-group]           a section hidden when none of its documents is visible
  //   [data-doc-count]           receives the visible document count
  // Matching: OR within a facet, AND across facets, plus a text search.
  var facetState = {};      // facet name -> { value: true }
  var query = '';
  var jsonIndex = null;
  var matchedPaths = null;  // Set of matched ids
  var matchedAttr = 'id';   // 'path' (api search) or 'id' (json search)

  function valuesOf(el, facet) {
    try {
      var v = JSON.parse(el.getAttribute('data-' + facet) || '[]');
      return Array.isArray(v) ? v : [v];
    } catch (e) { return []; }
  }

  function render() {
    var q = query.toLowerCase().trim();
    var facets = Object.keys(facetState);
    var visible = 0;
    document.querySelectorAll('[data-doc]').forEach(function (el) {
      var facetsOk = facets.every(function (f) {
        var active = Object.keys(facetState[f]).filter(function (k) { return facetState[f][k]; });
        if (active.length === 0) return true;
        return valuesOf(el, f).some(function (v) { return active.indexOf(v) >= 0; });
      });
      var textOk;
      if (!q) {
        textOk = true;
      } else if (matchedPaths !== null) {
        var attr = matchedAttr === 'path' ? (el.dataset.path || '') : (el.dataset.id || '');
        textOk = matchedPaths.has(attr);
      } else {
        textOk = el.textContent.toLowerCase().indexOf(q) >= 0;
      }
      var show = facetsOk && textOk;
      el.style.display = show ? '' : 'none';
      if (show) visible++;
    });
    document.querySelectorAll('[data-doc-group]').forEach(function (g) {
      var any = Array.prototype.some.call(g.querySelectorAll('[data-doc]'), function (el) { return el.style.display !== 'none'; });
      g.style.display = any ? '' : 'none';
    });
    document.querySelectorAll('[data-doc-count]').forEach(function (c) { c.textContent = '(' + visible + ')'; });
  }

  function applyJsonSearch(q) {
    if (jsonIndex !== null) {
      var terms = q.split(/\s+/).filter(Boolean);
      matchedPaths = new Set();
      matchedAttr = 'id';
      jsonIndex.forEach(function (entry) {
        var ok = terms.every(function (t) { return entry.s.indexOf(t) >= 0; });
        if (ok) matchedPaths.add(entry.id);
      });
    } else {
      matchedPaths = null;
    }
    render();
  }

  function applyFilters() {
    var q = query.toLowerCase().trim();
    if (q && window.location.protocol !== 'file:') {
      fetch('/api/search?q=' + encodeURIComponent(q))
        .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
        .then(function (result) {
          matchedPaths = new Set((result.hits || []).map(function (h) { return h.path; }));
          matchedAttr = 'id';
          render();
        })
        .catch(function () { applyJsonSearch(q); });
    } else {
      if (q) {
        applyJsonSearch(q);
      } else {
        matchedPaths = null;
        render();
      }
    }
  }

  function loadJsonIndex() {
    if (!window.ERU_DATA_ROOT) return;
    if (window.location.protocol === 'file:') {
      console.warn('[eru] file:// mode — using DOM text search (title, description, tags only). Run "eru site serve" for full search.');
      return;
    }
    fetch(window.ERU_DATA_ROOT + 'documents.json')
      .then(function(r) { return r.ok ? r.json() : Promise.reject(r.status); })
      .then(function(docs) {
        jsonIndex = docs.map(function(d) {
          var parts = [d.title, d.description, d.body].concat(d.tags || []);
          return { id: d.id, s: parts.filter(Boolean).join(' ').toLowerCase() };
        });
        applyFilters();
      })
      .catch(function() {
        console.warn('[eru] Static mode — using DOM text search (title, description, tags only). Run "eru site serve" for full search.');
      });
  }

  // Upgrades a facet list of plain links to checkboxes; the list's data-facet names the facet.
  function upgradeFacet(ul) {
    var facet = ul.getAttribute('data-facet');
    facetState[facet] = facetState[facet] || {};
    ul.querySelectorAll('li').forEach(function (li) {
      var a = li.querySelector('a');
      if (!a) return;
      var countEl = li.querySelector('.count');
      var value = a.getAttribute('data-value') || a.textContent.trim();
      var id = 'chk-' + facet + '-' + value.replace(/[^a-zA-Z0-9]/g, '_');
      var cb = document.createElement('input');
      cb.type = 'checkbox';
      cb.id = id;
      cb.value = value;
      var label = document.createElement('label');
      label.setAttribute('for', id);
      label.textContent = a.textContent.trim();
      while (li.firstChild) li.removeChild(li.firstChild);
      li.appendChild(cb);
      li.appendChild(label);
      if (countEl) li.appendChild(countEl);
      cb.addEventListener('change', function () {
        facetState[facet][value] = cb.checked;
        applyFilters();
      });
    });
  }

  // Click-to-sort headers for table views.
  function enableTableSort(table) {
    table.querySelectorAll('th[data-sortable]').forEach(function (th, idx) {
      th.style.cursor = 'pointer';
      th.addEventListener('click', function () {
        var asc = th.getAttribute('data-dir') !== 'asc';
        table.querySelectorAll('th').forEach(function (o) { o.removeAttribute('data-dir'); });
        th.setAttribute('data-dir', asc ? 'asc' : 'desc');
        var body = table.tBodies[0];
        var rows = Array.prototype.slice.call(body.rows);
        rows.sort(function (r1, r2) {
          var a = r1.cells[idx].textContent.trim(), b = r2.cells[idx].textContent.trim();
          var c = a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
          return asc ? c : -c;
        });
        rows.forEach(function (r) { body.appendChild(r); });
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
    document.querySelectorAll('[data-facet-mode="filter"] [data-facet]').forEach(upgradeFacet);
    document.querySelectorAll('table.doc-table').forEach(enableTableSort);

    var searchContainer = document.getElementById('search-container');
    if (searchContainer) searchContainer.style.display = '';

    var searchInput = document.getElementById('search-input');
    if (searchInput) {
      searchInput.addEventListener('input', function () {
        query = searchInput.value;
        applyFilters();
      });
    }

    loadJsonIndex();
  });
})();

(function () {
  if (window.location.protocol === 'file:') return;
  var POLL_MS = 3000;
  var knownVersion = null;
  var timer = null;

  function checkVersion() {
    fetch('/api/version', { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
      .then(function (data) {
        if (data.version !== knownVersion) location.reload();
      })
      .catch(function () {});
  }

  function startPolling() {
    if (timer === null) timer = setInterval(checkVersion, POLL_MS);
  }

  function stopPolling() {
    if (timer !== null) { clearInterval(timer); timer = null; }
  }

  function warnStaticMode() {
    console.warn('[eru] Static mode — fallen back to JSON document search, live reload unavailable. Run "eru site serve" for full search and live reload.');
  }

  fetch('/api/version', { cache: 'no-store' })
    .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
    .then(function (data) {
      knownVersion = data.version;
      if (document.visibilityState === 'visible') startPolling();
      document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') startPolling();
        else stopPolling();
      });
    })
    .catch(warnStaticMode);
})();
"""

let private graphJs = """
(function () {
  var SVGNS = 'http://www.w3.org/2000/svg';

  function svgEl(tag, attrs) {
    var el = document.createElementNS(SVGNS, tag);
    if (attrs) {
      Object.keys(attrs).forEach(function (k) { el.setAttribute(k, attrs[k]); });
    }
    return el;
  }

  function truncate(s, n) {
    if (!s) return '';
    return s.length > n ? s.slice(0, n - 1) + '…' : s;
  }

  function rootPrefix() {
    var root = window.ERU_DATA_ROOT || '';
    return root.replace(/data\/$/, '');
  }

  function buildTooltip(container) {
    var el = document.createElement('div');
    el.className = 'doc-graph-tooltip';
    container.appendChild(el);
    return el;
  }

  function showTooltip(tooltip, container, target, title, description) {
    var html = '<strong>' + escapeHtml(title) + '</strong>';
    if (description) html += escapeHtml(description);
    tooltip.innerHTML = html;
    tooltip.classList.add('visible');
    var cRect = container.getBoundingClientRect();
    var tRect = target.getBoundingClientRect();
    var x = tRect.left - cRect.left + tRect.width / 2;
    var y = tRect.top - cRect.top;
    tooltip.style.left = Math.max(0, x - 60) + 'px';
    tooltip.style.top = Math.max(0, y - 8 - tooltip.offsetHeight) + 'px';
  }

  function hideTooltip(tooltip) {
    tooltip.classList.remove('visible');
  }

  function escapeHtml(s) {
    var div = document.createElement('div');
    div.textContent = s;
    return div.innerHTML;
  }

  function wireHover(el, tooltip, container, title, description) {
    el.setAttribute('tabindex', '0');
    el.addEventListener('mouseenter', function () { showTooltip(tooltip, container, el, title, description); });
    el.addEventListener('focus', function () { showTooltip(tooltip, container, el, title, description); });
    el.addEventListener('mouseleave', function () { hideTooltip(tooltip); });
    el.addEventListener('blur', function () { hideTooltip(tooltip); });
  }

  function colorFor(kind) {
    var style = getComputedStyle(document.documentElement);
    return style.getPropertyValue(kind).trim();
  }

  function drawNode(svg, tooltip, container, colors, pos, node, cls, radius) {
    var group = svgEl('g', { class: 'graph-node' });
    var isExternal = node.kind === 'external';
    var href = isExternal ? node.id : (node.pageUrl ? rootPrefix() + node.pageUrl : null);
    var clickable = svgEl(href ? 'a' : 'g', {});
    if (href) {
      clickable.setAttribute('href', href);
      if (isExternal) {
        clickable.setAttribute('target', '_blank');
        clickable.setAttribute('rel', 'noopener');
      }
    }
    var shape;
    if (isExternal) {
      shape = svgEl('rect', {
        class: 'graph-node-rect', x: pos.x - radius, y: pos.y - radius * 0.7,
        width: radius * 2, height: radius * 1.4, rx: 6,
        fill: 'var(--color-bg)', stroke: colors.external, 'stroke-dasharray': '4 3', 'stroke-width': 1.5
      });
    } else {
      shape = svgEl('circle', {
        class: 'graph-node-circle', cx: pos.x, cy: pos.y, r: radius,
        fill: colors[cls], stroke: 'var(--color-bg)', 'stroke-width': 2
      });
    }
    clickable.appendChild(shape);
    var label = svgEl('text', {
      x: pos.x, y: pos.y + radius + 14, 'text-anchor': 'middle',
      fill: 'var(--color-text)', 'font-size': '11'
    });
    label.textContent = truncate(node.title, 20);
    clickable.appendChild(label);
    group.appendChild(clickable);
    svg.appendChild(group);
    wireHover(clickable, tooltip, container, node.title, node.description);
    return clickable;
  }

  function drawEdge(svg, tooltip, container, colors, x1, y1, x2, y2, direction, description, neighborTitle) {
    var color = direction === 'incoming' ? colors.incoming : colors.outgoing;
    var markerId = 'graph-arrow-' + direction;
    var visible = svgEl('line', {
      x1: x1, y1: y1, x2: x2, y2: y2,
      stroke: color, 'stroke-width': 1.5, 'marker-end': 'url(#' + markerId + ')'
    });
    var hit = svgEl('line', {
      class: 'graph-edge', x1: x1, y1: y1, x2: x2, y2: y2,
      stroke: 'transparent', 'stroke-width': 10
    });
    svg.appendChild(visible);
    svg.appendChild(hit);
    wireHover(hit, tooltip, container, neighborTitle, description);
  }

  function addArrowMarkers(svg, colors) {
    var defs = svgEl('defs');
    ['incoming', 'outgoing'].forEach(function (dir) {
      var marker = svgEl('marker', {
        id: 'graph-arrow-' + dir, viewBox: '0 0 10 10', refX: 9, refY: 5,
        markerWidth: 6, markerHeight: 6, orient: 'auto-start-reverse'
      });
      var path = svgEl('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: dir === 'incoming' ? colors.incoming : colors.outgoing });
      marker.appendChild(path);
      defs.appendChild(marker);
    });
    svg.appendChild(defs);
  }

  function renderGraph(container, graph, nodeId) {
    var nodesById = {};
    (graph.nodes || []).forEach(function (n) { nodesById[n.id] = n; });

    var neighbors = {}; // id -> { node, direction, description }
    (graph.edges || []).forEach(function (e) {
      if (e.from === nodeId && e.to !== nodeId) {
        var n = nodesById[e.to] || { id: e.to, kind: 'internal', title: e.to };
        if (!neighbors[n.id] || (!neighbors[n.id].description && e.description)) {
          neighbors[n.id] = { node: n, direction: 'outgoing', description: e.description };
        }
      }
      if (e.to === nodeId && e.from !== nodeId) {
        var n2 = nodesById[e.from] || { id: e.from, kind: 'internal', title: e.from };
        if (!neighbors[n2.id] || (!neighbors[n2.id].description && e.description)) {
          neighbors[n2.id] = { node: n2, direction: 'incoming', description: e.description };
        }
      }
    });

    var incoming = [];
    var outgoing = [];
    Object.keys(neighbors).forEach(function (id) {
      var entry = neighbors[id];
      if (entry.direction === 'incoming') incoming.push(entry);
      else outgoing.push(entry);
    });

    if (!incoming.length && !outgoing.length) return; // keep fallback list

    var rowH = 46;
    var rows = Math.max(incoming.length, outgoing.length, 1);
    var W = 640;
    var H = Math.max(140, rows * rowH + 60);
    var cx = W / 2, cy = H / 2;

    var colors = {
      focus: colorFor('--color-primary'),
      incoming: colorFor('--color-graph-incoming'),
      outgoing: colorFor('--color-graph-outgoing'),
      external: colorFor('--color-graph-external')
    };

    var svg = svgEl('svg', { viewBox: '0 0 ' + W + ' ' + H, class: 'doc-graph-svg', role: 'img', 'aria-label': 'Document link graph' });
    addArrowMarkers(svg, colors);

    var tooltip = buildTooltip(container);

    function place(list, x) {
      var n = list.length;
      var startY = cy - ((n - 1) * rowH) / 2;
      return list.map(function (entry, i) { return { entry: entry, x: x, y: startY + i * rowH }; });
    }

    place(incoming, 90).forEach(function (p) {
      drawEdge(svg, tooltip, container, colors, p.x, p.y, cx, cy, 'incoming', p.entry.description, p.entry.node.title);
      drawNode(svg, tooltip, container, colors, p, p.entry.node, 'incoming', 16);
    });
    place(outgoing, W - 90).forEach(function (p) {
      drawEdge(svg, tooltip, container, colors, cx, cy, p.x, p.y, 'outgoing', p.entry.description, p.entry.node.title);
      drawNode(svg, tooltip, container, colors, p, p.entry.node, 'outgoing', 16);
    });

    var focusNode = nodesById[nodeId] || { id: nodeId, kind: 'internal', title: nodeId };
    drawNode(svg, tooltip, container, colors, { x: cx, y: cy }, focusNode, 'focus', 20);

    container.innerHTML = '';
    container.appendChild(svg);
    container.appendChild(tooltip);
  }

  document.addEventListener('DOMContentLoaded', function () {
    var container = document.getElementById('doc-graph');
    if (!container) return;
    var nodeId = container.getAttribute('data-node-id');
    if (!nodeId || !window.ERU_DATA_ROOT || window.location.protocol === 'file:') return;
    fetch(window.ERU_DATA_ROOT + 'graph.json')
      .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
      .then(function (graph) {
        renderGraph(container, graph, nodeId);
        var section = container.closest('.doc-graph');
        if (section) section.hidden = false;
      })
      .catch(function () {
        console.warn('[eru] Could not load graph.json — the graph stays hidden; linked documents are listed by the related widget.');
      });
  });
})();
"""

// ── JSON serialisation ────────────────────────────────────────────────────────

type DocDto = {
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
    bundle      : string option
}

type SourceDto = {
    name        : string
    hasManifest : bool
    fileCount   : int
}

type GraphNodeDto = {
    id          : string
    kind        : string
    title       : string
    description : string option
    pageUrl     : string option
}

type GraphEdgeDto = {
    from        : string
    ``to``      : string
    description : string option
}

type GraphDto = {
    nodes : GraphNodeDto array
    edges : GraphEdgeDto array
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
    status      = match d.SyncStatus with Pulled -> "pulled" | Cached -> "cached" | IndexOnly -> "index-only"
    body        = d.Body
    pageUrl     = d.PageUrl
    bundle      = d.Bundle
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

// ── content pages ────────────────────────────────────────────────────────────

/// Repo-relative Markdown files that custom pages render; `eru site serve` watches these.
let referencedContentFiles (cfg: EffectiveConfig) : string list =
    cfg.Site.Pages
    |> List.choose (function
        | _, Defined { Content = Some (ContentFile f) } -> SiteSettings.normalizeRelPath f
        | _ -> None)
    |> List.distinct

let private hasScheme (s: string) = System.Text.RegularExpressions.Regex.IsMatch(s, @"^[a-zA-Z][a-zA-Z0-9+.\-]*:")

/// Rewrites a link in rendered content to a site URL when it points at another content page,
/// a pulled local file or an indexed document; anything else is left alone.
let private rewriteLink
    (localTargets: Map<string, string>) (docUrls: Map<string, string>)
    (prefix: string) (baseDir: string) (sourceName: string option) (href: string) : string option =
    if href = "" || href.StartsWith "#" || href.StartsWith "/" then None
    else
        let hashAt = href.IndexOf '#'
        let path, frag = if hashAt < 0 then href, "" else href.Substring(0, hashAt), href.Substring hashAt
        let path = (try Uri.UnescapeDataString path with _ -> path)
        let site (url: string) = Some (prefix + url + frag)
        if hasScheme path then
            docUrls |> Map.tryFind path |> Option.bind site
        else
            let joined = if baseDir = "" then path else baseDir + "/" + path
            match SiteSettings.normalizeRelPath joined with
            | None -> None
            | Some rel ->
                match localTargets |> Map.tryFind rel with
                | Some url -> site url
                | None ->
                    sourceName
                    |> Option.bind (fun src -> docUrls |> Map.tryFind $"{src}:{rel}")
                    |> Option.bind site

// ── generate ─────────────────────────────────────────────────────────────────

let private dirOf (path: string) =
    match path.LastIndexOf '/' with
    | -1 -> ""
    | i -> path.Substring(0, i)

let generate (deps: Deps) (cfg: EffectiveConfig) (opts: GenerateOptions) : Result<unit, string> =
    let out = opts.OutputDir
    let settings = cfg.Site

    match SiteSettings.validate settings with
    | Error e -> Error e
    | Ok () ->

    // build model
    let modelResult = IndexBuilder.buildModel deps cfg
    match modelResult with
    | Error e -> Error e
    | Ok model ->

    // build link graph
    let graphResult = LinkGraph.execute deps { SourceFilter = None }
    match graphResult with
    | Error e -> Error e
    | Ok graph ->

    let docsById = model.Documents |> List.map (fun d -> d.Id, d) |> Map.ofList

    let nodeInfo (n: LinkGraph.NodeId) : {| Id: string; Kind: string; Title: string; Description: string option; PageUrl: string option |} =
        match n with
        | LinkGraph.ExternalNode url ->
            {| Id = url; Kind = "external"; Title = url; Description = None; PageUrl = None |}
        | LinkGraph.InternalNode entryId ->
            let key = EntryId.toString entryId
            match docsById.TryFind key with
            | Some d -> {| Id = key; Kind = "internal"; Title = d.Title; Description = d.Description; PageUrl = d.PageUrl |}
            | None -> {| Id = key; Kind = "internal"; Title = Path.GetFileName entryId.RemotePath; Description = None; PageUrl = None |}

    let relatedFor (doc: SiteDocument) : RelatedLinks =
        let selfNode = LinkGraph.InternalNode { Source = doc.Source; RemotePath = doc.RemotePath }
        let edges = LinkGraph.edgesFor graph selfNode
        let toRelated (n: LinkGraph.NodeId) : RelatedDoc =
            let info = nodeInfo n
            { Id = info.Id; Title = info.Title; PageUrl = info.PageUrl; IsExternal = info.Kind = "external" }
        { Incoming = edges.Incoming |> List.distinct |> List.map toRelated
          Outgoing = edges.Outgoing |> List.distinct |> List.map toRelated }

    // cached content of an indexed document (pulled and cached docs both read from the cache)
    let cachedContent (doc: SiteDocument) : string option =
        match doc.SyncStatus with
        | Pulled | Cached ->
            match deps.ReadSourceIndex doc.Source with
            | Ok (Some idx) ->
                idx.Entries
                |> Map.tryFind doc.RemotePath
                |> Option.bind (fun e -> e.CacheRelPath)
                |> Option.bind (fun rel ->
                    match deps.ReadCachedSourceContent doc.Source rel with
                    | Ok (Some content) -> Some content
                    | _ -> None)
            | _ -> None
        | IndexOnly -> None

    let now = deps.GetUtcNow()
    let specs = PageSpecs.resolve settings
    let env : Widgets.SiteEnv = { Model = model; PageLinks = PageSpecs.pageLinks specs }
    let isLive (id: string) = env.PageLinks.ContainsKey id

    let sourceUrlFor (d: SiteDocument) : string option =
        cfg.Sources
        |> List.tryFind (fun s -> s.Name = d.Source)
        |> Option.bind (fun s -> s.Url |> Option.bind (fun u -> Views.sourceFileUrl u s.Branch d.RemotePath))

    let viewContext (prefix: string) (def: PageDef option) : Views.ViewContext =
        { Prefix = prefix; TagsLive = isLive "tags"; Open = PageLayout.openTarget settings def; SourceUrl = sourceUrlFor }

    // link targets for rewriting links inside content pages
    let docUrls =
        model.Documents |> List.choose (fun d -> d.PageUrl |> Option.map (fun u -> d.Id, u)) |> Map.ofList
    let localTargets =
        let fromPages =
            specs |> List.choose (fun sp ->
                match sp.Def with
                | Some { Content = Some (ContentFile f) } -> SiteSettings.normalizeRelPath f |> Option.map (fun n -> n, sp.Url)
                | _ -> None)
        let fromLock =
            model.Sources |> List.collect (fun src ->
                match deps.ReadSourceIndex src.Name with
                | Ok (Some idx) ->
                    idx.Entries |> Map.toList |> List.choose (fun (remote, e) ->
                        match e.LocalPath, docUrls |> Map.tryFind $"{src.Name}:{remote}" with
                        | Some lp, Some url -> SiteSettings.normalizeRelPath lp |> Option.map (fun n -> n, url)
                        | _ -> None)
                | _ -> [])
        Map.ofList (fromLock @ fromPages)

    // load every content page up front so a bad reference fails before anything is written
    let loadContent (spec: PageSpecs.PageSpec) (def: PageDef) (content: ContentRef) : Result<string * SiteDocument option, string> =
        let path = $"site.pages.{spec.Id}.content"
        let prefix = prefixFor (PageSpecs.depthOf spec.Url)
        match content with
        | ContentFile f ->
            match SiteSettings.normalizeRelPath f with
            | None -> Error $"{path}.file '{f}' must be a path inside the repository"
            | Some rel ->
                match deps.ReadLocalFile (Path.Combine(deps.GetCwd(), rel)) with
                | Error e -> Error $"{path}.file '{f}' could not be read: {e}"
                | Ok None -> Error $"{path}.file '{f}' does not exist"
                | Ok (Some text) ->
                    Ok (MarkdownRenderer.renderWith (rewriteLink localTargets docUrls prefix (dirOf rel) None) text, None)
        | ContentDoc d ->
            match model.Documents |> List.tryFind (fun x -> x.Id = d) with
            | None -> Error $"{path}.doc '{d}' does not match any indexed document"
            | Some doc ->
                match cachedContent doc with
                | None -> Error $"{path}.doc '{d}' is not cached; run 'eru sync' to pull it"
                | Some text ->
                    Ok (MarkdownRenderer.renderWith (rewriteLink localTargets docUrls prefix (dirOf doc.RemotePath) (Some doc.Source)) text, Some doc)
        |> fun r -> ignore def; r

    let contents =
        specs
        |> List.fold (fun acc spec ->
            acc |> Result.bind (fun (m: Map<string, string * SiteDocument option>) ->
                match spec.Def |> Option.bind (fun d -> d.Content |> Option.map (fun c -> d, c)) with
                | None -> Ok m
                | Some (def, c) -> loadContent spec def c |> Result.map (fun r -> Map.add spec.Id r m))) (Ok Map.empty)

    match contents with
    | Error e -> Error e
    | Ok contents ->

    // ── render every page into memory ────────────────────────────────────────
    let pages = ResizeArray<string * string>()
    let add (path: string) (html: string) = pages.Add((path, html))

    let docList (prefix: string) (def: PageDef option) (docs: SiteDocument list) : string =
        let vc = viewContext prefix def
        let spec = PageLayout.viewSpec settings def
        match def |> Option.bind (fun d -> d.Group) with
        | Some g -> Views.renderGroups vc spec (FilterEngine.group g docs)
        | None -> Views.renderDocs vc spec docs

    for spec in specs do
        let depth = PageSpecs.depthOf spec.Url
        let p = prefixFor depth
        let data, kind, pageLayout =
            match spec.Def with
            | Some def ->
                let docs =
                    def.Filter
                    |> Option.map (fun f ->
                        model.Documents
                        |> FilterEngine.apply now f
                        |> FilterEngine.sort def.Sort
                        |> FilterEngine.limit def.Limit)
                let content = contents |> Map.tryFind spec.Id
                let kind =
                    match def.Content, def.Filter with
                    | Some _, Some _ -> ContentListPage
                    | Some _, None -> ContentPage
                    | None, _ -> ListPage
                let contentDoc = content |> Option.bind snd
                { Widgets.PageData.empty with
                    Title = spec.Title
                    ContentHtml = content |> Option.map fst
                    Doc = contentDoc
                    Related = contentDoc |> Option.map relatedFor
                    ListHtml = docs |> Option.map (docList p (Some def))
                    DocCount = docs |> Option.map List.length
                    Searchable = docs.IsSome
                    FacetMode = if spec.Id = "browse" then Widgets.FacetFilter else Widgets.FacetLinks },
                kind, def.Layout
            | None ->
                let basePage = { Widgets.PageData.empty with Title = spec.Title }
                let listPage (html: string) = { basePage with ListHtml = Some html }
                let data =
                    match spec.Id with
                    | "browse" ->
                        { basePage with
                            ListHtml = Some (docList p None model.Documents)
                            DocCount = Some model.Documents.Length
                            Searchable = true
                            FacetMode = Widgets.FacetFilter }
                    | "sources" -> listPage (HtmlTemplates.sourcesListHtml p model.Sources)
                    | "tags" -> listPage (HtmlTemplates.tagsListHtml p model.Tags)
                    | "types" -> listPage (HtmlTemplates.typesListHtml p model.Types)
                    | _ -> listPage (HtmlTemplates.bundlesListHtml p model.Bundles)
                data, ListPage, Layout.empty
        let slots = PageLayout.forPage settings kind pageLayout
        add spec.Url (HtmlTemplates.render env depth slots data)

    let detailPage (parentId: string) (parentLabel: string) (depth: int) (title: string) (titleHtml: string option)
                   (cli: (string * string) list) (docs: SiteDocument list) : string =
        let p = prefixFor depth
        let data = HtmlTemplates.detailPageData env depth parentId parentLabel title titleHtml cli (docList p None docs) docs.Length
        HtmlTemplates.render env depth (PageLayout.forPage settings DetailPage Layout.empty) data

    // sources/<name>/index.html
    if isLive "sources" then
        for source in model.Sources do
            let manifestBadge =
                if source.HasManifest then """<span class="badge badge-manifest">manifest</span>"""
                else """<span class="badge badge-no-manifest">no manifest</span>"""
            let addRef = source.Url |> Option.defaultValue source.Name
            let titleHtml = $"{Views.escapeHtml source.Name} {manifestBadge}"
            add $"sources/{Uri.EscapeDataString source.Name}/index.html"
                (detailPage "sources" "Sources" 2 source.Name (Some titleHtml) [ "add source", $"eru source add {addRef}" ] source.Files)

    // tags/<tag>/index.html
    if isLive "tags" then
        for tag in model.Tags do
            add $"tags/{Uri.EscapeDataString tag.Name}/index.html"
                (detailPage "tags" "Tags" 2 $"#{tag.Name}" None
                    [ "pull all", $"eru add --tag {tag.Name}"; "search", $"eru search --tag {tag.Name}" ] tag.Files)

    // types/<type>/index.html
    if isLive "types" then
        for typ in model.Types do
            add $"types/{Uri.EscapeDataString typ.Name}/index.html"
                (detailPage "types" "Types" 2 typ.Name None [] typ.Files)

    // bundles/<source>/<path>/index.html
    if isLive "bundles" then
        for bundle in model.Bundles do
            add (HtmlTemplates.bundleUrl bundle)
                (detailPage "bundles" "Bundles" (Views.bundleDepth bundle) bundle.Name None [] bundle.Files)

    // files/<source>/<slug>.html
    if opts.Features.FilePages then
        for doc in model.Documents do
            match doc.PageUrl with
            | Some _ ->
                match cachedContent doc with
                | Some content ->
                    let htmlContent = MarkdownRenderer.render content
                    let sourceSlug = Uri.EscapeDataString doc.Source
                    let fileSlug = doc.RemotePath.Replace('/', '_').Replace('\\', '_').Replace(' ', '-')
                    let data = HtmlTemplates.documentPageData env doc htmlContent (relatedFor doc)
                    add $"files/{sourceSlug}/{fileSlug}.html" (HtmlTemplates.render env 2 (PageLayout.forDocument settings now doc) data)
                | None -> ()
            | None -> ()

    // ── write ────────────────────────────────────────────────────────────────

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
    if opts.Features.Graph then
        writeFileR (Path.Combine(out, "js/graph.js")) graphJs

    // write data files
    if opts.Features.Search then
        let docs = model.Documents |> List.map toDocDto |> List.toArray
        writeFileR (Path.Combine(out, "data/documents.json")) (JsonSerializer.Serialize(docs, jsonOpts))
        let srcs = model.Sources |> List.map (fun s -> { name = s.Name; hasManifest = s.HasManifest; fileCount = s.FileCount }) |> List.toArray
        writeFileR (Path.Combine(out, "data/sources.json")) (JsonSerializer.Serialize(srcs, jsonOpts))
        let manifest = $"""{{ "schemaVersion": 1, "documentCount": {model.Documents.Length} }}"""
        writeFileR (Path.Combine(out, "data/manifest.json")) manifest

    if opts.Features.Graph then
        let nodeDtos =
            graph.Nodes
            |> List.map (fun n ->
                let info = nodeInfo n
                { id = info.Id; kind = info.Kind; title = info.Title; description = info.Description; pageUrl = info.PageUrl })
            |> List.toArray
        let edgeDtos =
            graph.Edges
            |> List.map (fun e -> { from = LinkGraph.nodeKey e.From; ``to`` = LinkGraph.nodeKey e.To; description = e.Description })
            |> List.toArray
        let graphDto : GraphDto = { nodes = nodeDtos; edges = edgeDtos }
        writeFileR (Path.Combine(out, "data/graph.json")) (JsonSerializer.Serialize(graphDto, jsonOpts))

    for path, html in pages do
        writeFileR (Path.Combine(out, path)) html

    // open browser
    if opts.OpenBrowser then
        try
            let absIndex = Path.GetFullPath(Path.Combine(out, "index.html"))
            let psi = Diagnostics.ProcessStartInfo(absIndex, UseShellExecute = true)
            Diagnostics.Process.Start(psi) |> ignore
        with _ -> ()

    Ok ()
