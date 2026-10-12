module Eru.Site.HtmlTemplates

open System
open Eru
open Eru.Site.Views
open Eru.Site.PageLayout
open Eru.Site.Widgets

let prefixFor = Views.prefixFor

let bundleUrl = Views.bundleUrl

// ── page frame ───────────────────────────────────────────────────────────────

let private navLinks (env: SiteEnv) (p: string) (items: LayoutItem list) : string =
    items
    |> List.choose (function
        | PageRef (id, label) ->
            env.PageLinks.TryFind id
            |> Option.map (fun (defaultLabel, url) -> $"""<a href="{p}{url}">{escapeHtml (defaultArg label defaultLabel)}</a>""")
        | LinkRef (label, url) ->
            Some $"""<a href="{escapeHtml url}" target="_blank" rel="noopener">{escapeHtml label}</a>"""
        | WidgetRef _ -> None)
    |> String.concat "\n    "

/// The shared frame: top nav plus a CSS grid with `left`, `main` and `right` regions. An
/// empty region is left out of the grid so it leaves no gap.
let private frame (depth: int) (title: string) (nav: string) (left: string) (main: string) (right: string) : string =
    let p = prefixFor depth
    let present (html: string) = not (String.IsNullOrWhiteSpace html)
    let classes =
        [ if present left then "has-left"
          if present right then "has-right" ]
        |> String.concat " "
    let leftHtml = if present left then $"""<aside class="region region-left sidebar">{left}</aside>""" else ""
    let rightHtml = if present right then $"""<aside class="region region-right">{right}</aside>""" else ""
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
    {nav}
    <button id="theme-toggle" aria-label="Toggle theme" style="display:none"></button>
  </nav>
  <noscript><p class="noscript-note">Search requires JavaScript. Browse by source or tag using the navigation links.</p></noscript>
  <main>
    <div class="page-grid {classes}">
      {leftHtml}
      <div class="region region-main">{main}</div>
      {rightHtml}
    </div>
  </main>
  <script defer src="{p}js/theme.js"></script>
  <script>window.ERU_DATA_ROOT = "{p}data/";</script>
  <script defer src="{p}js/app.js"></script>
  <script defer src="{p}js/graph.js"></script>
</body>
</html>"""

/// Renders one page: resolved slots of widgets around the page's data.
let render (env: SiteEnv) (depth: int) (slots: ResolvedSlots) (page: PageData) : string =
    let p = prefixFor depth
    let ctx = { Env = env; Prefix = p; Slots = slots; Page = page }
    frame depth page.Title (navLinks env p slots.Top) (renderSide ctx slots.Left) (renderMain ctx slots.Main) (renderSide ctx slots.Right)

// ── environment ──────────────────────────────────────────────────────────────

let emptyModel : SiteModel =
    { Documents = []; Sources = []; Tags = []; Types = []; Bundles = []; AllExtensions = [] }

/// Page links for the built-in pages with no configuration.
let defaultEnv (model: SiteModel) : SiteEnv =
    { Model = model
      PageLinks = PageSpecs.resolve SiteSettings.empty |> PageSpecs.pageLinks }

let private indexUrl (env: SiteEnv) (p: string) (id: string) : string option =
    env.PageLinks.TryFind id |> Option.map (fun (_, url) -> p + url)

// ── built-in list bodies ─────────────────────────────────────────────────────

let sourcesListHtml (p: string) (sources: SiteSource list) : string =
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
    <a class="source-card-name" href="{p}sources/{Uri.EscapeDataString s.Name}/index.html">{escapeHtml s.Name}</a>
    {manifestBadge}
  </div>
  {descHtml}
  {urlHtml}
  <div class="source-card-meta">
    <span class="count">{s.FileCount} files</span>
  </div>
  <div class="source-card-tip">{tip}</div>
</article>"""
    $"""<div class="source-grid">{sources |> List.map sourceCard |> String.concat "\n"}</div>"""

let tagsListHtml (p: string) (tags: SiteTag list) : string =
    let rows =
        tags
        |> List.map (fun t ->
            $"""<li><a href="{p}tags/{Uri.EscapeDataString t.Name}/index.html">#{escapeHtml t.Name}</a> <span class="count">({t.FileCount} files)</span></li>""")
        |> String.concat "\n"
    $"""<ul class="tag-list">{rows}</ul>"""

let typesListHtml (p: string) (types: SiteType list) : string =
    let rows =
        types
        |> List.map (fun t ->
            $"""<li><a href="{p}types/{Uri.EscapeDataString t.Name}/index.html">{escapeHtml t.Name}</a> <span class="count">({t.FileCount} files)</span></li>""")
        |> String.concat "\n"
    $"""<ul class="tag-list">{rows}</ul>"""

let bundlesListHtml (p: string) (bundles: SiteBundle list) : string =
    let rows =
        bundles
        |> List.map (fun b ->
            $"""<li><a href="{p}{bundleUrl b}">{escapeHtml b.Name}</a> <span class="badge">{b.Kind}</span> <span class="count">({b.FileCount} files)</span></li>""")
        |> String.concat "\n"
    $"""<ul class="tag-list">{rows}</ul>"""

// ── page data ────────────────────────────────────────────────────────────────

/// Detail page (tags/<tag>, sources/<name>, ...): breadcrumbs, a heading and a document list.
let detailPageData
    (env: SiteEnv) (depth: int) (parentId: string) (parentLabel: string) (title: string)
    (titleHtml: string option) (cli: (string * string) list) (listHtml: string) (count: int) : PageData =
    let p = prefixFor depth
    let parentHref = indexUrl env p parentId
    { PageData.empty with
        Title = title
        TitleHtml = titleHtml
        Breadcrumbs = [ parentLabel, parentHref; title, None ]
        ListHtml = Some listHtml
        DocCount = Some count
        CliTips = cli }

/// A document page's data: breadcrumbs, heading, rendered content and link lists.
let documentPageData (env: SiteEnv) (doc: SiteDocument) (contentHtml: string) (related: RelatedLinks) : PageData =
    let p = prefixFor 2
    let fileName = System.IO.Path.GetFileName doc.RemotePath
    let sourcesHref = indexUrl env p "sources"
    let sourceHref = sourcesHref |> Option.map (fun _ -> $"{p}sources/{Uri.EscapeDataString doc.Source}/index.html")
    { PageData.empty with
        Title = fileName
        Breadcrumbs =
            [ "Sources", sourcesHref
              doc.Source, sourceHref
              fileName, None ]
        Doc = Some doc
        ContentHtml = Some contentHtml
        Related = Some related
        CliTips = [ "pull this file", $"eru add {doc.Source}:{doc.RemotePath}" ] }

/// A document page with the default layout and no site model; what a site with no
/// configuration renders (minus the facets, which need the model).
let filePage (doc: SiteDocument) (contentHtml: string) (related: RelatedLinks) : string =
    let env = defaultEnv emptyModel
    let slots = PageLayout.forDocument SiteSettings.empty DateTimeOffset.UtcNow doc
    render env 2 slots (documentPageData env doc contentHtml related)
