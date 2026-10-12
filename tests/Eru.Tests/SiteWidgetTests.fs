module Eru.Tests.SiteWidgetTests

open System
open Xunit
open Eru
open Eru.Site
open Eru.Site.Widgets
open Eru.Tests.SiteTestSupport

let private noRelated : RelatedLinks = { Incoming = []; Outgoing = [] }

let private ctxFor (doc: SiteDocument) (contentHtml: string) (related: RelatedLinks) : WidgetContext =
    let env = HtmlTemplates.defaultEnv HtmlTemplates.emptyModel
    { Env = env
      Prefix = "../../"
      Slots = { Top = []; Left = []; Right = []; Main = [] }
      Page = HtmlTemplates.documentPageData env doc contentHtml related }

let private render (name: string) (doc: SiteDocument) =
    renderWidget (ctxFor doc "<p>body</p>" noRelated) name

[<Fact>]
let ``every widget in the vocabulary has a renderer`` () =
    for name, _ in SiteVocabulary.widgets do
        Assert.True(Widgets.renderers.ContainsKey name, $"no renderer for widget '{name}'")

[<Fact>]
let ``verified with no entries shows unverified and no verifier list`` () =
    let html = render "verified" baseDoc
    Assert.Contains("badge-unverified", html)
    Assert.DoesNotContain("verifier-list", html)

[<Fact>]
let ``verified shows the tier and each verifier with the date`` () =
    let at = DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero)
    let doc = { baseDoc with Verified = [ { By = "human:ada"; At = Some at }; { By = "process:nightly"; At = None } ] }
    let html = render "verified" doc
    Assert.Contains("badge-human-reviewed", html)
    Assert.Contains("human:ada", html)
    Assert.Contains("actor-human", html)
    Assert.Contains("2026-03-04", html)
    Assert.Contains("process:nightly", html)
    Assert.Contains("actor-machine", html)

[<Fact>]
let ``verified omits the date when at is absent`` () =
    let doc = { baseDoc with Verified = [ { By = "process:nightly"; At = None } ] }
    let html = render "verified" doc
    Assert.Contains("badge-machine-confirmed", html)
    Assert.DoesNotContain("<time", html)

[<Fact>]
let ``generated shows actor and date`` () =
    let at = DateTimeOffset(2026, 5, 6, 0, 0, 0, TimeSpan.Zero)
    let html = render "generated" { baseDoc with Generated = Some { By = "process:agent"; At = Some at } }
    Assert.Contains("process:agent", html)
    Assert.Contains("actor-machine", html)
    Assert.Contains("2026-05-06", html)

[<Fact>]
let ``generated omits the date when at is absent`` () =
    let html = render "generated" { baseDoc with Generated = Some { By = "human:ada"; At = None } }
    Assert.Contains("actor-human", html)
    Assert.DoesNotContain("<time", html)

[<Fact>]
let ``generated renders nothing when the field is missing`` () =
    Assert.Equal("", render "generated" baseDoc)

[<Fact>]
let ``stale renders nothing for a fresh document`` () =
    Assert.Equal("", render "stale" baseDoc)
    Assert.Equal("", render "stale" { baseDoc with StaleAfter = Some (DateTimeOffset.UtcNow.AddDays 1.0) })
    Assert.Contains("doc-stale-warning", render "stale" { baseDoc with StaleAfter = Some (DateTimeOffset.UtcNow.AddDays -1.0) })

[<Fact>]
let ``toc is built from rendered headings and is empty without any`` () =
    let html = MarkdownRenderer.render "# Title\n\n## First part\n\ntext\n\n### Deeper\n\n## Second\n"
    let toc = renderWidget (ctxFor baseDoc html noRelated) "toc"
    Assert.Contains("href=\"#first-part\"", toc)
    Assert.Contains("toc-l3", toc)
    Assert.DoesNotContain(">Title<", toc)
    Assert.Equal("", renderWidget (ctxFor baseDoc "<p>no headings</p>" noRelated) "toc")

[<Fact>]
let ``toc renders nothing on a page without content`` () =
    let ctx = { ctxFor baseDoc "" noRelated with Page = { PageData.empty with Title = "List" } }
    Assert.Equal("", renderWidget ctx "toc")

let private someRelated : RelatedLinks =
    { Incoming = [ { Id = "src:b.md"; Title = "B"; PageUrl = Some "files/src/b.md.html"; IsExternal = false } ]; Outgoing = [] }

[<Fact>]
let ``related lists links without the graph element`` () =
    let html = renderWidget (ctxFor baseDoc "<p/>" someRelated) "related"
    Assert.Contains("Links to this document", html)
    Assert.DoesNotContain("id=\"doc-graph\"", html)

[<Fact>]
let ``graph renders hidden until the script draws it and does not repeat the link lists`` () =
    let html = renderWidget (ctxFor baseDoc "<p/>" someRelated) "graph"
    Assert.Contains("id=\"doc-graph\"", html)
    Assert.Contains("class=\"doc-graph\" hidden", html)
    Assert.DoesNotContain("Links to this document", html)

[<Fact>]
let ``related and graph render nothing without links`` () =
    Assert.Equal("", render "related" baseDoc)
    Assert.Equal("", render "graph" baseDoc)

[<Fact>]
let ``meta shows type and lifecycle status`` () =
    let html = render "meta" { baseDoc with Type = Some "runbook"; Status = Some "stable" }
    Assert.Contains("badge-type", html)
    Assert.Contains("runbook", html)
    Assert.Contains("badge-lifecycle-stable", html)
    Assert.Equal("", render "meta" baseDoc)

[<Fact>]
let ``a facets widget with checkbox mode marks the container for the script`` () =
    let model =
        { HtmlTemplates.emptyModel with
            Types = [ { SiteType.Name = "runbook"; FileCount = 1; Files = [] } ]
            Bundles = [ { SiteBundle.Name = "kb/ops"; Source = "kb"; Path = "ops"; Kind = "okf"; FileCount = 1; Files = [] } ]
            AllExtensions = [ ".md" ] }
    let env = HtmlTemplates.defaultEnv model
    let ctx mode =
        { Env = env; Prefix = ""; Slots = { Top = []; Left = []; Right = []; Main = [] }
          Page = { PageData.empty with FacetMode = mode } }
    let filter = renderWidget (ctx FacetFilter) "facets"
    Assert.Contains("data-facet-mode=\"filter\"", filter)
    Assert.Contains("data-facet=\"type\"", filter)
    Assert.Contains("data-facet=\"bundle\"", filter)
    Assert.Contains("data-facet=\"ext\"", filter)
    let links = renderWidget (ctx FacetLinks) "facets"
    Assert.Contains("data-facet-mode=\"links\"", links)
    Assert.DoesNotContain("data-facet=\"ext\"", links)
