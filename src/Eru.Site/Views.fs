module Eru.Site.Views

open System
open System.Text.Json
open System.Web
open Eru

// HTML primitives and the registered list views (cards, list, table). Every view marks
// each document with `data-doc` plus one `data-<facet>` attribute per facet (a JSON array
// of values), which is all js/app.js needs to filter and search; the script knows nothing
// about how a view lays documents out.

let escapeHtml (s: string) = HttpUtility.HtmlEncode s

let prefixFor (depth: int) = String.replicate depth "../"

/// Tag chips; plain text when the Tags page (and so every tag page) is dropped.
let tagList (tags: string list) (prefix: string) (linked: bool) =
    tags
    |> List.map (fun t ->
        if linked then
            let slug = Uri.EscapeDataString t
            $"""<a class="tag" href="{prefix}tags/{slug}/index.html">#{escapeHtml t}</a>"""
        else $"""<span class="tag">#{escapeHtml t}</span>""")
    |> String.concat " "

type CliLabel = ShowLabel | HideLabel

let cliTip (label: string) (cmd: string) (visibility: CliLabel) =
    let labelHtml =
        match visibility with
        | ShowLabel -> $"""<span class="cli-tip-label">{escapeHtml label}</span>"""
        | HideLabel -> ""
    $"""<span class="cli-tip">{labelHtml}<code>{escapeHtml cmd}</code><button class="copy-btn" data-copy="{escapeHtml cmd}" aria-label="Copy command"></button></span>"""

let cliTips (items: (string * string) list) =
    let inner = items |> List.map (fun (l, c) -> cliTip l c ShowLabel) |> String.concat "\n"
    $"""<div class="cli-tips">{inner}</div>"""

let breadcrumbs (items: (string * string option) list) =
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

let private bundleSegments (b: SiteBundle) : string list =
    b.Source :: (if b.Path = "" then [] else b.Path.Split('/') |> Array.toList)
    |> List.map Uri.EscapeDataString

/// Number of path segments between a bundle page and the site root.
let bundleDepth (b: SiteBundle) : int = 1 + (bundleSegments b).Length

/// Site-root-relative URL of a bundle's page: bundles/<source>/<path>/index.html.
let bundleUrl (b: SiteBundle) : string =
    "bundles/" + String.Join("/", bundleSegments b) + "/index.html"

/// Browser URL of a file in its source repository, for hosts with the usual blob layout.
let sourceFileUrl (repoUrl: string) (branch: string option) (path: string) : string option =
    let u = repoUrl.Trim()
    let https =
        if u.StartsWith("git@", StringComparison.OrdinalIgnoreCase) then
            let rest = u.Substring 4
            match rest.IndexOf ':' with
            | -1 -> None
            | i -> Some $"https://{rest.Substring(0, i)}/{rest.Substring(i + 1)}"
        elif u.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) then
            match Uri.TryCreate(u, UriKind.Absolute) with
            | true, uri -> Some $"https://{uri.Host}{uri.AbsolutePath}"
            | _ -> None
        elif u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) then
            Some u
        else None
    https
    |> Option.bind (fun b ->
        let b = (b.TrimEnd '/')
        let b = if b.EndsWith ".git" then b.Substring(0, b.Length - 4) else b
        match Uri.TryCreate(b, UriKind.Absolute) with
        | true, uri ->
            let marker =
                if uri.Host.Contains "gitlab" then "/-/blob/"
                elif uri.Host.Contains "bitbucket" then "/src/"
                else "/blob/"
            let escaped = path.Split('/') |> Array.map Uri.EscapeDataString |> String.concat "/"
            Some (b + marker + Uri.EscapeDataString(defaultArg branch "HEAD") + "/" + escaped)
        | _ -> None)

type ViewContext = {
    Prefix    : string
    TagsLive  : bool
    Open      : OpenTarget
    SourceUrl : SiteDocument -> string option
}

type ViewSpec = {
    View         : string
    Columns      : int
    TableColumns : string list
    ListShow     : string list
}

