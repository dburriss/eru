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
        Features    = { TagPages = true; SourcePages = true; FilePages = true; Search = true; ThemeToggle = true; Graph = true }
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
  var jsonIndex = null;
  var matchedPaths = null;  // Set of matched values
  var matchedAttr = 'id';   // 'path' (api search) or 'id' (json search)

  function renderCards() {
    var srcActive = Object.keys(filterState.sources).filter(function (k) { return filterState.sources[k]; });
    var extActive = Object.keys(filterState.exts).filter(function (k) { return filterState.exts[k]; });
    var tagActive = Object.keys(filterState.tags).filter(function (k) { return filterState.tags[k]; });
    var q = filterState.query.toLowerCase().trim();
    var cards = document.querySelectorAll('.file-card');
    var visible = 0;
    cards.forEach(function (card) {
      var src = card.dataset.source || '';
      var ext = card.dataset.ext || '';
      var cardTags = (card.dataset.tags || '').split(' ').filter(Boolean);
      var srcOk = srcActive.length === 0 || srcActive.indexOf(src) >= 0;
      var extOk = extActive.length === 0 || extActive.indexOf(ext) >= 0;
      var tagOk = tagActive.length === 0 || tagActive.some(function (t) { return cardTags.indexOf(t) >= 0; });
      var textOk;
      if (!q) {
        textOk = true;
      } else if (matchedPaths !== null) {
        var attr = matchedAttr === 'path' ? (card.dataset.path || '') : (card.dataset.id || '');
        textOk = matchedPaths.has(attr);
      } else {
        textOk = card.textContent.toLowerCase().indexOf(q) >= 0;
      }
      var show = srcOk && extOk && tagOk && textOk;
      card.style.display = show ? '' : 'none';
      if (show) visible++;
    });
    var counter = document.getElementById('file-count');
    if (counter) counter.textContent = '(' + visible + ')';
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
    renderCards();
  }

  function applyFilters() {
    var q = filterState.query.toLowerCase().trim();
    if (q && window.location.protocol !== 'file:') {
      fetch('/api/search?q=' + encodeURIComponent(q))
        .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
        .then(function (result) {
          matchedPaths = new Set((result.hits || []).map(function (h) { return h.path; }));
          matchedAttr = 'id';
          renderCards();
        })
        .catch(function () { applyJsonSearch(q); });
    } else {
      if (q) {
        applyJsonSearch(q);
      } else {
        matchedPaths = null;
        renderCards();
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
      .then(function (graph) { renderGraph(container, graph, nodeId); })
      .catch(function () {
        console.warn('[eru] Could not load graph.json — showing plain-text linked document list.');
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

// ── generate ─────────────────────────────────────────────────────────────────

let generate (deps: Deps) (cfg: EffectiveConfig) (opts: GenerateOptions) : Result<unit, string> =
    let out = opts.OutputDir

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
        { Incoming = edges.Incoming |> List.map toRelated
          Outgoing = edges.Outgoing |> List.map toRelated }

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

    // types/index.html + types/<type>/index.html
    if opts.Features.TagPages then
        writeFileR (Path.Combine(out, "types/index.html")) (HtmlTemplates.typesPage model.Types)
        for typ in model.Types do
            let typeSlug = Uri.EscapeDataString typ.Name
            writeFileR (Path.Combine(out, $"types/{typeSlug}/index.html")) (HtmlTemplates.typeFilesPage typ)

    // bundles/index.html + bundles/<source>/<path>/index.html
    if opts.Features.TagPages then
        writeFileR (Path.Combine(out, "bundles/index.html")) (HtmlTemplates.bundlesPage model.Bundles)
        for bundle in model.Bundles do
            writeFileR (Path.Combine(out, HtmlTemplates.bundleUrl bundle)) (HtmlTemplates.bundleFilesPage bundle)

    // files/<source>/<slug>.html
    if opts.Features.FilePages then
        for doc in model.Documents do
            match doc.PageUrl, doc.SyncStatus with
            | Some _, (Pulled | Cached) ->
                let contentOpt =
                    match doc.SyncStatus with
                    | Pulled | Cached ->
                        // find the IndexEntry to get the cacheRelPath
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
                    | _ -> None
                match contentOpt with
                | Some content ->
                    let htmlContent = MarkdownRenderer.render content
                    let sourceSlug = Uri.EscapeDataString doc.Source
                    let fileSlug = doc.RemotePath.Replace('/', '_').Replace('\\', '_').Replace(' ', '-')
                    let filePath = Path.Combine(out, $"files/{sourceSlug}/{fileSlug}.html")
                    let related = relatedFor doc
                    writeFileR filePath (HtmlTemplates.filePage doc htmlContent related)
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
