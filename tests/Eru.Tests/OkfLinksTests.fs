module Eru.Tests.OkfLinksTests

open Xunit
open Eru

let private kinds (links: OkfLinks.Link list) = links |> List.map (fun l -> l.Kind, l.Target)

[<Fact>]
let ``extracts page, image and wikilink references`` () =
    let links = OkfLinks.extract "See [a](a.md), ![pic](img/p.png) and [[Orders]] and [[Orders|the orders]]."
    Assert.Equal<(OkfLinks.Kind * string) list>(
        [ OkfLinks.Wiki, "Orders"; OkfLinks.Wiki, "Orders"; OkfLinks.Page, "a.md"; OkfLinks.Image, "img/p.png" ] |> List.sort,
        kinds links |> List.sort)

[<Fact>]
let ``records line number and verbatim text`` () =
    let links = OkfLinks.extract "line one\n\nsee [text](foo.md) here\n"
    let l = Assert.Single links
    Assert.Equal(3, l.Line)
    Assert.Equal("[text](foo.md)", l.Text)

[<Fact>]
let ``splits fragment and query and decodes percent escapes`` () =
    let links = OkfLinks.extract "[a](my%20note.md#Setup) [b](other.md?x=1#top) [[Note#Heading]]"
    let find t = links |> List.find (fun l -> l.Target = t)
    Assert.Equal(Some "Setup", (find "my note.md").Fragment)
    Assert.Equal(Some "top", (find "other.md").Fragment)
    Assert.Equal(Some "Heading", (find "Note").Fragment)

[<Fact>]
let ``skips external, mailto, anchor-only and protocol-relative links`` () =
    let text = "[a](https://x.test) [b](mailto:me@x.test) [c](#local) [d](//cdn.test/x.js) [e](ftp://h/f)"
    Assert.Empty(OkfLinks.extract text)

[<Fact>]
let ``ignores links in fenced code, inline code and frontmatter`` () =
    let text = "---\ntitle: \"[fm](fm.md)\"\n---\n`[inline](inline.md)`\n```\n[fenced](fenced.md)\n```\n~~~md\n[tilde](tilde.md)\n~~~\n[real](real.md)\n"
    let links = OkfLinks.extract text
    let l = Assert.Single links
    Assert.Equal("real.md", l.Target)
    Assert.Equal(11, l.Line)

[<Fact>]
let ``image nested in a link is a separate reference`` () =
    let links = OkfLinks.extract "[![badge](b.png)](page.md)"
    Assert.Equal<(OkfLinks.Kind * string) list>(
        [ OkfLinks.Page, "page.md"; OkfLinks.Image, "b.png" ] |> List.sort,
        kinds links |> List.sort)

[<Fact>]
let ``obsidian embed is not treated as a note reference`` () =
    Assert.Empty(OkfLinks.extract "![[diagram.png]]")

[<Fact>]
let ``link title is not part of the target`` () =
    let l = OkfLinks.extract "[a](a.md \"A title\")" |> List.exactlyOne
    Assert.Equal("a.md", l.Target)

[<Fact>]
let ``github and markdig slugs of a heading`` () =
    Assert.Equal("hello-world", OkfLinks.githubSlug "Hello, World!")
    Assert.Equal("hello-world", OkfLinks.markdigSlug "Hello, World!")
    Assert.Equal("v1.2-notes", OkfLinks.markdigSlug "v1.2 Notes")
    Assert.Equal("v12-notes", OkfLinks.githubSlug "v1.2 Notes")
    Assert.Equal("a-b", OkfLinks.githubSlug "`a` **b**")

[<Fact>]
let ``heading anchors accept either style and ignore code fences`` () =
    let anchors = OkfLinks.headingAnchors "# Title\n## v1.2 Notes\n```\n# not a heading\n```\n"
    Assert.True(OkfLinks.anchorExists anchors "title")
    Assert.True(OkfLinks.anchorExists anchors "v1.2-notes")
    Assert.True(OkfLinks.anchorExists anchors "v12-notes")
    Assert.False(OkfLinks.anchorExists anchors "not-a-heading")

[<Fact>]
let ``repeated heading suffix is accepted`` () =
    let anchors = OkfLinks.headingAnchors "## Setup\n## Setup\n"
    Assert.True(OkfLinks.anchorExists anchors "setup-1")
    Assert.False(OkfLinks.anchorExists anchors "teardown-1")
