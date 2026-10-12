module Eru.Tests.SiteGeneratorPagesTests

open System.IO
open Xunit
open Eru
open Eru.Site
open Eru.Site.SiteGenerator
open Eru.Tests.SiteTestSupport

let private sourceIndex =
    Map.ofList [
        "ops/restart.md",
            { makeEntry (Some "files/h1") "Restart" with
                Type = Some "runbook"
                Tags = [ "ops" ]
                Resource = Some "https://example.com/restart" }
        "dev/setup.md", { makeEntry (Some "files/h2") "Setup" with Type = Some "guide"; Tags = [ "dev" ] }
        "remote.md", makeEntry None "Remote only"
    ]

let private content =
    Map.ofList [ "files/h1", "# Restart\n\n## Steps\n\nrun it"; "files/h2", "# Setup\n\n## Install\n\ngo" ]

let private deps = makeDeps sourceIndex content Map.empty

let private generateWith (d: Deps) (siteJson: string) (check: string -> unit) =
    withOutDir (fun outDir ->
        match SiteGenerator.generate d (cfgWithSite siteJson) { GenerateOptions.defaults with OutputDir = outDir } with
        | Error e -> Assert.Fail e
        | Ok () -> check outDir)

let private run (siteJson: string) (check: string -> unit) = generateWith deps siteJson check

[<Fact>]
let ``no config renders the refreshed defaults`` () =
    run "{}" (fun out ->
        let index = readOut out "index.html"
        // top nav: every built-in page
        for url in [ "index.html"; "sources/index.html"; "tags/index.html"; "types/index.html"; "bundles/index.html" ] do
            Assert.Contains($"""<a href="{url}">""", index)
        // left = facets (filter mode on browse); no right content on a plain list page
        Assert.Contains("region-left", index)
        Assert.Contains("data-facet-mode=\"filter\"", index)
        Assert.DoesNotContain("region-right", index)
        // main = title + list, in the cards view
        Assert.Contains("card-grid", index)
        Assert.Contains("data-doc-list", index)
        // document page: facets on the left, toc on the right
        let doc = readOut out "files/src/ops_restart.md.html"
        Assert.Contains("data-facet-mode=\"links\"", doc)
        Assert.Contains("class=\"toc\"", doc)
        Assert.Contains("has-left has-right", doc))

[<Fact>]
let ``disabled slots leave no region`` () =
    run """{ "layout": { "left": false, "right": false } }""" (fun out ->
        let doc = readOut out "files/src/ops_restart.md.html"
        Assert.DoesNotContain("region-left", doc)
        Assert.DoesNotContain("region-right", doc)
        Assert.DoesNotContain("has-left", doc)
        Assert.DoesNotContain("class=\"toc\"", doc))

[<Fact>]
let ``top can list pages and links and drop the rest`` () =
    run """{ "layout": { "top": ["tags", { "label": "Wiki", "url": "https://wiki.example" }, { "page": "sources", "label": "Repos" }] } }"""
        (fun out ->
            let index = readOut out "index.html"
            Assert.Contains("""<a href="tags/index.html">Tags</a>""", index)
            Assert.Contains("""<a href="https://wiki.example" target="_blank" rel="noopener">Wiki</a>""", index)
            Assert.Contains("""<a href="sources/index.html">Repos</a>""", index)
            Assert.DoesNotContain("""<a href="types/index.html">""", index))

[<Fact>]
let ``a custom page lands at pages-id and filters documents`` () =
    run """{ "pages": { "ops": { "title": "Ops", "filter": { "type": ["runbook"] }, "view": "list" } } }""" (fun out ->
        Assert.True(existsOut out "pages/ops/index.html")
        let page = readOut out "pages/ops/index.html"
        Assert.Contains("Restart", page)
        Assert.DoesNotContain("Setup", page.Substring(page.IndexOf "data-doc-list"))
        Assert.Contains("class=\"doc-list\"", page)
        // relative asset prefix at depth 2
        Assert.Contains("../../assets/css/style.css", page)
        // custom pages follow the built-ins in the default nav
        Assert.Contains("""<a href="pages/ops/index.html">Ops</a>""", readOut out "index.html")
        Assert.Contains("""<a href="../../pages/ops/index.html">Ops</a>""", page))