module ViewSpec =
    let defaults : ViewSpec =
        { View = SiteVocabulary.defaultView
          Columns = SiteVocabulary.defaultCardsColumns
          TableColumns = SiteVocabulary.defaultTableColumns
          ListShow = SiteVocabulary.defaultListShow }

let private jsonArray (xs: string list) =
    escapeHtml ("[" + String.Join(",", xs |> List.map (fun x -> JsonSerializer.Serialize x)) + "]")

/// The filter contract: marks an element as a filterable document.
let docAttrs (d: SiteDocument) : string =
    let one (v: string option) = jsonArray (Option.toList v)
    $"""data-doc data-id="{escapeHtml d.Id}" data-path="{escapeHtml d.RemotePath}" data-source="{jsonArray [ d.Source ]}" data-ext="{jsonArray [ d.Extension ]}" data-tags="{jsonArray d.Tags}" data-type="{one d.Type}" data-bundle="{one d.Bundle}" data-status="{one d.Status}" """.TrimEnd()

let private docHref (vc: ViewContext) (d: SiteDocument) : (string * bool) option =
    let page = d.PageUrl |> Option.map (fun u -> vc.Prefix + u, false)
    match vc.Open with
    | OpenPage -> page
    | OpenResource -> d.Resource |> Option.map (fun r -> r, true) |> Option.orElse page
    | OpenSource -> vc.SourceUrl d |> Option.map (fun r -> r, true) |> Option.orElse page

/// The document's title as a link per the `open` target, or plain text when it has none.
let titleLink (vc: ViewContext) (d: SiteDocument) : string =
    match docHref vc d with
    | Some (href, true) -> $"""<a href="{escapeHtml href}" target="_blank" rel="noopener">{escapeHtml d.Title}</a>"""
    | Some (href, false) -> $"""<a href="{href}">{escapeHtml d.Title}</a>"""
    | None -> escapeHtml d.Title

let lifecycleBadge (status: string) =
    $"""<span class="badge badge-lifecycle badge-lifecycle-{escapeHtml status}">{escapeHtml status}</span>"""

let trustBadge (verified: Eru.Frontmatter.ActorAt list) =
    let tier = DocFacts.trustTier verified
    $"""<span class="badge badge-trust badge-{tier}">{tier}</span>"""

let private columnLabel =
    function
    | "staleAfter" -> "Stale after"
    | "syncStatus" -> "Sync"
    | "title" -> "Title"
    | c -> string (Char.ToUpperInvariant c.[0]) + c.Substring 1

/// Inline HTML for one field of a document, empty when the document has no value.
let private fieldHtml (vc: ViewContext) (field: string) (d: SiteDocument) : string =
    match field with
    | "source" -> $"""<span class="badge badge-source">{escapeHtml d.Source}</span>"""
    | "path" -> $"<code>{escapeHtml d.RemotePath}</code>"
    | "bundle" -> d.Bundle |> Option.map escapeHtml |> Option.defaultValue ""
    | "type" -> d.Type |> Option.map (fun t -> $"""<span class="badge badge-type">{escapeHtml t}</span>""") |> Option.defaultValue ""
    | "tags" -> tagList d.Tags vc.Prefix vc.TagsLive
    | "status" -> d.Status |> Option.map lifecycleBadge |> Option.defaultValue ""
    | "verified" -> trustBadge d.Verified
    | "staleAfter" ->
        d.StaleAfter
        |> Option.map (fun s ->
            let cls = if DocFacts.isStale DateTimeOffset.UtcNow (Some s) then "stale-date is-stale" else "stale-date"
            $"""<time class="{cls}" datetime="{s:o}">{DocFacts.formatDate s}</time>""")
        |> Option.defaultValue ""
    | "generated" ->
        d.Generated
        |> Option.map (fun g ->
            let at = g.At |> Option.map (fun a -> $" ({DocFacts.formatDate a})") |> Option.defaultValue ""
            escapeHtml g.By + at)
        |> Option.defaultValue ""
    | "syncStatus" -> $"""<span class="badge badge-status badge-{DocFacts.statusLabel d.SyncStatus}">{DocFacts.statusLabel d.SyncStatus}</span>"""
    | "ext" -> escapeHtml d.Extension
    | _ -> ""

