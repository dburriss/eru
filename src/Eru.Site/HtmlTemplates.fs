module Eru.Site.HtmlTemplates

open System
open System.Web

let private prefixFor (depth: int) =
    String.replicate depth "../"

let private escapeHtml (s: string) = HttpUtility.HtmlEncode s

let private statusLabel =
    function
    | Pulled    -> "pulled"
    | Cached    -> "cached"
    | IndexOnly -> "index-only"

type private CliLabel = ShowLabel | HideLabel

let private cliTip (label: string) (cmd: string) (visibility: CliLabel) =
    let labelHtml =
        match visibility with
        | ShowLabel -> $"""<span class="cli-tip-label">{escapeHtml label}</span>"""
        | HideLabel -> ""
    $"""<span class="cli-tip">{labelHtml}<code>{escapeHtml cmd}</code><button class="copy-btn" data-copy="{escapeHtml cmd}" aria-label="Copy command"></button></span>"""

let private cliTips (items: (string * string) list) =
    let inner = items |> List.map (fun (l, c) -> cliTip l c ShowLabel) |> String.concat "\n"
    $"""<div class="cli-tips">{inner}</div>"""

let private breadcrumbs (items: (string * string option) list) =
    let parts =
        items
        |> List.mapi (fun i (label, href) ->
            let isLast = i = items.Length - 1
            let node =
                match href with
                | Some url when not isLast -> $"""<a href="{url}">{escapeHtml label}</a>"""
                | _ -> $"""<span class="bc-current">{escapeHtml label}</span>"""
            if i = 0 then node
            else $"""<span class="bc-sep">/</span>{node}""")
        |> String.concat ""
    $"""<nav class="breadcrumbs" aria-label="breadcrumb">{parts}</nav>"""

let private tagList (tags: string list) (prefix: string) =
    tags
    |> List.map (fun t ->
        let slug = Uri.EscapeDataString t
        $"""<a class="tag" href="{prefix}tags/{slug}/index.html">#{escapeHtml t}</a>""")
    |> String.concat " "

let private fileCard (prefix: string) (doc: SiteDocument) =
    let titleHtml =
        match doc.PageUrl with
        | Some url -> $"""<a href="{prefix}{url}">{escapeHtml doc.Title}</a>"""
        | None     -> escapeHtml doc.Title
    let snippet =
        match doc.Description with
        | Some d -> escapeHtml d
        | None   ->
            match doc.Body with
            | Some b -> escapeHtml (if b.Length > 120 then b.[..119] + "…" else b)
            | None   -> ""
    let tagsHtml = tagList doc.Tags prefix
    let tagsAttr = doc.Tags |> String.concat " " |> escapeHtml
    let bodyHtml = if snippet <> "" then $"<p class=\"card-body\">{snippet}</p>" else ""
    let tagsBlock = if not doc.Tags.IsEmpty then $"<div class=\"card-tags\">{tagsHtml}</div>" else ""
    $"""<article class="file-card" data-id="{escapeHtml doc.Id}" data-source="{escapeHtml doc.Source}" data-ext="{escapeHtml doc.Extension}" data-tags="{tagsAttr}">
  <div class="card-header">
    <span class="card-title">{titleHtml}</span>
    <span class="badge badge-source">{escapeHtml doc.Source}</span>
    <span class="badge badge-status badge-{statusLabel doc.Status}">{statusLabel doc.Status}</span>
  </div>
  {bodyHtml}
  {tagsBlock}
</article>"""

let layout (depth: int) (title: string) (body: string) : string =
    let p = prefixFor depth
    $"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{escapeHtml title} — eru</title>
  <link rel="stylesheet" href="{p}assets/css/style.css" />
  <link rel="stylesheet" href="{p}assets/css/custom.css" />
</head>
<body>
  <nav class="site-nav">
    <a class="nav-brand" href="{p}index.html">eru</a>
    <a href="{p}index.html">Browse</a>
    <a href="{p}sources/index.html">Sources</a>
    <a href="{p}tags/index.html">Tags</a>
    <button id="theme-toggle" aria-label="Toggle theme" style="display:none"></button>
  </nav>
  <noscript><p class="noscript-note">Search requires JavaScript. Browse by source or tag using the navigation links.</p></noscript>
  <main>{body}</main>
  <script defer src="{p}js/theme.js"></script>
  <script>window.ERU_DATA_ROOT = "{p}data/";</script>
  <script defer src="{p}js/app.js"></script>
</body>
</html>"""

let indexPage (model: SiteModel) : string =
    let p = prefixFor 0
    let sourceLinks =
        model.Sources
        |> List.map (fun s ->
            $"""<li><a href="{p}sources/{Uri.EscapeDataString s.Name}/index.html">{escapeHtml s.Name}</a> <span class="count">({s.FileCount})</span></li>""")
        |> String.concat "\n"
    let extLinks =
        model.AllExtensions
        |> List.map (fun e ->
            $"""<li><a href="#" data-filter-ext="{escapeHtml e}">{escapeHtml e}</a></li>""")
        |> String.concat "\n"
    let tagLinks =
        model.Tags
        |> List.map (fun t ->
            $"""<li><a href="{p}tags/{Uri.EscapeDataString t.Name}/index.html">{escapeHtml t.Name}</a> <span class="count">({t.FileCount})</span></li>""")
        |> String.concat "\n"
    let cards =
        model.Documents
        |> List.map (fileCard p)
        |> String.concat "\n"
    let body = $"""<div class="page-layout">
  <aside class="sidebar">
    <section class="sidebar-section">
      <h3>Sources</h3>
      <ul id="source-filters">{sourceLinks}</ul>
    </section>
    <section class="sidebar-section">
      <h3>Types</h3>
      <ul id="ext-filters">{extLinks}</ul>
    </section>
    <section class="sidebar-section">
      <h3>Tags</h3>
      <ul id="tag-filters">{tagLinks}</ul>
    </section>
  </aside>
  <section class="content">
    <div class="content-header">
      <h1>All files <span class="count" id="file-count">({model.Documents.Length})</span></h1>
      <div id="search-container" style="display:none">
        <input type="search" id="search-input" placeholder="Search…" aria-label="Search files" />
      </div>
    </div>
    <div id="file-list">{cards}</div>
  </section>