[<Fact>]
let ``a custom page renders the table view with sort and limit`` () =
    run """{ "pages": { "all": { "filter": {}, "view": "table", "table": { "columns": ["title", "type"] },
                                   "sort": { "by": "title", "dir": "desc" }, "limit": 2 } } }""" (fun out ->
        let page = readOut out "pages/all/index.html"
        Assert.Contains("<table class=\"doc-table\">", page)
        let rows = page.Split("<tr data-doc").Length - 1
        Assert.Equal(2, rows)
        Assert.True(page.IndexOf "Setup" < page.IndexOf "Restart"))

[<Fact>]
let ``grouping renders one section per group`` () =
    run """{ "pages": { "g": { "filter": {}, "group": "type" } } }""" (fun out ->
        let page = readOut out "pages/g/index.html"
        Assert.Contains("data-doc-group", page)
        Assert.Contains("runbook", page)
        Assert.Contains("No type", page))

[<Fact>]
let ``an override keeps the built-in url and the detail pages`` () =
    run """{ "pages": { "tags": { "title": "Labels", "filter": { "tag": ["ops"] } } } }""" (fun out ->
        let page = readOut out "tags/index.html"
        Assert.Contains("Labels", page)
        Assert.Contains("Restart", page)
        Assert.DoesNotContain("Setup", page.Substring(page.IndexOf "data-doc-list"))
        Assert.True(existsOut out "tags/ops/index.html")
        Assert.True(existsOut out "tags/dev/index.html")
        Assert.Contains("""<a href="tags/index.html">Labels</a>""", readOut out "index.html"))

[<Fact>]
let ``a dropped built-in has no page, detail pages or nav link`` () =
    run """{ "pages": { "types": false } }""" (fun out ->
        Assert.False(existsOut out "types/index.html")
        Assert.False(existsOut out "types/runbook/index.html")
        let index = readOut out "index.html"
        Assert.DoesNotContain("types/index.html", index)
        Assert.DoesNotContain("types/runbook/index.html", index)
        Assert.True(existsOut out "tags/index.html"))

[<Fact>]
let ``facet links to a dropped page do not point at it`` () =
    run """{ "pages": { "tags": false } }""" (fun out ->
        let doc = readOut out "files/src/ops_restart.md.html"
        Assert.DoesNotContain("tags/ops/index.html", doc.Replace("href=\"#\"", ""))
        Assert.False(existsOut out "tags/ops/index.html"))

[<Fact>]
let ``home names the page written to index.html`` () =
    let local = Map.ofList [ "docs/welcome.md", "# Hello\n\n## Part\n\nwelcome text" ]
    generateWith (makeDeps sourceIndex content local)
        """{ "home": "welcome", "pages": { "welcome": { "title": "Welcome", "content": { "file": "docs/welcome.md" } } } }"""
        (fun out ->
            let index = readOut out "index.html"
            Assert.Contains("welcome text", index)
            Assert.Contains("assets/css/style.css", index)
            Assert.False(existsOut out "pages/welcome/index.html")
            // the browse page moves out of the way instead of vanishing
            Assert.True(existsOut out "browse/index.html")
            Assert.Contains("""<a href="index.html">Welcome</a>""", index)
            Assert.Contains("""<a href="browse/index.html">Browse</a>""", index))

[<Fact>]
let ``content pages render local markdown with a toc on the right`` () =
    let local = Map.ofList [ "docs/guide.md", "# Guide\n\n## Setup steps\n\ntext" ]
    generateWith (makeDeps sourceIndex content local)
        """{ "pages": { "guide": { "content": { "file": "docs/guide.md" } } } }""" (fun out ->
            let page = readOut out "pages/guide/index.html"
            Assert.Contains("markdown-body", page)
            Assert.Contains("href=\"#setup-steps\"", page)
            Assert.Contains("region-right", page))

[<Fact>]
let ``content docs render an indexed document at a chosen url`` () =
    run """{ "pages": { "runbook": { "title": "Runbook", "content": { "doc": "src:ops/restart.md" } } } }""" (fun out ->
        let page = readOut out "pages/runbook/index.html"
        Assert.Contains("run it", page))

