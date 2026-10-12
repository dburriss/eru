module Eru.Site.PageLayout

open System
open Eru

// Pure resolution of what a page shows: its four slots and its list view. Precedence is
// built-in defaults for the page kind, then `site.layout`, then the page's own `layout`
// (document pages: `site.document.layout`, then matching `site.document.rules` in order).
// A slot set at a later level replaces the earlier one as a whole.

type ResolvedSlots = {
    Top   : LayoutItem list
    Left  : LayoutItem list
    Right : LayoutItem list
    Main  : string list
}

type PageKind =
    | ListPage           // browse, tags, custom filter pages
    | DetailPage         // tags/<tag>, sources/<name>, ...
    | ContentPage
    | ContentListPage    // content and filter on one page
    | DocumentPage

let defaultMain (kind: PageKind) : string list =
    match kind with
    | ListPage -> [ "title"; "list" ]
    | DetailPage -> [ "breadcrumbs"; "title"; "cli"; "list" ]
    | ContentPage -> [ "title"; "content" ]
    | ContentListPage -> [ "title"; "content"; "list" ]
    | DocumentPage -> SiteVocabulary.defaultDocumentMain

let baseLayout (settings: SiteSettings) (kind: PageKind) : Layout =
    let widgets = List.map WidgetRef
    { Top = Some (Items (SiteSettings.knownPageIds settings |> List.map (fun id -> PageRef (id, None))))
      Left = Some (Items (widgets [ "facets" ]))
      Right = Some (Items (widgets [ "toc" ]))
      Main = Some (Items (widgets (defaultMain kind))) }

let resolve (baseL: Layout) (overlays: Layout list) : ResolvedSlots =
    let l = overlays |> List.fold Layout.overlay baseL
    let items =
        function
        | Some (Items xs) -> xs
        | _ -> []
    { Top = items l.Top
      Left = items l.Left
      Right = items l.Right
      Main = items l.Main |> List.choose (function WidgetRef w -> Some w | _ -> None) }

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