</div>"""
    layout 0 "Browse" body

let sourcesPage (sources: SiteSource list) : string =
    let sourceCard (s: SiteSource) =
        let manifestBadge =
            if s.HasManifest then """<span class="badge badge-manifest">manifest</span>"""
            else """<span class="badge badge-no-manifest">no manifest</span>"""
        let descHtml =
            match s.Description with
            | Some d -> $"""<p class="source-card-desc">{escapeHtml d}</p>"""
            | None   -> ""
        let urlHtml =
            match s.Url with
            | Some u -> $"""<div class="source-card-url"><a href="{escapeHtml u}" target="_blank" rel="noopener">{escapeHtml u}</a></div>"""
            | None -> ""
        let addRef = s.Url |> Option.defaultValue s.Name
        let tip = cliTip "add source" $"eru source add {addRef}" HideLabel
        $"""<article class="source-card">
  <div class="source-card-header">
    <a class="source-card-name" href="{Uri.EscapeDataString s.Name}/index.html">{escapeHtml s.Name}</a>
    {manifestBadge}
  </div>
  {descHtml}
  {urlHtml}
  <div class="source-card-meta">
    <span class="count">{s.FileCount} files</span>
  </div>
  <div class="source-card-tip">{tip}</div>
</article>"""
    let cards = sources |> List.map sourceCard |> String.concat "\n"
    let body = $"""<h1>Sources</h1>
<div class="source-grid">{cards}</div>"""
    layout 1 "Sources" body

let sourceFilesPage (source: SiteSource) : string =
    let manifestBadge =
        if source.HasManifest then """<span class="badge badge-manifest">manifest</span>"""
        else """<span class="badge badge-no-manifest">no manifest</span>"""
    let p = prefixFor 2
    let cards =
        source.Files
        |> List.map (fileCard p)
        |> String.concat "\n"
    let addRef = source.Url |> Option.defaultValue source.Name
    let sourceTip = cliTips ["add source", $"eru source add {escapeHtml addRef}"]
    let crumbs = breadcrumbs ["Sources", Some "../index.html"; source.Name, None]
    let body = $"""<div class="page-header">
  {crumbs}
  <h1>{escapeHtml source.Name} {manifestBadge}</h1>
</div>
{sourceTip}
<div id="file-list">{cards}</div>"""
    layout 2 source.Name body

let tagsPage (tags: SiteTag list) : string =
    let rows =
        tags
        |> List.map (fun t ->
            $"""<li><a href="{Uri.EscapeDataString t.Name}/index.html">#{escapeHtml t.Name}</a> <span class="count">({t.FileCount} files)</span></li>""")
        |> String.concat "\n"
    let body = $"""<h1>Tags</h1>
<ul class="tag-list">{rows}</ul>"""
    layout 1 "Tags" body

let tagFilesPage (tag: SiteTag) : string =
    let p = prefixFor 2
    let cards =
        tag.Files
        |> List.map (fileCard p)
        |> String.concat "\n"
    let tagTip = cliTips ["pull all", $"eru add --tag {escapeHtml tag.Name}"; "search", $"eru search --tag {escapeHtml tag.Name}"]
    let crumbs = breadcrumbs ["Tags", Some "../index.html"; $"#{tag.Name}", None]
    let body = $"""<div class="page-header">
  {crumbs}
  <h1>#{escapeHtml tag.Name}</h1>
</div>
{tagTip}
<div id="file-list">{cards}</div>"""
    layout 2 tag.Name body

let filePage (doc: SiteDocument) (contentHtml: string) : string =
    let p = prefixFor 2
    let fileName = System.IO.Path.GetFileName doc.RemotePath
    let fileTip = cliTips ["pull this file", $"eru add {escapeHtml doc.Source}:{escapeHtml doc.RemotePath}"]
    let crumbs = breadcrumbs [
        "Sources", Some $"{p}sources/index.html"
        doc.Source, Some $"{p}sources/{Uri.EscapeDataString doc.Source}/index.html"
        fileName, None
    ]
    let descHtml =
        match doc.Description with
        | Some d -> $"""<p class="doc-description">{escapeHtml d}</p>"""
        | None -> ""
    let tagsHtml =
        if doc.Tags.IsEmpty then ""
        else $"""<div class="doc-tags">{tagList doc.Tags p}</div>"""
    let metaBox =
        if descHtml = "" && tagsHtml = "" then ""
        else $"""<div class="doc-meta">{descHtml}{tagsHtml}</div>"""
    let body = $"""<div class="page-header">
  {crumbs}
  <h1>{escapeHtml fileName}</h1>
</div>
{metaBox}
{fileTip}
<article class="markdown-body">{contentHtml}</article>"""
    layout 2 fileName body
