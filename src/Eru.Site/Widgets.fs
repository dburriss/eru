module Eru.Site.Widgets

open System
open Eru
open Eru.Site.Views
open Eru.Site.PageLayout

// Each widget is a small render function over a WidgetContext, registered by name. The
// names and the slots they may appear in are declared in Eru.SiteVocabulary.widgets so
// config validation and rendering share one list.

type FacetMode =
    | FacetLinks      // plain links to pages (every page)
    | FacetFilter     // links the script upgrades to checkboxes (Browse)

type PageData = {
    Title       : string
    TitleHtml   : string option          // overrides the escaped Title in the heading
    Breadcrumbs : (string * string option) list
    Doc         : SiteDocument option
    ContentHtml : string option
    ListHtml    : string option          // already rendered by the page's view
    DocCount    : int option             // documents in the list; shown next to the title
    Searchable  : bool
    Related     : RelatedLinks option
    CliTips     : (string * string) list
    FacetMode   : FacetMode
}

module PageData =
    let empty : PageData =
        { Title = ""; TitleHtml = None; Breadcrumbs = []; Doc = None; ContentHtml = None; ListHtml = None
          DocCount = None; Searchable = false; Related = None; CliTips = []; FacetMode = FacetLinks }

/// Site-wide facts widgets need to link to other pages.
type SiteEnv = {
    Model     : SiteModel
    PageLinks : Map<string, string * string>   // page id -> (label, site-root-relative url)
}

type WidgetContext = {
    Env    : SiteEnv
    Prefix : string
    Slots  : ResolvedSlots
    Page   : PageData
}

let private pageHref (ctx: WidgetContext) (id: string) (fallback: string) =
    match ctx.Env.PageLinks.TryFind id with
    | Some _ -> fallback
    | None -> "#"

let private timeTag (d: DateTimeOffset) =
    $"""<time datetime="{d:o}">{DocFacts.formatDate d}</time>"""

// ── related / graph ──────────────────────────────────────────────────────────

let private relatedLink (p: string) (r: RelatedDoc) =
    match r.IsExternal, r.PageUrl with
    | true, _ -> $"""<li><a href="{escapeHtml r.Id}" target="_blank" rel="noopener">{escapeHtml r.Title}</a></li>"""
    | false, Some url -> $"""<li><a href="{p}{url}">{escapeHtml r.Title}</a></li>"""
    | false, None -> $"""<li class="related-unresolved">{escapeHtml r.Title}</li>"""

let private relatedGroups (p: string) (related: RelatedLinks) (groupClass: string) : string =
    let group label (docs: RelatedDoc list) =
        if docs.IsEmpty then ""
        else
            let items = docs |> List.map (relatedLink p) |> String.concat "\n"
            $"""<div class="{groupClass}"><span class="related-label">{label}</span><ul>{items}</ul></div>"""
    group "Links to this document" related.Incoming + group "Links from this document" related.Outgoing

let private hasLinks (r: RelatedLinks option) =
    match r with
    | Some r -> not (r.Incoming.IsEmpty && r.Outgoing.IsEmpty)
    | None -> false

let private relatedWidget (ctx: WidgetContext) =
    match ctx.Page.Related with
    | Some r when hasLinks ctx.Page.Related ->
        $"""<section class="doc-related">
  <h3>Linked documents</h3>
  <div class="related-lists">{relatedGroups ctx.Prefix r "related-group"}</div>
</section>"""
    | _ -> ""

let private graphWidget (ctx: WidgetContext) =
    match ctx.Page.Related, ctx.Page.Doc with
    | Some _, Some doc when hasLinks ctx.Page.Related ->
        // Hidden until graph.js has drawn the graph, so without JS (or without graph.json) the page
        // shows only the `related` lists and nothing is duplicated.
        $"""<section class="doc-graph" hidden>
  <h3>Linked documents</h3>
  <div id="doc-graph" data-node-id="{escapeHtml doc.Id}"></div>
  <div class="doc-graph-legend">
    <span class="legend-item legend-focus">This document</span>
    <span class="legend-item legend-incoming">Links to this</span>
    <span class="legend-item legend-outgoing">Linked from this</span>
  </div>
</section>"""
    | _ -> ""

// ── facets ───────────────────────────────────────────────────────────────────

