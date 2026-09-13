module Eru.Tests.MarkdownLinkAdapterTests

open Xunit
open Eru
open Eru.Adapters.MarkdownLinkAdapter

[<Fact>]
let ``extracts wikilink without description`` () =
    let links = extractLinks "see [[guide]] for details"
    Assert.Contains(links, fun l -> l.Kind = Wikilink && l.Target = "guide" && l.Description = None)

[<Fact>]
let ``extracts wikilink with description`` () =
    let links = extractLinks "see [[guide|See the guide]] for details"
    Assert.Contains(links, fun l -> l.Kind = Wikilink && l.Target = "guide" && l.Description = Some "See the guide")

[<Fact>]
let ``wikilink-looking text inside fenced code block is not extracted`` () =
    let content = "before\n```\n[[not a link]]\n```\nafter"
    let links = extractLinks content
    Assert.DoesNotContain(links, fun l -> l.Kind = Wikilink)

[<Fact>]
let ``wikilink-looking text inside inline code is not extracted`` () =
    let content = "use `[[not a link]]` syntax"
    let links = extractLinks content
    Assert.DoesNotContain(links, fun l -> l.Kind = Wikilink)

[<Fact>]
let ``markdown link extracts target and description for internal path`` () =
    let links = extractLinks "[See guide](guide.md)"
    Assert.Contains(links, fun l -> l.Kind = MarkdownLink && l.Target = "guide.md" && l.Description = Some "See guide")

[<Fact>]
let ``markdown link extracts target and description for external url`` () =
    let links = extractLinks "[Example site](https://example.com)"
    Assert.Contains(links, fun l -> l.Kind = MarkdownLink && l.Target = "https://example.com" && l.Description = Some "Example site")

[<Fact>]
let ``markdown link with no link text has no description`` () =
    let links = extractLinks "[](guide.md)"
    Assert.Contains(links, fun l -> l.Kind = MarkdownLink && l.Target = "guide.md" && l.Description = None)

[<Fact>]
let ``mixed content extracts both markdown links and wikilinks`` () =
    let content = "[guide](guide.md) and [[other]] and [[other|Other doc]]"
    let links = extractLinks content
    Assert.Equal(3, links.Length)
    Assert.Contains(links, fun l -> l.Kind = MarkdownLink && l.Target = "guide.md" && l.Description = Some "guide")
    Assert.Contains(links, fun l -> l.Kind = Wikilink && l.Target = "other" && l.Description = None)
    Assert.Contains(links, fun l -> l.Kind = Wikilink && l.Target = "other" && l.Description = Some "Other doc")
