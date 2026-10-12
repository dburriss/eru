module Eru.Tests.SiteConfigTests

open Xunit
open Eru
open Eru.Adapters
open Eru.Tests.SiteTestSupport

let private items (slot: Slot option) =
    match slot with
    | Some (Items xs) -> xs
    | _ -> failwith "expected a slot with items"

// ── parsing ──────────────────────────────────────────────────────────────────

[<Fact>]
let ``absent site keys leave the defaults`` () =
    let r = Config.merge None None |> Result.defaultWith failwith
    Assert.Equal(SiteSettings.empty, r.Site)

[<Fact>]
let ``layout slots parse false, arrays and mixed items`` () =
    let s =
        siteSettings """{
          "layout": {
            "top": ["browse", "sources", { "label": "Wiki", "url": "https://wiki.example" }, { "page": "tags", "label": "Labels" }],
            "left": false,
            "right": ["toc", "related"]
          }
        }"""
    Assert.Equal(Some Disabled, s.Layout.Left)
    Assert.Equal<LayoutItem list>(
        [ PageRef ("browse", None); PageRef ("sources", None); LinkRef ("Wiki", "https://wiki.example"); PageRef ("tags", Some "Labels") ],
        items s.Layout.Top)
    Assert.Equal<LayoutItem list>([ WidgetRef "toc"; WidgetRef "related" ], items s.Layout.Right)
    Assert.Equal(None, s.Layout.Main)

[<Fact>]
let ``a page id is allowed in a side slot and parsed as a page ref`` () =
    let s = siteSettings """{ "layout": { "left": ["facets", "sources"] } }"""
    Assert.Equal<LayoutItem list>([ WidgetRef "facets"; PageRef ("sources", None) ], items s.Layout.Left)

[<Fact>]
let ``pages parse false and objects`` () =
    let s =
        siteSettings """{
          "pages": {
            "types": false,
            "needs-review": { "title": "Needs review", "filter": { "stale": true }, "view": "table",
                       "table": { "columns": ["title", "staleAfter"] },
                       "sort": { "by": "staleAfter", "dir": "desc" }, "open": "resource", "limit": 10 }
          }
        }"""
    Assert.Equal(Some Dropped, s.Pages |> List.tryPick (function "types", e -> Some e | _ -> None))
    match s.Pages |> List.tryPick (function "needs-review", Defined d -> Some d | _ -> None) with
    | Some d ->
        Assert.Equal(Some "Needs review", d.Title)
        Assert.Equal(Some true, d.Filter.Value.Stale)
        Assert.Equal(Some "table", d.View)
        Assert.Equal(Some [ "title"; "staleAfter" ], d.TableColumns)
        Assert.Equal(Some { By = "staleAfter"; Descending = true }, d.Sort)
        Assert.Equal(Some OpenResource, d.Open)
        Assert.Equal(Some 10, d.Limit)
    | None -> Assert.Fail "needs-review page missing"

[<Fact>]
let ``filter accepts a string or a list and a nested not`` () =
    let s =
        siteSettings """{
          "pages": { "p": { "filter": { "type": "runbook", "tag": ["a", "b"], "bundle": ["kb/ops"], "exact": true,
                                         "not": { "status": ["deprecated"] } } } }
        }"""
    let f = s.Pages |> List.pick (function "p", Defined d -> d.Filter | _ -> None)
    Assert.Equal<string list>([ "runbook" ], f.Type)
    Assert.Equal<string list>([ "a"; "b" ], f.Tag)
    Assert.True f.Exact
    Assert.Equal<string list>([ "deprecated" ], f.Not.Value.Status)

[<Fact>]
let ``document layout and rules parse`` () =
    let s =
        siteSettings """{
          "document": {
            "layout": { "left": false, "right": ["toc"] },
            "rules": [ { "match": { "type": ["runbook"] }, "layout": { "right": ["toc"], "main": ["title", "content"] } } ]
          }
        }"""
    Assert.Equal(Some Disabled, s.Document.Layout.Left)
    Assert.Equal(1, s.Document.Rules.Length)
    Assert.Equal<string list>([ "runbook" ], s.Document.Rules.Head.Match.Type)

