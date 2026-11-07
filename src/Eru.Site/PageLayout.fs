module Eru.Site.PageLayout

open System
open Eru

// Pure resolution of what a page shows: its seven slots and its list view. Precedence is
// built-in defaults for the page kind, then `site.layout`, then the page's own `layout`
// (document pages: `site.document.layout`, then matching `site.document.rules` in order).
// A slot set at a later level replaces the earlier one as a whole.

type ResolvedSlots = {
    Nav     : LayoutItem list
    Top     : string list
    Toolbar : string list
    Left    : LayoutItem list
    Right   : LayoutItem list
    Main    : string list
    Footer  : LayoutItem list
}

module ResolvedSlots =
    let empty : ResolvedSlots =
        { Nav = []; Top = []; Toolbar = []; Left = []; Right = []; Main = []; Footer = [] }

    /// True when any slot places the query line, so the list should not add its plain search box.
    let hasQuery (s: ResolvedSlots) : bool =
        let widget (items: LayoutItem list) = items |> List.exists (function WidgetRef "query" -> true | _ -> false)
        let named (names: string list) = List.contains "query" names
        named s.Top || named s.Toolbar || named s.Main || widget s.Left || widget s.Right || widget s.Footer

/// Where the footer points by default, on every page.
let docsLink = LinkRef ("docs", "https://github.com/dburriss/eru/tree/main/docs")
let changelogLink = LinkRef ("changelog", "https://github.com/dburriss/eru/blob/main/CHANGELOG.md")

type PageKind =
    | BrowsePage         // the browse page: query line and table, no sidebars
    | ListPage           // tags, custom filter pages
    | DetailPage         // tags/<tag>, sources/<name>, ...
    | ContentPage
    | ContentListPage    // content and filter on one page
    | DocumentPage

let defaultMain (kind: PageKind) : string list =
    match kind with
    | BrowsePage -> [ "list" ]
    | ListPage -> [ "title"; "list" ]
    | DetailPage -> [ "breadcrumbs"; "title"; "cli"; "list" ]
    | ContentPage -> [ "title"; "content" ]
    | ContentListPage -> [ "title"; "content"; "list" ]
    | DocumentPage -> SiteVocabulary.defaultDocumentMain

let baseLayout (settings: SiteSettings) (kind: PageKind) : Layout =
    let widgets = List.map WidgetRef
    { Nav = Some (Items (SiteSettings.knownPageIds settings |> List.map (fun id -> PageRef (id, None))))
      Top = Some (Items (if kind = BrowsePage then widgets [ "title" ] else []))
      Toolbar = Some (Items (if kind = BrowsePage then widgets [ "query" ] else []))
      Left = if kind = BrowsePage then Some Disabled else Some (Items (widgets [ "facets" ]))
      Right = if kind = BrowsePage then Some Disabled else Some (Items (widgets [ "toc" ]))
      Main = Some (Items (widgets (defaultMain kind)))
      Footer = Some (Items [ docsLink; changelogLink; WidgetRef "version" ]) }

let resolve (baseL: Layout) (overlays: Layout list) : ResolvedSlots =
    let l = overlays |> List.fold Layout.overlay baseL
    let items =
        function
        | Some (Items xs) -> xs
        | _ -> []
    let widgets (slot: Slot option) =
        items slot |> List.choose (function WidgetRef w -> Some w | _ -> None)
    { Nav = items l.Nav
      Top = widgets l.Top
      Toolbar = widgets l.Toolbar
      Left = items l.Left
      Right = items l.Right
      Main = widgets l.Main
      Footer = items l.Footer }

let forPage (settings: SiteSettings) (kind: PageKind) (pageLayout: Layout) : ResolvedSlots =
    resolve (baseLayout settings kind) [ settings.Layout; pageLayout ]

let forDocument (settings: SiteSettings) (now: DateTimeOffset) (doc: SiteDocument) : ResolvedSlots =
    let rules =
        settings.Document.Rules
        |> List.filter (fun r -> FilterEngine.matches now r.Match doc)
        |> List.map (fun r -> r.Layout)
    resolve (baseLayout settings DocumentPage) (settings.Layout :: settings.Document.Layout :: rules)

let openTarget (settings: SiteSettings) (def: PageDef option) : OpenTarget =
    def |> Option.bind (fun d -> d.Open)
    |> Option.orElse settings.Document.Open
    |> Option.defaultValue OpenPage

let viewSpec (settings: SiteSettings) (def: PageDef option) : Views.ViewSpec =
    let d = Views.ViewSpec.defaults
    { View = def |> Option.bind (fun p -> p.View) |> Option.orElse settings.DefaultView |> Option.defaultValue d.View
      Columns = def |> Option.bind (fun p -> p.CardsColumns) |> Option.orElse settings.CardsColumns |> Option.defaultValue d.Columns
      TableColumns = def |> Option.bind (fun p -> p.TableColumns) |> Option.defaultValue d.TableColumns
      ListShow = def |> Option.bind (fun p -> p.ListShow) |> Option.defaultValue d.ListShow }

/// Browse shows a table of name, source and tags unless the site or the page picks a view.
let browseViewSpec (settings: SiteSettings) (def: PageDef option) : Views.ViewSpec =
    let s = viewSpec settings def
    let viewSet = (def |> Option.bind (fun p -> p.View) |> Option.isSome) || settings.DefaultView.IsSome
    let colsSet = def |> Option.bind (fun p -> p.TableColumns) |> Option.isSome
    { s with
        View = if viewSet then s.View else "table"
        TableColumns = if colsSet then s.TableColumns else [ "title"; "source"; "tags" ] }
