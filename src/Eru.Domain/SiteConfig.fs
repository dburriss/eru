namespace Eru

open System
open System.Text.Json

// Typed model of the `site` section's page/layout keys (home, defaultView, cards,
// layout, pages, document). The on-disk form uses unions-as-JSON (`false | array`,
// `string | object`), which System.Text.Json cannot map, so SiteSection keeps those
// keys as raw JsonElements and this module parses them into the types below.

type SlotName = Top | Left | Right | Main

type LayoutItem =
    | PageRef   of id: string * label: string option
    | WidgetRef of name: string
    | LinkRef   of label: string * url: string

type Slot =
    | Disabled
    | Items of LayoutItem list

// A slot that is None is "not set here": it falls through to the next level.
type Layout = {
    Top   : Slot option
    Left  : Slot option
    Right : Slot option
    Main  : Slot option
}

module Layout =
    let empty : Layout = { Top = None; Left = None; Right = None; Main = None }

    let get (name: SlotName) (l: Layout) : Slot option =
        match name with
        | Top -> l.Top
        | Left -> l.Left
        | Right -> l.Right
        | Main -> l.Main

    let set (name: SlotName) (slot: Slot option) (l: Layout) : Layout =
        match name with
        | Top -> { l with Top = slot }
        | Left -> { l with Left = slot }
        | Right -> { l with Right = slot }
        | Main -> { l with Main = slot }

    /// Slots set in `over` replace the ones in `under`, each as a whole.
    let overlay (under: Layout) (over: Layout) : Layout =
        { Top   = over.Top   |> Option.orElse under.Top
          Left  = over.Left  |> Option.orElse under.Left
          Right = over.Right |> Option.orElse under.Right
          Main  = over.Main  |> Option.orElse under.Main }

    let slotNames = [ Top; Left; Right; Main ]

// AND across fields, OR within a field's list. Empty lists and None mean "no constraint".
type FilterSpec = {
    Source : string list
    Bundle : string list
    Type   : string list
    Tag    : string list
    Status : string list
    Ext    : string list
    Path   : string list
    Stale  : bool option
    Exact  : bool                 // bundle matches only the named bundle, not its children
    Not    : FilterSpec option
}

module FilterSpec =
    let empty : FilterSpec =
        { Source = []; Bundle = []; Type = []; Tag = []; Status = []; Ext = []; Path = []
          Stale = None; Exact = false; Not = None }

    let rec isEmpty (f: FilterSpec) : bool =
        f.Source.IsEmpty && f.Bundle.IsEmpty && f.Type.IsEmpty && f.Tag.IsEmpty
        && f.Status.IsEmpty && f.Ext.IsEmpty && f.Path.IsEmpty && f.Stale.IsNone
        && (match f.Not with None -> true | Some n -> isEmpty n)

type SortSpec = { By: string; Descending: bool }

type ContentRef =
    | ContentFile of string       // repo-relative Markdown path
    | ContentDoc of string        // "<source>:<path>"

type OpenTarget = OpenPage | OpenResource | OpenSource

type PageDef = {
    Title        : string option
    Content      : ContentRef option
    Filter       : FilterSpec option
    View         : string option
    Sort         : SortSpec option
    Group        : string option
    Limit        : int option
    Open         : OpenTarget option
    Layout       : Layout
    CardsColumns : int option
    TableColumns : string list option
    ListShow     : string list option
}

type PageEntry =
    | Dropped
    | Defined of PageDef

type DocumentRule = { Match: FilterSpec; Layout: Layout }

type DocumentSettings = {
    Layout : Layout
    Open   : OpenTarget option
    Rules  : DocumentRule list
}

type SiteSettings = {
    Home         : string option
    DefaultView  : string option
    CardsColumns : int option
    Layout       : Layout
    Pages        : (string * PageEntry) list     // config order; custom pages follow built-ins in the default nav
    Document     : DocumentSettings
}