[<Fact>]
let ``content with a filter shows both content and list`` () =
    let local = Map.ofList [ "docs/ops.md", "ops intro" ]
    generateWith (makeDeps sourceIndex content local)
        """{ "pages": { "ops": { "content": { "file": "docs/ops.md" }, "filter": { "tag": "ops" } } } }""" (fun out ->
            let page = readOut out "pages/ops/index.html"
            Assert.Contains("ops intro", page)
            Assert.Contains("Restart", page)
            Assert.True(page.IndexOf "ops intro" < page.IndexOf "data-doc-list"))

[<Fact>]
let ``links between content pages are rewritten to site urls`` () =
    let local = Map.ofList [ "docs/a.md", "see [b](b.md#part) and [web](https://example.com) and [doc](src:dev/setup.md)"; "docs/b.md", "bee" ]
    generateWith (makeDeps sourceIndex content local)
        """{ "pages": { "a": { "content": { "file": "docs/a.md" } }, "b": { "content": { "file": "docs/b.md" } } } }""" (fun out ->
            let page = readOut out "pages/a/index.html"
            Assert.Contains("href=\"../../pages/b/index.html#part\"", page)
            Assert.Contains("href=\"https://example.com\"", page)
            Assert.Contains("href=\"../../files/src/dev_setup.md.html\"", page))

let private expectError (d: Deps) (siteJson: string) : string =
    withOutDir (fun outDir ->
        match SiteGenerator.generate d (cfgWithSite siteJson) { GenerateOptions.defaults with OutputDir = outDir } with
        | Ok () -> failwith "expected generation to fail"
        | Error e ->
            // nothing is written when a page cannot be built, so a failed rebuild leaves the last good site alone
            Assert.False(existsOut outDir "index.html")
            e)

[<Fact>]
let ``a missing content file fails generation with a clear message`` () =
    let e = expectError deps """{ "pages": { "w": { "content": { "file": "docs/missing.md" } } } }"""
    Assert.Contains("site.pages.w.content.file 'docs/missing.md' does not exist", e)

[<Fact>]
let ``an uncached or unknown content doc fails generation`` () =
    Assert.Contains("is not cached", expectError deps """{ "pages": { "w": { "content": { "doc": "src:remote.md" } } } }""")
    Assert.Contains("does not match any indexed document", expectError deps """{ "pages": { "w": { "content": { "doc": "src:nope.md" } } } }""")

[<Fact>]
let ``config that escapes the repo root fails generation`` () =
    let cfg = { cfgWithSite "{}" with Site = { SiteSettings.empty with Pages = [ "w", Defined { Title = None; Content = Some (ContentFile "../x.md"); Filter = None; View = None; Sort = None; Group = None; Limit = None; Open = None; Layout = Layout.empty; CardsColumns = None; TableColumns = None; ListShow = None } ] } }
    withOutDir (fun outDir ->
        match SiteGenerator.generate deps cfg { GenerateOptions.defaults with OutputDir = outDir } with
        | Ok () -> Assert.Fail "expected failure"
        | Error e -> Assert.Contains("inside the repository", e))

[<Fact>]
let ``open resource links to the frontmatter url and index-only docs fall back to text`` () =
    run """{ "pages": { "all": { "filter": {}, "view": "list", "open": "resource" } } }""" (fun out ->
        let page = readOut out "pages/all/index.html"
        Assert.Contains("href=\"https://example.com/restart\"", page)
        Assert.Contains("href=\"../../files/src/dev_setup.md.html\"", page)
        Assert.Contains("Remote only", page)
        Assert.DoesNotContain(">Remote only</a>", page))

[<Fact>]
let ``open source links to the file in its repository`` () =
    run """{ "pages": { "all": { "filter": {}, "view": "list", "open": "source" } } }""" (fun out ->
        let page = readOut out "pages/all/index.html"
        Assert.Contains("https://github.com/example/src/blob/main/ops/restart.md", page))

