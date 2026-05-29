module Eru.Tests.FrontmatterTests

open Xunit
open Eru

[<Fact>]
let ``empty string returns empty`` () =
    let result = Frontmatter.parse ""
    Assert.Equal(None, result.Description)
    Assert.Empty(result.Tags)

[<Fact>]
let ``no frontmatter block returns empty`` () =
    let result = Frontmatter.parse "# Heading\n\nSome content here."
    Assert.Equal(None, result.Description)
    Assert.Empty(result.Tags)

[<Fact>]
let ``unclosed frontmatter returns empty`` () =
    let result = Frontmatter.parse "---\ndescription: orphaned\ntags: [a]\n"
    Assert.Equal(None, result.Description)
    Assert.Empty(result.Tags)

[<Fact>]
let ``empty frontmatter block returns empty`` () =
    let result = Frontmatter.parse "---\n---\n# Content"
    Assert.Equal(None, result.Description)
    Assert.Empty(result.Tags)

[<Fact>]
let ``description only`` () =
    let result = Frontmatter.parse "---\ndescription: A shared utility\n---\n# Content"
    Assert.Equal(Some "A shared utility", result.Description)
    Assert.Empty(result.Tags)

[<Fact>]
let ``description with double quotes`` () =
    let result = Frontmatter.parse """---
description: "A quoted description"
---
"""
    Assert.Equal(Some "A quoted description", result.Description)

[<Fact>]
let ``tags inline list`` () =
    let result = Frontmatter.parse "---\ntags: [dotnet, logging, utils]\n---\n"
    Assert.Equal<string list>(["dotnet"; "logging"; "utils"], result.Tags)
    Assert.Equal(None, result.Description)

[<Fact>]
let ``tags inline list with quoted items`` () =
    let result = Frontmatter.parse """---
tags: ["dotnet", "logging"]
---
"""
    Assert.Equal<string list>(["dotnet"; "logging"], result.Tags)

[<Fact>]
let ``tags block list`` () =
    let content = "---\ntags:\n  - dotnet\n  - logging\n---\n"
    let result = Frontmatter.parse content
    Assert.Equal<string list>(["dotnet"; "logging"], result.Tags)

[<Fact>]
let ``tags block list with quoted items`` () =
    let content = "---\ntags:\n  - \"dotnet\"\n  - 'logging'\n---\n"
    let result = Frontmatter.parse content
    Assert.Equal<string list>(["dotnet"; "logging"], result.Tags)

[<Fact>]
let ``both description and inline tags`` () =
    let content = "---\ndescription: Logging helpers\ntags: [dotnet, utils]\n---\n# Doc"
    let result = Frontmatter.parse content
    Assert.Equal(Some "Logging helpers", result.Description)
    Assert.Equal<string list>(["dotnet"; "utils"], result.Tags)

[<Fact>]
let ``both description and block tags`` () =
    let content = "---\ndescription: Logging helpers\ntags:\n  - dotnet\n  - utils\n---\n"
    let result = Frontmatter.parse content
    Assert.Equal(Some "Logging helpers", result.Description)
    Assert.Equal<string list>(["dotnet"; "utils"], result.Tags)

[<Fact>]
let ``CRLF line endings are handled`` () =
    let content = "---\r\ndescription: Windows style\r\ntags: [a, b]\r\n---\r\n"
    let result = Frontmatter.parse content
    Assert.Equal(Some "Windows style", result.Description)
    Assert.Equal<string list>(["a"; "b"], result.Tags)

// Merge rules (applied by callers, tested here for documentation)

[<Fact>]
let ``configured description takes precedence over frontmatter`` () =
    let fm = Frontmatter.parse "---\ndescription: from file\n---\n"
    let configuredDesc = Some "from config"
    let effective = configuredDesc |> Option.orElse fm.Description
    Assert.Equal(Some "from config", effective)

[<Fact>]
let ``frontmatter description used when no configured description`` () =
    let fm = Frontmatter.parse "---\ndescription: from file\n---\n"
    let configuredDesc : string option = None
    let effective = configuredDesc |> Option.orElse fm.Description
    Assert.Equal(Some "from file", effective)

[<Fact>]
let ``tags merge deduplicates`` () =
    let fm = Frontmatter.parse "---\ntags: [dotnet, logging]\n---\n"
    let configuredTags = ["logging"; "extra"]
    let merged = (configuredTags @ fm.Tags) |> List.distinct
    Assert.Equal<string list>(["logging"; "extra"; "dotnet"], merged)
