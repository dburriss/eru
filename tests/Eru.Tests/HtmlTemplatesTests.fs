module Eru.Tests.HtmlTemplatesTests

open Xunit
open Eru
open Eru.Site

let private baseDoc : SiteDocument = {
    Id          = "src:a.md"
    Source      = "src"
    RemotePath  = "a.md"
    Title       = "A"
    Extension   = ".md"
    Tags        = []
    Description = None
    SyncStatus  = Pulled
    Body        = None
    PageUrl     = Some "files/src/a.md.html"
    Type        = None
    Status      = None
    Generated   = None
    Verified    = []
    StaleAfter  = None
    Resource    = None
    Bundle      = None
}

let private noRelated : RelatedLinks = { Incoming = []; Outgoing = [] }

[<Fact>]
let ``no verified entries renders unverified trust tier`` () =
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>" noRelated
    Assert.Contains("badge-unverified", html)

[<Fact>]
let ``verified by a human actor renders human-reviewed`` () =
    let doc = { baseDoc with Verified = [ { By = "human:ahormati"; At = None } ] }
    let html = HtmlTemplates.filePage doc "<p>body</p>" noRelated
    Assert.Contains("badge-human-reviewed", html)

[<Fact>]
let ``verified by a non-human actor renders machine-confirmed`` () =
    let doc = { baseDoc with Verified = [ { By = "process:finance-nightly"; At = None } ] }
    let html = HtmlTemplates.filePage doc "<p>body</p>" noRelated
    Assert.Contains("badge-machine-confirmed", html)

[<Fact>]
let ``stale_after in the past renders a staleness warning`` () =
    let doc = { baseDoc with StaleAfter = Some (System.DateTimeOffset.UtcNow.AddDays(-1.0)) }
    let html = HtmlTemplates.filePage doc "<p>body</p>" noRelated
    Assert.Contains("doc-stale-warning", html)

[<Fact>]
let ``stale_after in the future does not render a staleness warning`` () =
    let doc = { baseDoc with StaleAfter = Some (System.DateTimeOffset.UtcNow.AddDays(1.0)) }
    let html = HtmlTemplates.filePage doc "<p>body</p>" noRelated
    Assert.DoesNotContain("doc-stale-warning", html)

[<Fact>]
let ``absent stale_after does not render a staleness warning`` () =
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>" noRelated
    Assert.DoesNotContain("doc-stale-warning", html)

[<Fact>]
let ``no related links renders no graph section`` () =
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>" noRelated
    Assert.DoesNotContain("doc-graph", html)

[<Fact>]
let ``incoming related link renders in fallback list`` () =
    let related = { Incoming = [ { Id = "src:b.md"; Title = "B"; PageUrl = Some "files/src/b.md.html"; IsExternal = false } ]; Outgoing = [] }
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>" related
    Assert.Contains("Links to this document", html)
    Assert.Contains("files/src/b.md.html", html)

[<Fact>]
let ``outgoing external related link renders with target blank`` () =
    let related = { Incoming = []; Outgoing = [ { Id = "https://example.com"; Title = "https://example.com"; PageUrl = None; IsExternal = true } ] }
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>" related
    Assert.Contains("Links from this document", html)
    Assert.Contains("target=\"_blank\"", html)

[<Fact>]
let ``document page heading and title use the document title, not the filename`` () =
    let doc = { baseDoc with Title = "Customer Orders"; RemotePath = "docs/orders.md" }
    let html = HtmlTemplates.filePage doc "<p>body</p>" noRelated
    Assert.Contains("Customer Orders", html)
    Assert.DoesNotContain("<title>orders.md", html)

let private baseSource : SiteSource = {
    Name        = "src"
    Url         = None
    Description = None
    HasManifest = false
    HasOkf      = false
    FileCount   = 1
    Files       = []
}

[<Fact>]
let ``sources list shows okf badge for an okf source and never no manifest`` () =
    let html = HtmlTemplates.sourcesListHtml "" [ { baseSource with HasOkf = true } ]
    Assert.Contains("badge-okf", html)
    Assert.DoesNotContain("no manifest", html)

[<Fact>]
let ``sources list shows manifest badge for a manifest source`` () =
    let html = HtmlTemplates.sourcesListHtml "" [ { baseSource with HasManifest = true } ]
    Assert.Contains("badge-manifest", html)
    Assert.DoesNotContain("badge-okf", html)

[<Fact>]
let ``sources list shows no badge for a plain source`` () =
    let html = HtmlTemplates.sourcesListHtml "" [ baseSource ]
    Assert.DoesNotContain("badge-manifest", html)
    Assert.DoesNotContain("badge-okf", html)
    Assert.DoesNotContain("no manifest", html)