// ── views ────────────────────────────────────────────────────────────────────

let private cardsView (vc: ViewContext) (spec: ViewSpec) (docs: SiteDocument list) : string =
    let card (d: SiteDocument) =
        let snippet =
            match d.Description with
            | Some desc -> escapeHtml desc
            | None ->
                match d.Body with
                | Some b -> escapeHtml (if b.Length > 120 then b.[..119] + "…" else b)
                | None -> ""
        let bodyHtml = if snippet <> "" then $"<p class=\"card-body\">{snippet}</p>" else ""
        let tagsBlock = if not d.Tags.IsEmpty then $"<div class=\"card-tags\">{tagList d.Tags vc.Prefix vc.TagsLive}</div>" else ""
        let tier = DocFacts.trustTier d.Verified
        $"""<article class="file-card" {docAttrs d}>
  <div class="card-header">
    <span class="card-title">{titleLink vc d}</span>
    <span class="badge badge-source">{escapeHtml d.Source}</span>
    <span class="badge badge-status badge-{DocFacts.statusLabel d.SyncStatus}">{DocFacts.statusLabel d.SyncStatus}</span>
    <span class="badge badge-trust badge-{tier}">{tier}</span>
  </div>
  {bodyHtml}
  {tagsBlock}
</article>"""
    $"""<div class="card-grid" style="--cols: {spec.Columns}">
{docs |> List.map card |> String.concat "\n"}
</div>"""

let private listView (vc: ViewContext) (spec: ViewSpec) (docs: SiteDocument list) : string =
    let item (d: SiteDocument) =
        let metaHtml =
            spec.ListShow
            |> List.map (fun f -> fieldHtml vc f d)
            |> List.filter (fun h -> h <> "")
            |> String.concat " "
        let metaBlock = if metaHtml = "" then "" else $"""<span class="doc-list-meta">{metaHtml}</span>"""
        $"""<li class="doc-list-item" {docAttrs d}><span class="doc-list-title">{titleLink vc d}</span>{metaBlock}</li>"""
    $"""<ul class="doc-list">
{docs |> List.map item |> String.concat "\n"}
</ul>"""

let private tableView (vc: ViewContext) (spec: ViewSpec) (docs: SiteDocument list) : string =
    let head =
        spec.TableColumns
        |> List.map (fun c -> $"""<th scope="col" data-col="{c}" data-sortable>{columnLabel c}</th>""")
        |> String.concat ""
    let row (d: SiteDocument) =
        let cells =
            spec.TableColumns
            |> List.map (fun c ->
                let inner = if c = "title" then titleLink vc d else fieldHtml vc c d
                $"""<td data-col="{c}">{inner}</td>""")
            |> String.concat ""
        $"""<tr {docAttrs d}>{cells}</tr>"""
    $"""<table class="doc-table">
<thead><tr>{head}</tr></thead>
<tbody>
{docs |> List.map row |> String.concat "\n"}
</tbody>
</table>"""

/// View name -> renderer. Later views (graph, timeline) register here without any config
/// reshaping.
let renderers : Map<string, ViewContext -> ViewSpec -> SiteDocument list -> string> =
    Map.ofList [ "cards", cardsView; "list", listView; "table", tableView ]

let renderDocs (vc: ViewContext) (spec: ViewSpec) (docs: SiteDocument list) : string =
    let render = renderers |> Map.tryFind spec.View |> Option.defaultValue cardsView
    render vc spec docs

/// Headed sections, each with its own grid/list/table. `data-doc-group` lets the filter
/// script hide a section with no visible documents.
let renderGroups (vc: ViewContext) (spec: ViewSpec) (groups: (string * SiteDocument list) list) : string =
    groups
    |> List.map (fun (name, docs) ->
        $"""<section class="doc-group" data-doc-group>
<h2 class="doc-group-title">{escapeHtml name}</h2>
{renderDocs vc spec docs}
</section>""")
    |> String.concat "\n"