module SiteVocabulary =
    let builtInPages = [ "browse"; "sources"; "tags"; "types"; "bundles" ]
    let views = [ "cards"; "list"; "table" ]
    let defaultView = "cards"
    let defaultCardsColumns = 3
    let tableColumns =
        [ "title"; "source"; "path"; "bundle"; "type"; "tags"; "status"; "verified"; "staleAfter"
          "generated"; "syncStatus"; "ext" ]
    let defaultTableColumns = [ "title"; "source"; "type"; "tags"; "status" ]
    let defaultListShow = [ "source"; "type"; "status" ]
    let sortFields = tableColumns
    let groupFields = [ "tag"; "type"; "bundle"; "source" ]
    let filterFields = [ "source"; "bundle"; "type"; "tag"; "status"; "ext"; "path"; "stale"; "exact"; "not" ]

    let private sideOnly = [ Left; Right ]
    let private mainAndSide = [ Main; Left; Right ]

    /// Every widget and the slots it may appear in. Rendering lives in Eru.Site; a test
    /// asserts that each name here has a renderer.
    let widgets : (string * SlotName list) list =
        [ "breadcrumbs", [ Main ]
          "title",       [ Main ]
          "meta",        mainAndSide
          "verified",    mainAndSide
          "generated",   mainAndSide
          "description", mainAndSide
          "tags",        mainAndSide
          "stale",       [ Main ]
          "cli",         mainAndSide
          "content",     [ Main ]
          "list",        [ Main ]
          "toc",         sideOnly
          "related",     mainAndSide
          "graph",       mainAndSide
          "facets",      sideOnly
          "links",       sideOnly ]

    let isWidget (name: string) = widgets |> List.exists (fun (n, _) -> n = name)

    let widgetValidIn (name: string) (slot: SlotName) =
        widgets |> List.exists (fun (n, slots) -> n = name && List.contains slot slots)

    let defaultDocumentMain =
        [ "graph"; "breadcrumbs"; "title"; "meta"; "verified"; "generated"; "description"; "tags"; "stale"; "cli"; "content"; "related" ]

// Config v3 `site` section as stored on disk. Settings that are unions in JSON
// (`false | array`, `string | object`) stay raw and are parsed by SiteSettings.parse.
type SiteSection = {
    IgnorePatterns   : string list option
    HideEmptyBundles : bool option
    Home             : string option
    DefaultView      : string option
    Cards            : JsonElement option
    Layout           : JsonElement option
    Pages            : JsonElement option
    Document         : JsonElement option
}

module SiteSection =
    let empty : SiteSection =
        { IgnorePatterns = None; HideEmptyBundles = None; Home = None; DefaultView = None
          Cards = None; Layout = None; Pages = None; Document = None }

