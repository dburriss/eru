module Eru.Tests.SiteViewTests

open Xunit
open Eru
open Eru.Site
open Eru.Site.Views
open Eru.Tests.SiteTestSupport

let private vc : ViewContext = { Prefix = "../"; TagsLive = true; Open = OpenPage; SourceUrl = fun _ -> None }

let private docs =
    [ { baseDoc with Title = "Alpha"; Type = Some "runbook"; Tags = [ "ops"; "has space" ]; Bundle = Some "kb/ops"; Status = Some "stable" }
      { baseDoc with Id = "src:b.md"; RemotePath = "b.md"; Title = "Beta"; PageUrl = None; SyncStatus = IndexOnly } ]

let private spec view = { ViewSpec.defaults with View = view }

[<Fact>]
let ``cards view sets the column count as a CSS variable`` () =
    let html = renderDocs vc { spec "cards" with Columns = 2 } docs
    Assert.Contains("class=\"card-grid\" style=\"--cols: 2\"", html)
    Assert.Contains("file-card", html)

[<Fact>]
let ``list view renders one list item per document`` () =
    let html = renderDocs vc (spec "list") docs
    Assert.Contains("<ul class=\"doc-list\">", html)
    Assert.Equal(2, html.Split("<li ").Length - 1)

[<Fact>]
let ``table view renders the chosen columns in order`` () =
    let html = renderDocs vc { spec "table" with TableColumns = [ "title"; "type"; "bundle" ] } docs
    Assert.Contains("<table class=\"doc-table\">", html)
    Assert.True(html.IndexOf "data-col=\"title\"" < html.IndexOf "data-col=\"type\"")
    Assert.True(html.IndexOf "data-col=\"type\"" < html.IndexOf "data-col=\"bundle\"")
    Assert.DoesNotContain("data-col=\"source\"", html)
    Assert.Contains("kb/ops", html)

[<Theory>]
[<InlineData("cards")>]
[<InlineData("list")>]
[<InlineData("table")>]
let ``every view emits the filter contract on each document`` (view: string) =
    let html = renderDocs vc (spec view) docs
    Assert.Equal(2, html.Split("data-doc ").Length - 1)
    Assert.Contains("data-source=\"[&quot;src&quot;]\"", html)
    Assert.Contains("data-type=\"[&quot;runbook&quot;]\"", html)
    Assert.Contains("data-bundle=\"[&quot;kb/ops&quot;]\"", html)
    Assert.Contains("data-status=\"[&quot;stable&quot;]\"", html)
    Assert.Contains("data-ext=\"[&quot;.md&quot;]\"", html)
    // a tag containing a space stays one value
    Assert.Contains("data-tags=\"[&quot;ops&quot;,&quot;has space&quot;]\"", html)

[<Fact>]
let ``groups render headed sections the script can hide`` () =
    let html = renderGroups vc (spec "list") (FilterEngine.group "type" docs)
    Assert.Equal(2, html.Split("data-doc-group").Length - 1)
    Assert.Contains("runbook", html)
    Assert.Contains("No type", html)

[<Fact>]
let ``open page links the document page and falls back to text for index-only docs`` () =
    let html = renderDocs vc (spec "list") docs
    Assert.Contains("href=\"../files/src/a.md.html\"", html)
    Assert.Contains("Beta", html)
    Assert.DoesNotContain(">Beta</a>", html)

[<Fact>]
let ``open resource links the resource frontmatter url and falls back to the page`` () =
    let withResource = { docs.Head with Resource = Some "https://example.com/r" }
    let html = renderDocs { vc with Open = OpenResource } (spec "list") [ withResource; docs.[1] ]
    Assert.Contains("href=\"https://example.com/r\" target=\"_blank\"", html)
    let fallback = renderDocs { vc with Open = OpenResource } (spec "list") [ docs.Head ]
    Assert.Contains("href=\"../files/src/a.md.html\"", fallback)

[<Fact>]
let ``open source links the file in its repository`` () =
    let vcs = { vc with Open = OpenSource; SourceUrl = fun d -> sourceFileUrl "https://github.com/o/r.git" (Some "main") d.RemotePath }
    let html = renderDocs vcs (spec "list") docs
    Assert.Contains("https://github.com/o/r/blob/main/a.md", html)

[<Fact>]
let ``sourceFileUrl handles ssh, gitlab and local urls`` () =
    Assert.Equal(Some "https://github.com/o/r/blob/main/docs/a%20b.md", sourceFileUrl "git@github.com:o/r.git" (Some "main") "docs/a b.md")
    Assert.Equal(Some "https://gitlab.com/o/r/-/blob/HEAD/a.md", sourceFileUrl "https://gitlab.com/o/r" None "a.md")
    Assert.Equal(None, sourceFileUrl "/local/path" None "a.md")