[<Fact>]
let ``site section survives a serialize and deserialize round trip`` () =
    let json = """{ "version": 3, "site": { "home": "welcome", "layout": { "left": false, "top": ["browse"] },
                     "pages": { "welcome": { "content": { "file": "docs/welcome.md" } }, "types": false } } }"""
    let local = Serialization.deserialize<LocalConfig> json |> Result.defaultWith failwith
    let again = Serialization.serialize local |> Serialization.deserialize<LocalConfig> |> Result.defaultWith failwith
    let a = Config.merge None (Some local) |> Result.defaultWith failwith
    let b = Config.merge None (Some again) |> Result.defaultWith failwith
    Assert.Equal(a.Site, b.Site)
    Assert.Equal(Some "welcome", b.Site.Home)

// ── merge ────────────────────────────────────────────────────────────────────

let private local (siteJson: string) : LocalConfig =
    Serialization.deserialize<LocalConfig> ("{ \"version\": 3, \"site\": " + siteJson + " }") |> Result.defaultWith failwith

let private global' (siteJson: string) : GlobalConfig =
    Serialization.deserialize<GlobalConfig> ("{ \"version\": 3, \"site\": " + siteJson + " }") |> Result.defaultWith failwith

[<Fact>]
let ``layout merges per slot with local over global`` () =
    let g = global' """{ "layout": { "left": ["facets"], "right": ["toc"] } }"""
    let l = local """{ "layout": { "right": ["related"] } }"""
    let eff = Config.merge (Some g) (Some l) |> Result.defaultWith failwith
    Assert.Equal<LayoutItem list>([ WidgetRef "facets" ], items eff.Site.Layout.Left)
    Assert.Equal<LayoutItem list>([ WidgetRef "related" ], items eff.Site.Layout.Right)

[<Fact>]
let ``pages merge by id and a local false removes a global page`` () =
    let g = global' """{ "pages": { "a": { "filter": { "tag": "x" } }, "b": { "filter": { "tag": "y" } } } }"""
    let l = local """{ "pages": { "a": false, "c": { "filter": { "tag": "z" } } } }"""
    let eff = Config.merge (Some g) (Some l) |> Result.defaultWith failwith
    Assert.Equal<string list>([ "a"; "b"; "c" ], eff.Site.Pages |> List.map fst)
    Assert.Equal(Some Dropped, eff.Site.Pages |> List.tryPick (function "a", e -> Some e | _ -> None))
    Assert.DoesNotContain("a", SiteSettings.knownPageIds eff.Site)

[<Fact>]
let ``document rules concatenate with global first`` () =
    let g = global' """{ "document": { "rules": [ { "match": { "type": "a" }, "layout": { "left": false } } ] } }"""
    let l = local """{ "document": { "rules": [ { "match": { "type": "b" }, "layout": { "left": false } } ] } }"""
    let eff = Config.merge (Some g) (Some l) |> Result.defaultWith failwith
    Assert.Equal<string list list>([ [ "a" ]; [ "b" ] ], eff.Site.Document.Rules |> List.map (fun r -> r.Match.Type))

// ── validation ───────────────────────────────────────────────────────────────

let private assertRejected (needle: string) (siteJson: string) =
    let e = siteError siteJson
    Assert.Contains(needle, e)

[<Fact>]
let ``a custom page id equal to a widget name is rejected`` () =
    assertRejected "reserved widget name" """{ "pages": { "toc": { "filter": {} } } }"""
    assertRejected "reserved widget name" """{ "pages": { "stale": { "filter": {} } } }"""

[<Fact>]
let ``a built-in page id that is also a widget name can still be overridden and listed in top`` () =
    let s = siteSettings """{ "layout": { "top": ["tags"] }, "pages": { "tags": { "filter": { "tag": "x" } } } }"""
    Assert.Equal<LayoutItem list>([ PageRef ("tags", None) ], items s.Layout.Top)

[<Fact>]
let ``an unknown page id in top is rejected`` () =
    assertRejected "unknown page id 'nope'" """{ "layout": { "top": ["browse", "nope"] } }"""

[<Fact>]
let ``a widget name in top is rejected`` () =
    assertRejected "is a widget, not a page" """{ "layout": { "top": ["facets"] } }"""

[<Fact>]
let ``an unknown widget in main is rejected`` () =
    assertRejected "unknown widget 'sparkles'" """{ "pages": { "p": { "filter": {}, "layout": { "main": ["sparkles"] } } } }"""

[<Fact>]
let ``content in a side slot is rejected`` () =
    assertRejected "widget 'content' is not valid in right" """{ "layout": { "right": ["content"] } }"""

