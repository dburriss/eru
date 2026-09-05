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
}

[<Fact>]
let ``no verified entries renders unverified trust tier`` () =
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>"
    Assert.Contains("badge-unverified", html)

[<Fact>]
let ``verified by a human actor renders human-reviewed`` () =
    let doc = { baseDoc with Verified = [ { By = "human:ahormati"; At = None } ] }
    let html = HtmlTemplates.filePage doc "<p>body</p>"
    Assert.Contains("badge-human-reviewed", html)

[<Fact>]
let ``verified by a non-human actor renders machine-confirmed`` () =
    let doc = { baseDoc with Verified = [ { By = "process:finance-nightly"; At = None } ] }
    let html = HtmlTemplates.filePage doc "<p>body</p>"
    Assert.Contains("badge-machine-confirmed", html)

[<Fact>]
let ``stale_after in the past renders a staleness warning`` () =
    let doc = { baseDoc with StaleAfter = Some (System.DateTimeOffset.UtcNow.AddDays(-1.0)) }
    let html = HtmlTemplates.filePage doc "<p>body</p>"
    Assert.Contains("doc-stale-warning", html)

[<Fact>]
let ``stale_after in the future does not render a staleness warning`` () =
    let doc = { baseDoc with StaleAfter = Some (System.DateTimeOffset.UtcNow.AddDays(1.0)) }
    let html = HtmlTemplates.filePage doc "<p>body</p>"
    Assert.DoesNotContain("doc-stale-warning", html)

[<Fact>]
let ``absent stale_after does not render a staleness warning`` () =
    let html = HtmlTemplates.filePage baseDoc "<p>body</p>"
    Assert.DoesNotContain("doc-stale-warning", html)
