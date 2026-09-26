module Eru.Tests.IndexBlendTests

open Xunit
open Eru

let private c tags desc : Contribution = { Tags = tags; Description = desc }

[<Fact>]
let ``blend unions tags across all contributions`` () =
    let contributions =
        Map.ofList [
            ContributionKey.manifest "", c ["dotnet"] None
            ContributionKey.frontmatter, c ["architecture"] None
        ]
    let result = IndexBlend.blend [] "adr.md" contributions
    Assert.Equal<Set<string>>(Set.ofList ["dotnet"; "architecture"], Set.ofList result.Tags)
    Assert.Equal(2, result.Tags.Length)

[<Fact>]
let ``blend deduplicates tags shared across contributions`` () =
    let contributions =
        Map.ofList [
            ContributionKey.manifest "", c ["dotnet"; "shared"] None
            ContributionKey.frontmatter, c ["shared"; "architecture"] None
        ]
    let result = IndexBlend.blend [] "adr.md" contributions
    Assert.Equal<Set<string>>(Set.ofList ["dotnet"; "shared"; "architecture"], Set.ofList result.Tags)
    Assert.Equal(3, result.Tags.Length)

[<Fact>]
let ``blend tag union is additive even when a contribution key is removed`` () =
    // Simulates the staleness-bug fix: once the "frontmatter" key is replaced with
    // fewer tags (not merged), the union naturally reflects the drop.
    let contributions = Map.ofList [ ContributionKey.frontmatter, c ["kept"] None ]
    let result = IndexBlend.blend [] "adr.md" contributions
    Assert.Equal<string list>(["kept"], result.Tags)
    Assert.DoesNotContain("dropped", result.Tags)

[<Fact>]
let ``blend description prefers the most specific covering Manifest bundle`` () =
    let bundles = [ { Path = ""; Kind = Manifest }; { Path = "docs"; Kind = Manifest } ]
    let contributions =
        Map.ofList [
            ContributionKey.manifest "", c [] (Some "root description")
            ContributionKey.manifest "docs", c [] (Some "docs description")
        ]
    let result = IndexBlend.blend bundles "docs/adr.md" contributions
    Assert.Equal(Some "docs description", result.Description)

[<Fact>]
let ``blend description falls back to a less specific bundle when the specific one has none`` () =
    let bundles = [ { Path = ""; Kind = Manifest }; { Path = "docs"; Kind = Manifest } ]
    let contributions =
        Map.ofList [
            ContributionKey.frontmatter, c [] (Some "frontmatter description")
        ]
    let result = IndexBlend.blend bundles "docs/adr.md" contributions
    Assert.Equal(Some "frontmatter description", result.Description)

[<Fact>]
let ``blend description falls back to frontmatter when no Manifest bundle covers the path`` () =
    let bundles = [ { Path = "docs"; Kind = Okf } ]
    let contributions =
        Map.ofList [
            ContributionKey.frontmatter, c [] (Some "frontmatter description")
        ]
    let result = IndexBlend.blend bundles "docs/adr.md" contributions
    Assert.Equal(Some "frontmatter description", result.Description)

[<Fact>]
let ``blend with zero bundles falls back to frontmatter description`` () =
    let contributions =
        Map.ofList [
            ContributionKey.frontmatter, c ["dotnet"] (Some "only frontmatter")
        ]
    let result = IndexBlend.blend [] "adr.md" contributions
    Assert.Equal(Some "only frontmatter", result.Description)
    Assert.Equal<string list>(["dotnet"], result.Tags)

[<Fact>]
let ``blend with zero contributions returns empty tags and no description`` () =
    let result = IndexBlend.blend [] "adr.md" Map.empty
    Assert.Empty result.Tags
    Assert.Equal(None, result.Description)

[<Fact>]
let ``blend longest-matching bundle path wins over a shorter covering bundle`` () =
    let bundles = [ { Path = ""; Kind = Manifest }; { Path = "docs"; Kind = Manifest }; { Path = "docs/knowledge"; Kind = Manifest } ]
    let contributions =
        Map.ofList [
            ContributionKey.manifest "", c [] (Some "root")
            ContributionKey.manifest "docs", c [] (Some "docs")
            ContributionKey.manifest "docs/knowledge", c [] (Some "docs/knowledge")
        ]
    let result = IndexBlend.blend bundles "docs/knowledge/adr.md" contributions
    Assert.Equal(Some "docs/knowledge", result.Description)