let private facetsWidget (ctx: WidgetContext) =
    let p = ctx.Prefix
    let model = ctx.Env.Model
    let filter = ctx.Page.FacetMode = FacetFilter
    let section (title: string) (facet: string) (items: string list) =
        if items.IsEmpty then ""
        else
            $"""<section class="sidebar-section">
  <h3>{title}</h3>
  <ul data-facet="{facet}">{String.concat "\n" items}</ul>
</section>"""
    let item (value: string) (href: string) (count: int option) =
        let c = count |> Option.map (fun n -> $""" <span class="count">({n})</span>""") |> Option.defaultValue ""
        $"""<li><a href="{href}" data-value="{escapeHtml value}">{escapeHtml value}</a>{c}</li>"""
    let sources =
        model.Sources |> List.map (fun s ->
            item s.Name (pageHref ctx "sources" $"{p}sources/{Uri.EscapeDataString s.Name}/index.html") (Some s.FileCount))
    let exts =
        if filter then model.AllExtensions |> List.map (fun e -> item e "#" None) else []
    let tags =
        model.Tags |> List.map (fun t ->
            item t.Name (pageHref ctx "tags" $"{p}tags/{Uri.EscapeDataString t.Name}/index.html") (Some t.FileCount))
    let types =
        model.Types |> List.map (fun t ->
            item t.Name (pageHref ctx "types" $"{p}types/{Uri.EscapeDataString t.Name}/index.html") (Some t.FileCount))
    let bundles =
        model.Bundles |> List.map (fun b -> item b.Name (pageHref ctx "bundles" (p + bundleUrl b)) (Some b.FileCount))
    let inner =
        [ section "Sources" "source" sources
          section "File Extensions" "ext" exts
          section "Tags" "tags" tags
          section "Types" "type" types
          section "Bundles" "bundle" bundles ]
        |> List.filter (fun s -> s <> "")
        |> String.concat "\n"
    if inner = "" then ""
    else
        let mode = if filter then "filter" else "links"
        $"""<div class="facets" data-facet-mode="{mode}">{inner}</div>"""

// ── toc ──────────────────────────────────────────────────────────────────────

let private tocWidget (ctx: WidgetContext) =
    match ctx.Page.ContentHtml with
    | None -> ""
    | Some html ->
        let heads = MarkdownRenderer.headings html |> List.filter (fun (level, _, _) -> level >= 2 && level <= 4)
        if heads.IsEmpty then ""
        else
            let items =
                heads
                |> List.map (fun (level, id, text) ->
                    $"""<li class="toc-l{level}"><a href="#{escapeHtml id}">{escapeHtml text}</a></li>""")
                |> String.concat "\n"
            $"""<nav class="toc" aria-label="Table of contents">
  <h3>On this page</h3>
  <ul>{items}</ul>
</nav>"""

// ── document widgets ─────────────────────────────────────────────────────────

let private withDoc (f: SiteDocument -> string) (ctx: WidgetContext) =
    match ctx.Page.Doc with
    | Some d -> f d
    | None -> ""

let private actorKind (by: string) = if DocFacts.isHumanActor by then "human" else "machine"

let private metaWidget =
    withDoc (fun d ->
        let typeHtml = d.Type |> Option.map (fun t -> $"""<span class="badge badge-type">{escapeHtml t}</span>""") |> Option.defaultValue ""
        let statusHtml = d.Status |> Option.map lifecycleBadge |> Option.defaultValue ""
        if typeHtml = "" && statusHtml = "" then "" else $"""<div class="doc-meta-badges">{typeHtml}{statusHtml}</div>""")

let private verifiedWidget =
    withDoc (fun d ->
        let tier = DocFacts.trustTier d.Verified
        let badge = $"""<span class="badge badge-trust badge-{tier}">{tier}</span>"""
        let list =
            if d.Verified.IsEmpty then ""
            else
                let items =
                    d.Verified
                    |> List.map (fun v ->
                        let at = v.At |> Option.map (fun a -> " " + timeTag a) |> Option.defaultValue ""
                        $"""<li><span class="actor-kind actor-{actorKind v.By}">{actorKind v.By}</span> <span class="actor-by">{escapeHtml v.By}</span>{at}</li>""")
                    |> String.concat "\n"
                $"""<ul class="verifier-list">{items}</ul>"""
        $"""<div class="doc-verified">{badge}{list}</div>""")

let private generatedWidget =
    withDoc (fun d ->
        match d.Generated with
        | None -> ""
        | Some g ->
            let at = g.At |> Option.map (fun a -> " " + timeTag a) |> Option.defaultValue ""
            $"""<div class="doc-generated"><span class="related-label">Generated</span> <span class="actor-kind actor-{actorKind g.By}">{actorKind g.By}</span> <span class="actor-by">{escapeHtml g.By}</span>{at}</div>""")

let private descriptionWidget =
    withDoc (fun d ->
        match d.Description with
        | Some desc -> $"""<p class="doc-description">{escapeHtml desc}</p>"""
        | None -> "")

let private tagsWidget (ctx: WidgetContext) =
    withDoc (fun d -> if d.Tags.IsEmpty then "" else $"""<div class="doc-tags">{tagList d.Tags ctx.Prefix (ctx.Env.PageLinks.ContainsKey "tags")}</div>""") ctx