module SiteSettings =
    let empty : SiteSettings =
        { Home = None; DefaultView = None; CardsColumns = None; Layout = Layout.empty; Pages = []
          Document = { Layout = Layout.empty; Open = None; Rules = [] } }

    let slotLabel =
        function
        | Top -> "top"
        | Left -> "left"
        | Right -> "right"
        | Main -> "main"

    /// Normalises a repo-relative path to `/` separators. None when it is empty, rooted or
    /// escapes the repo root via `..`.
    let normalizeRelPath (path: string) : string option =
        let p = path.Replace('\\', '/').Trim()
        if p = "" || p.StartsWith "/" || (p.Length > 1 && p.[1] = ':') then None
        else
            let folded =
                p.Split('/')
                |> Array.filter (fun s -> s <> "" && s <> ".")
                |> Array.fold (fun acc seg ->
                    match acc with
                    | None -> None
                    | Some (stack: string list) ->
                        if seg = ".." then
                            match stack with
                            | [] -> None
                            | _ :: rest -> Some rest
                        else Some (seg :: stack)) (Some [])
            match folded with
            | Some (_ :: _ as stack) -> Some (String.Join("/", List.rev stack))
            | _ -> None

    // ── parsing ──────────────────────────────────────────────────────────────

    exception private Invalid of string

    let private fail (msg: string) : 'a = raise (Invalid msg)

    let private joinList (xs: string list) = String.Join(", ", xs)

    let private kindName (el: JsonElement) =
        match el.ValueKind with
        | JsonValueKind.Object -> "an object"
        | JsonValueKind.Array -> "an array"
        | JsonValueKind.String -> "a string"
        | JsonValueKind.Number -> "a number"
        | JsonValueKind.True | JsonValueKind.False -> "a boolean"
        | _ -> "null"

    let private expectObject (path: string) (el: JsonElement) =
        if el.ValueKind <> JsonValueKind.Object then fail $"{path} must be an object, not {kindName el}"

    let private expectKeys (path: string) (allowed: string list) (el: JsonElement) =
        for p in el.EnumerateObject() do
            if not (List.contains p.Name allowed) then
                fail $"{path}: unknown key '{p.Name}' (allowed: {joinList allowed})"

    let private str (path: string) (el: JsonElement) : string =
        if el.ValueKind <> JsonValueKind.String then fail $"{path} must be a string, not {kindName el}"
        el.GetString()

    let private int' (path: string) (el: JsonElement) : int =
        match el.ValueKind with
        | JsonValueKind.Number ->
            match el.TryGetInt32() with
            | true, i -> i
            | _ -> fail $"{path} must be a whole number"
        | _ -> fail $"{path} must be a number, not {kindName el}"

    let private bool' (path: string) (el: JsonElement) : bool =
        match el.ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> fail $"{path} must be true or false, not {kindName el}"

    /// A string or an array of strings.
    let private strings (path: string) (el: JsonElement) : string list =
        match el.ValueKind with
        | JsonValueKind.String -> [ el.GetString() ]
        | JsonValueKind.Array ->
            el.EnumerateArray() |> Seq.mapi (fun i e -> str $"{path}[{i}]" e) |> List.ofSeq
        | _ -> fail $"{path} must be a string or an array of strings, not {kindName el}"

    let private parseItem (path: string) (slot: SlotName) (el: JsonElement) : LayoutItem =
        match el.ValueKind with
        | JsonValueKind.String ->
            let s = el.GetString()
            match slot with
            | Top -> PageRef (s, None)
            | Main -> WidgetRef s
            | Left | Right -> if SiteVocabulary.isWidget s then WidgetRef s else PageRef (s, None)
        | JsonValueKind.Object ->
            expectKeys path [ "page"; "label"; "url" ] el
            let get (k: string) = match el.TryGetProperty k with | true, v -> Some v | _ -> None
            match get "page", get "label", get "url" with
            | Some p, label, None -> PageRef (str $"{path}.page" p, label |> Option.map (str $"{path}.label"))
            | None, Some l, Some u -> LinkRef (str $"{path}.label" l, str $"{path}.url" u)
            | _ -> fail $"{path} must be {{ \"page\", \"label\"? }} or {{ \"label\", \"url\" }}"
        | _ -> fail $"{path} must be a page id, a widget name or an object, not {kindName el}"

    let private parseSlot (path: string) (slot: SlotName) (el: JsonElement) : Slot =
        match el.ValueKind with
        | JsonValueKind.False -> Disabled
        | JsonValueKind.Array ->
            Items (el.EnumerateArray() |> Seq.mapi (fun i e -> parseItem $"{path}[{i}]" slot e) |> List.ofSeq)
        | _ -> fail $"{path} must be an array or false, not {kindName el}"

    let private parseLayout (path: string) (el: JsonElement) : Layout =
        expectObject path el
        expectKeys path [ "top"; "left"; "right"; "main" ] el
        let slot name (key: string) =
            match el.TryGetProperty key with
            | true, v -> Some (parseSlot $"{path}.{key}" name v)
            | _ -> None
        { Top = slot Top "top"; Left = slot Left "left"; Right = slot Right "right"; Main = slot Main "main" }

    let rec private parseFilter (path: string) (el: JsonElement) : FilterSpec =
        expectObject path el
        expectKeys path SiteVocabulary.filterFields el
        let get (k: string) = match el.TryGetProperty k with | true, v -> Some v | _ -> None
        let list (k: string) = get k |> Option.map (strings $"{path}.{k}") |> Option.defaultValue []
        { Source = list "source"
          Bundle = list "bundle"
          Type   = list "type"
          Tag    = list "tag"
          Status = list "status"
          Ext    = list "ext"
          Path   = list "path"
          Stale  = get "stale" |> Option.map (bool' $"{path}.stale")
          Exact  = get "exact" |> Option.map (bool' $"{path}.exact") |> Option.defaultValue false
          Not    = get "not" |> Option.map (parseFilter $"{path}.not") }

    let private parseOpen (path: string) (el: JsonElement) : OpenTarget =
        match str path el with
        | "page" -> OpenPage
        | "resource" -> OpenResource
        | "source" -> OpenSource
        | other -> fail $"{path}: unknown value '{other}' (expected page, resource or source)"

    let private parseSort (path: string) (el: JsonElement) : SortSpec =
        expectObject path el
        expectKeys path [ "by"; "dir" ] el
        let by =
            match el.TryGetProperty "by" with
            | true, v -> str $"{path}.by" v
            | _ -> fail $"{path}.by is required"
        let desc =
            match el.TryGetProperty "dir" with
            | true, v ->
                match str $"{path}.dir" v with
                | "asc" -> false
                | "desc" -> true
                | other -> fail $"{path}.dir: unknown value '{other}' (expected asc or desc)"
            | _ -> false
        { By = by; Descending = desc }

    let private parseContent (path: string) (el: JsonElement) : ContentRef =
        expectObject path el
        expectKeys path [ "file"; "doc" ] el
        match el.TryGetProperty "file", el.TryGetProperty "doc" with
        | (true, f), (false, _) -> ContentFile (str $"{path}.file" f)
        | (false, _), (true, d) -> ContentDoc (str $"{path}.doc" d)
        | _ -> fail $"{path} must have exactly one of \"file\" or \"doc\""

    let private parsePage (path: string) (el: JsonElement) : PageEntry =
        match el.ValueKind with
        | JsonValueKind.False -> Dropped
        | JsonValueKind.Object ->
            expectKeys path [ "title"; "content"; "filter"; "view"; "sort"; "group"; "limit"; "open"; "layout"; "cards"; "table"; "list" ] el
            let get (k: string) = match el.TryGetProperty k with | true, v -> Some v | _ -> None
            let block (k: string) (allowed: string) =
                get k |> Option.map (fun v ->
                    expectObject $"{path}.{k}" v
                    expectKeys $"{path}.{k}" [ allowed ] v
                    v)
            let optionOf (b: JsonElement option) (key: string) =
                b |> Option.bind (fun v -> match v.TryGetProperty key with | true, x -> Some x | _ -> None)
            Defined
                { Title        = get "title" |> Option.map (str $"{path}.title")
                  Content      = get "content" |> Option.map (parseContent $"{path}.content")
                  Filter       = get "filter" |> Option.map (parseFilter $"{path}.filter")
                  View         = get "view" |> Option.map (str $"{path}.view")
                  Sort         = get "sort" |> Option.map (parseSort $"{path}.sort")
                  Group        = get "group" |> Option.map (str $"{path}.group")
                  Limit        = get "limit" |> Option.map (int' $"{path}.limit")
                  Open         = get "open" |> Option.map (parseOpen $"{path}.open")
                  Layout       = get "layout" |> Option.map (parseLayout $"{path}.layout") |> Option.defaultValue Layout.empty
                  CardsColumns = optionOf (block "cards" "columns") "columns" |> Option.map (int' $"{path}.cards.columns")
                  TableColumns = optionOf (block "table" "columns") "columns" |> Option.map (strings $"{path}.table.columns")
                  ListShow     = optionOf (block "list" "show") "show" |> Option.map (strings $"{path}.list.show") }
        | _ -> fail $"{path} must be an object or false, not {kindName el}"

    let private parsePages (path: string) (el: JsonElement) : (string * PageEntry) list =
        expectObject path el
        el.EnumerateObject() |> Seq.map (fun p -> p.Name, parsePage $"{path}.{p.Name}" p.Value) |> List.ofSeq

    let private parseDocument (path: string) (el: JsonElement) : DocumentSettings =
        expectObject path el
        expectKeys path [ "layout"; "open"; "rules" ] el
        let get (k: string) = match el.TryGetProperty k with | true, v -> Some v | _ -> None
        let rule (i: int) (r: JsonElement) : DocumentRule =
            let rp = $"{path}.rules[{i}]"
            expectObject rp r
            expectKeys rp [ "match"; "layout" ] r
            let m =
                match r.TryGetProperty "match" with
                | true, v -> parseFilter $"{rp}.match" v
                | _ -> fail $"{rp}.match is required"
            if FilterSpec.isEmpty m then fail $"{rp}.match must set at least one field"
            let l =
                match r.TryGetProperty "layout" with
                | true, v -> parseLayout $"{rp}.layout" v
                | _ -> fail $"{rp}.layout is required"
            { Match = m; Layout = l }
        { Layout = get "layout" |> Option.map (parseLayout $"{path}.layout") |> Option.defaultValue Layout.empty
          Open   = get "open" |> Option.map (parseOpen $"{path}.open")
          Rules  =
            match get "rules" with
            | None -> []
            | Some r when r.ValueKind = JsonValueKind.Array -> r.EnumerateArray() |> Seq.mapi rule |> List.ofSeq
            | Some r -> fail $"{path}.rules must be an array, not {kindName r}" }

    let private parseCards (path: string) (el: JsonElement) : int option =
        expectObject path el
        expectKeys path [ "columns" ] el
        match el.TryGetProperty "columns" with
        | true, v -> Some (int' $"{path}.columns" v)
        | _ -> None

    /// Parses the raw `site` section of one config file. Shape errors only; cross-references
    /// (page ids, widget placement) are checked by `validate` after the two files are merged.
    let parse (section: SiteSection) : Result<SiteSettings, string> =
        try
            Ok { Home         = section.Home
                 DefaultView  = section.DefaultView
                 CardsColumns = section.Cards |> Option.bind (parseCards "site.cards")
                 Layout       = section.Layout |> Option.map (parseLayout "site.layout") |> Option.defaultValue Layout.empty
                 Pages        = section.Pages |> Option.map (parsePages "site.pages") |> Option.defaultValue []
                 Document     = section.Document |> Option.map (parseDocument "site.document")
                                |> Option.defaultValue empty.Document }
        with Invalid msg -> Error msg

    // ── merge ────────────────────────────────────────────────────────────────

    /// Local over global. `layout` merges per slot, `pages` by id (a local entry, including
    /// `false`, replaces the global one in place), `document.rules` concatenate global first.
    let merge (global': SiteSettings) (local: SiteSettings) : SiteSettings =
        let pages =
            let replaced =
                global'.Pages |> List.map (fun (id, e) ->
                    match local.Pages |> List.tryFind (fun (lid, _) -> lid = id) with
                    | Some (_, le) -> id, le
                    | None -> id, e)
            let added = local.Pages |> List.filter (fun (id, _) -> not (global'.Pages |> List.exists (fun (g, _) -> g = id)))
            replaced @ added
        { Home         = local.Home |> Option.orElse global'.Home
          DefaultView  = local.DefaultView |> Option.orElse global'.DefaultView
          CardsColumns = local.CardsColumns |> Option.orElse global'.CardsColumns
          Layout       = Layout.overlay global'.Layout local.Layout
          Pages        = pages
          Document     =
            { Layout = Layout.overlay global'.Document.Layout local.Document.Layout
              Open   = local.Document.Open |> Option.orElse global'.Document.Open
              Rules  = global'.Document.Rules @ local.Document.Rules } }

    // ── validation ───────────────────────────────────────────────────────────

    let private validId (id: string) =
        id <> "" && not (id.StartsWith ".")
        && id |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '_' || c = '.')

    let isBuiltIn (id: string) = List.contains id SiteVocabulary.builtInPages

    /// Ids of every page that exists: built-ins not dropped, plus defined custom pages.
    let knownPageIds (s: SiteSettings) : string list =
        let dropped = s.Pages |> List.choose (function id, Dropped -> Some id | _ -> None) |> Set.ofList
        let custom = s.Pages |> List.choose (function id, Defined _ when not (isBuiltIn id) -> Some id | _ -> None)
        (SiteVocabulary.builtInPages |> List.filter (fun id -> not (dropped.Contains id))) @ custom

    let private oneOf (path: string) (what: string) (allowed: string list) (value: string) =
        if not (List.contains value allowed) then
            fail $"{path}: unknown {what} '{value}' (expected one of: {joinList allowed})"

    let private validateLayout (known: Set<string>) (path: string) (layout: Layout) =
        for name in Layout.slotNames do
            match Layout.get name layout with
            | None | Some Disabled -> ()
            | Some (Items items) ->
                let sp = $"{path}.{slotLabel name}"
                for i, item in List.indexed items do
                    let ip = $"{sp}[{i}]"
                    match name, item with
                    | Top, PageRef (id, _) ->
                        if known.Contains id then ()
                        elif SiteVocabulary.isWidget id then
                            fail $"{ip}: '{id}' is a widget, not a page; top takes pages and links"
                        else fail $"{ip}: unknown page id '{id}'"
                    | Top, WidgetRef w -> fail $"{ip}: widget '{w}' is not valid in top; top takes pages and links"
                    | Top, LinkRef _ -> ()
                    | Main, WidgetRef w ->
                        if not (SiteVocabulary.isWidget w) then fail $"{ip}: unknown widget '{w}'"
                        elif not (SiteVocabulary.widgetValidIn w Main) then fail $"{ip}: widget '{w}' is not valid in main"
                    | Main, (PageRef _ | LinkRef _) -> fail $"{ip}: main takes widget names only"
                    | (Left | Right), WidgetRef w ->
                        if not (SiteVocabulary.widgetValidIn w name) then
                            fail $"{ip}: widget '{w}' is not valid in {slotLabel name}"
                    | (Left | Right), PageRef (id, _) ->
                        if not (known.Contains id) then fail $"{ip}: unknown widget or page id '{id}'"
                    | (Left | Right), LinkRef _ -> ()

    let private mainWidgets (layout: Layout) : string list option =
        match layout.Main with
        | Some (Items items) -> Some (items |> List.choose (function WidgetRef w -> Some w | _ -> None))
        | _ -> None

    let private validateFilter (path: string) (f: FilterSpec) =
        // Nothing to check beyond shape today; kept so rules and pages share one entry point.
        ignore path
        ignore f

    let private validatePage (s: SiteSettings) (known: Set<string>) (id: string) (def: PageDef) =
        let path = $"site.pages.{id}"
        if def.Content.IsNone && def.Filter.IsNone then
            fail $"{path} needs at least one of \"content\" or \"filter\""
        let effectiveView = def.View |> Option.orElse s.DefaultView |> Option.defaultValue SiteVocabulary.defaultView
        def.View |> Option.iter (oneOf $"{path}.view" "view" SiteVocabulary.views)
        if def.CardsColumns.IsSome && effectiveView <> "cards" then
            fail $"{path}.cards is only valid with view \"cards\" (this page uses \"{effectiveView}\")"
        if def.TableColumns.IsSome && effectiveView <> "table" then
            fail $"{path}.table is only valid with view \"table\" (this page uses \"{effectiveView}\")"
        if def.ListShow.IsSome && effectiveView <> "list" then
            fail $"{path}.list is only valid with view \"list\" (this page uses \"{effectiveView}\")"
        def.CardsColumns |> Option.iter (fun n ->
            if n < 1 || n > 6 then fail $"{path}.cards.columns must be between 1 and 6, not {n}")
        def.TableColumns |> Option.iter (fun cols ->
            if cols.IsEmpty then fail $"{path}.table.columns must not be empty"
            cols |> List.iter (oneOf $"{path}.table.columns" "column" SiteVocabulary.tableColumns))
        def.ListShow |> Option.iter (fun cols ->
            cols |> List.iter (oneOf $"{path}.list.show" "field" (SiteVocabulary.tableColumns |> List.filter ((<>) "title"))))
        def.Sort |> Option.iter (fun sort -> oneOf $"{path}.sort.by" "sort field" SiteVocabulary.sortFields sort.By)
        def.Group |> Option.iter (oneOf $"{path}.group" "group field" SiteVocabulary.groupFields)
        def.Limit |> Option.iter (fun n -> if n < 1 then fail $"{path}.limit must be at least 1, not {n}")
        def.Filter |> Option.iter (validateFilter $"{path}.filter")
        match def.Content with
        | Some (ContentFile f) ->
            if (normalizeRelPath f).IsNone then
                fail $"{path}.content.file '{f}' must be a path inside the repository (no '..' escape, not absolute)"
        | Some (ContentDoc d) ->
            if not (d.IndexOf ':' > 0 && d.IndexOf ':' < d.Length - 1) then fail $"{path}.content.doc '{d}' must look like <source>:<path>"
        | None -> ()
        validateLayout known $"{path}.layout" def.Layout
        match mainWidgets def.Layout with
        | Some ws ->
            if List.contains "content" ws && def.Content.IsNone then
                fail $"{path}.layout.main has \"content\" but the page has no content"
            if List.contains "list" ws && def.Filter.IsNone then
                fail $"{path}.layout.main has \"list\" but the page has no filter"
        | None -> ()

    let private requireContent (path: string) (layout: Layout) =
        match layout.Main with
        | Some Disabled -> fail $"{path}.main cannot be disabled; it must keep the \"content\" widget"
        | _ ->
            match mainWidgets layout with
            | Some ws when not (List.contains "content" ws) -> fail $"{path}.main must keep the \"content\" widget"
            | _ -> ()

    /// Cross-reference and placement checks on the merged settings.
    let validate (s: SiteSettings) : Result<unit, string> =
        try
            let known = knownPageIds s |> Set.ofList
            for id, _ in s.Pages do
                // built-in ids that are also widget names (`tags`) stay valid: they override the built-in page
                if SiteVocabulary.isWidget id && not (isBuiltIn id) then
                    fail $"site.pages.{id}: '{id}' is a reserved widget name and cannot be a page id"
                if not (validId id) then fail $"site.pages.{id}: page ids may only contain letters, digits, '-', '_' and '.'"
            let dups = s.Pages |> List.countBy fst |> List.filter (fun (_, n) -> n > 1)
            if not dups.IsEmpty then fail $"site.pages: duplicate page id '{fst dups.Head}'"
            s.DefaultView |> Option.iter (oneOf "site.defaultView" "view" SiteVocabulary.views)
            s.CardsColumns |> Option.iter (fun n ->
                if n < 1 || n > 6 then fail $"site.cards.columns must be between 1 and 6, not {n}")
            if s.Layout.Main.IsSome then
                fail "site.layout.main is not supported; set main per page (site.pages.<id>.layout) or in site.document"
            validateLayout known "site.layout" s.Layout
            for id, entry in s.Pages do
                match entry with
                | Defined def -> validatePage s known id def
                | Dropped -> ()
            validateLayout known "site.document.layout" s.Document.Layout
            requireContent "site.document.layout" s.Document.Layout
            s.Document.Rules |> List.iteri (fun i r ->
                let rp = $"site.document.rules[{i}].layout"
                validateLayout known rp r.Layout
                requireContent rp r.Layout)
            match s.Home with
            | Some h when not (known.Contains h) -> fail $"site.home: unknown page id '{h}'"
            | None when not (known.Contains "browse") ->
                fail "the built-in 'browse' page is dropped and site.home is not set; set site.home to a page id"
            | _ -> ()
            Ok ()
        with Invalid msg -> Error msg