[<Fact>]
let ``toc in main is rejected`` () =
    assertRejected "widget 'toc' is not valid in main" """{ "pages": { "p": { "filter": {}, "layout": { "main": ["toc"] } } } }"""

[<Fact>]
let ``site-wide layout main is rejected`` () =
    assertRejected "site.layout.main is not supported" """{ "layout": { "main": ["title"] } }"""

[<Fact>]
let ``a rule with an empty match is rejected`` () =
    assertRejected "must set at least one field" """{ "document": { "rules": [ { "match": {}, "layout": { "left": false } } ] } }"""

[<Fact>]
let ``a document layout without content in main is rejected`` () =
    assertRejected "must keep the \"content\" widget" """{ "document": { "layout": { "main": ["title"] } } }"""
    assertRejected "must keep the \"content\" widget"
        """{ "document": { "rules": [ { "match": { "type": "x" }, "layout": { "main": false } } ] } }"""

[<Fact>]
let ``bad view, column and column count are rejected`` () =
    assertRejected "unknown view 'graph'" """{ "pages": { "p": { "filter": {}, "view": "graph" } } }"""
    assertRejected "unknown column 'colour'" """{ "pages": { "p": { "filter": {}, "view": "table", "table": { "columns": ["colour"] } } } }"""
    assertRejected "between 1 and 6" """{ "pages": { "p": { "filter": {}, "cards": { "columns": 7 } } } }"""
    assertRejected "between 1 and 6" """{ "cards": { "columns": 0 } }"""
    assertRejected "unknown view 'tiles'" """{ "defaultView": "tiles" }"""

[<Fact>]
let ``an option block for another view is rejected`` () =
    assertRejected "only valid with view \"cards\"" """{ "pages": { "p": { "filter": {}, "view": "table", "cards": { "columns": 2 } } } }"""
    assertRejected "only valid with view \"table\"" """{ "pages": { "p": { "filter": {}, "table": { "columns": ["title"] } } } }"""

[<Fact>]
let ``an option block is judged against the site default view`` () =
    let s = siteSettings """{ "defaultView": "table", "pages": { "p": { "filter": {}, "table": { "columns": ["title"] } } } }"""
    Assert.Equal(1, s.Pages.Length)

[<Fact>]
let ``a page needs content or a filter`` () =
    assertRejected "needs at least one of" """{ "pages": { "p": { "title": "Empty" } } }"""

[<Fact>]
let ``content file escaping the repo or absolute is rejected`` () =
    assertRejected "inside the repository" """{ "pages": { "p": { "content": { "file": "../secret.md" } } } }"""
    assertRejected "inside the repository" """{ "pages": { "p": { "content": { "file": "/etc/passwd" } } } }"""
    assertRejected "inside the repository" """{ "pages": { "p": { "content": { "file": "docs/../../x.md" } } } }"""

[<Fact>]
let ``unknown home and dropped browse without home are rejected`` () =
    assertRejected "site.home: unknown page id 'nowhere'" """{ "home": "nowhere" }"""
    assertRejected "set site.home" """{ "pages": { "browse": false } }"""

[<Fact>]
let ``dropping browse is fine once home names another page`` () =
    let s = siteSettings """{ "home": "welcome", "pages": { "browse": false, "welcome": { "content": { "file": "w.md" } } } }"""
    Assert.Equal(Some "welcome", s.Home)

[<Fact>]
let ``unknown keys are rejected with the allowed list`` () =
    assertRejected "unknown key 'colour'" """{ "layout": { "colour": [] } }"""

[<Fact>]
let ``shape errors name the config file and the key path`` () =
    let e = siteError """{ "layout": { "colour": [] } }"""
    Assert.StartsWith("local config: site.layout: unknown key 'colour'", e)

[<Fact>]
let ``normalizeRelPath folds dot segments and rejects escapes`` () =
    Assert.Equal(Some "docs/a.md", SiteSettings.normalizeRelPath "./docs/x/../a.md")
    Assert.Equal(Some "docs/a.md", SiteSettings.normalizeRelPath "docs\\a.md")
    Assert.Equal(None, SiteSettings.normalizeRelPath "../a.md")
    Assert.Equal(None, SiteSettings.normalizeRelPath "/a.md")
    Assert.Equal(None, SiteSettings.normalizeRelPath "")