let private staleWidget =
    withDoc (fun d ->
        if DocFacts.isStale DateTimeOffset.UtcNow d.StaleAfter then
            """<p class="doc-stale-warning">Stale — this content is past its stale_after date.</p>"""
        else "")

// ── page widgets ─────────────────────────────────────────────────────────────

let private titleWidget (ctx: WidgetContext) =
    let title = ctx.Page.TitleHtml |> Option.defaultValue (escapeHtml ctx.Page.Title)
    let count =
        ctx.Page.DocCount
        |> Option.map (fun n -> $""" <span class="count" data-doc-count>({n})</span>""")
        |> Option.defaultValue ""
    $"<h1>{title}{count}</h1>"

let private breadcrumbsWidget (ctx: WidgetContext) =
    if ctx.Page.Breadcrumbs.IsEmpty then "" else breadcrumbs ctx.Page.Breadcrumbs

let private cliWidget (ctx: WidgetContext) =
    if ctx.Page.CliTips.IsEmpty then "" else cliTips ctx.Page.CliTips

let private contentWidget (ctx: WidgetContext) =
    match ctx.Page.ContentHtml with
    | Some html -> $"""<article class="markdown-body">{html}</article>"""
    | None -> ""

let private listWidget (ctx: WidgetContext) =
    match ctx.Page.ListHtml with
    | None -> ""
    | Some html ->
        let search =
            if ctx.Page.Searchable then
                """<div id="search-container" class="list-search" style="display:none">
  <input type="search" id="search-input" placeholder="Search…" aria-label="Search files" />
</div>
"""
            else ""
        $"""{search}<div class="doc-list-container" data-doc-list>{html}</div>"""

let private linksWidget (ctx: WidgetContext) =
    let links =
        ctx.Slots.Top
        |> List.choose (function LinkRef (label, url) -> Some (label, url) | _ -> None)
    if links.IsEmpty then ""
    else
        let items =
            links
            |> List.map (fun (label, url) ->
                $"""<li><a href="{escapeHtml url}" target="_blank" rel="noopener">{escapeHtml label}</a></li>""")
            |> String.concat "\n"
        $"""<nav class="side-links"><h3>Links</h3><ul>{items}</ul></nav>"""

/// Name -> renderer. Every name in SiteVocabulary.widgets has an entry.
let renderers : Map<string, WidgetContext -> string> =
    Map.ofList [
        "breadcrumbs", breadcrumbsWidget
        "title",       titleWidget
        "meta",        metaWidget
        "verified",    verifiedWidget
        "generated",   generatedWidget
        "description", descriptionWidget
        "tags",        tagsWidget
        "stale",       staleWidget
        "cli",         cliWidget
        "content",     contentWidget
        "list",        listWidget
        "toc",         tocWidget
        "related",     relatedWidget
        "graph",       graphWidget
        "facets",      facetsWidget
        "links",       linksWidget
    ]

let private wrap (name: string) (html: string) =
    if String.IsNullOrWhiteSpace html then "" else $"""<div class="widget widget-{name}">{html}</div>"""

let renderWidget (ctx: WidgetContext) (name: string) : string =
    match renderers.TryFind name with
    | Some r -> wrap name (r ctx)
    | None -> ""

let renderMain (ctx: WidgetContext) (names: string list) : string =
    names |> List.map (renderWidget ctx) |> List.filter (fun h -> h <> "") |> String.concat "\n"

/// Side slots hold widgets plus page links and custom links; runs of links share a list.
let renderSide (ctx: WidgetContext) (items: LayoutItem list) : string =
    let linkHtml =
        function
        | PageRef (id, label) ->
            match ctx.Env.PageLinks.TryFind id with
            | Some (defaultLabel, url) ->
                Some $"""<li><a href="{ctx.Prefix}{url}">{escapeHtml (defaultArg label defaultLabel)}</a></li>"""
            | None -> None
        | LinkRef (label, url) ->
            Some $"""<li><a href="{escapeHtml url}" target="_blank" rel="noopener">{escapeHtml label}</a></li>"""
        | WidgetRef _ -> None
    let flush (run: string list) =
        if run.IsEmpty then []
        else [ $"""<div class="widget widget-page-links"><ul class="side-links">{String.concat "" (List.rev run)}</ul></div>""" ]
    let chunks, run =
        items
        |> List.fold (fun (chunks: string list, run: string list) item ->
            match item with
            | WidgetRef w ->
                let html = renderWidget ctx w
                (chunks @ flush run @ (if html = "" then [] else [ html ])), []
            | other ->
                match linkHtml other with
                | Some l -> chunks, l :: run
                | None -> chunks, run) ([], [])
    chunks @ flush run |> String.concat "\n"