[<Fact>]
let ``document rules give different documents different layouts`` () =
    run """{ "document": { "rules": [ { "match": { "type": ["runbook"] }, "layout": { "main": ["title", "content"], "right": false } } ] } }"""
        (fun out ->
            let runbook = readOut out "files/src/ops_restart.md.html"
            let guide = readOut out "files/src/dev_setup.md.html"
            // the guide keeps the default main (type badge via meta, breadcrumbs, cli tip)
            Assert.Contains("badge-type", guide)
            Assert.Contains("breadcrumbs", guide)
            Assert.Contains("class=\"toc\"", guide)
            // the runbook rule replaced main and right as whole slots
            Assert.DoesNotContain("badge-type", runbook)
            Assert.DoesNotContain("breadcrumbs", runbook)
            Assert.DoesNotContain("class=\"toc\"", runbook)
            Assert.Contains("markdown-body", runbook))

[<Fact>]
let ``document layout applies to every document page`` () =
    run """{ "document": { "layout": { "main": ["title", "tags", "content"], "left": false } } }""" (fun out ->
        let doc = readOut out "files/src/dev_setup.md.html"
        Assert.DoesNotContain("region-left", doc)
        Assert.DoesNotContain("breadcrumbs", doc)
        Assert.Contains("#dev", doc))

[<Fact>]
let ``a page layout replaces a slot for that page only`` () =
    run """{ "pages": { "ops": { "filter": {}, "layout": { "left": false } } } }""" (fun out ->
        Assert.DoesNotContain("region-left", readOut out "pages/ops/index.html")
        Assert.Contains("region-left", readOut out "index.html"))

[<Fact>]
let ``site default view and card columns apply to browse`` () =
    run """{ "defaultView": "table", "cards": { "columns": 2 } }""" (fun out ->
        Assert.Contains("doc-table", readOut out "index.html"))
    run """{ "cards": { "columns": 2 } }""" (fun out ->
        Assert.Contains("--cols: 2", readOut out "index.html"))

[<Fact>]
let ``browse documents carry the filter contract and facets become filters`` () =
    run "{}" (fun out ->
        let index = readOut out "index.html"
        Assert.Contains("data-doc ", index)
        Assert.Contains("data-type=", index)
        for facet in [ "source"; "ext"; "tags"; "type" ] do
            Assert.Contains($"data-facet=\"{facet}\"", index)
        Assert.Contains("data-doc-count", index)
        let js = readOut out "js/app.js"
        Assert.Contains("[data-doc]", js)
        Assert.DoesNotContain("source-filters", js)
        Assert.DoesNotContain(".file-card", js))

[<Fact>]
let ``detail pages keep breadcrumbs and cli tips`` () =
    run "{}" (fun out ->
        let tag = readOut out "tags/ops/index.html"
        Assert.Contains("breadcrumbs", tag)
        Assert.Contains("eru add --tag ops", tag)
        Assert.Contains("../../assets/css/style.css", tag))

[<Fact>]
let ``referenced content files are listed for serve to watch`` () =
    let cfg = cfgWithSite """{ "pages": { "a": { "content": { "file": "./docs/a.md" } }, "b": { "content": { "doc": "src:x.md" } } } }"""
    Assert.Equal<string list>([ "docs/a.md" ], SiteGenerator.referencedContentFiles cfg)

[<Fact>]
let ``document pages open with the graph and end with the related lists`` () =
    let index =
        sourceIndex
        |> Map.add "ops/backup.md" (makeEntry (Some "files/h3") "Backup")
        |> Map.map (fun _ e -> { e with LocalPath = Some "unused" })
    let linked = Map.ofList [ "files/h1", "# Restart\n\nsee [backup](backup.md)"; "files/h2", "# Setup"; "files/h3", "# Backup" ]
    generateWith (makeDeps index linked Map.empty) "{}" (fun out ->
        let doc = readOut out "files/src/ops_restart.md.html"
        let graph = doc.IndexOf "id=\"doc-graph\""
        let title = doc.IndexOf "widget-title"
        let content = doc.IndexOf "widget-content"
        let related = doc.IndexOf "widget-related"
        Assert.True(graph >= 0 && graph < title, "graph should come first")
        Assert.True(related > content, "related should come after the content")
        // the right column holds only the toc, and the hidden graph carries no link list, so the lists appear once
        Assert.Equal(1, doc.Split("widget-related").Length - 1)
        Assert.Equal(1, doc.Split("Links from this document").Length - 1)
        Assert.Contains("class=\"doc-graph\" hidden", doc)
        Assert.Contains("section.hidden = false", readOut out "js/graph.js"))
