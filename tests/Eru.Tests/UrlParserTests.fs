module Eru.Tests.UrlParserTests

open Xunit
open Eru

[<Fact>]
let ``GitHub blob URL is parsed correctly`` () =
    let url = "https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md"
    match UrlParser.tryParse url with
    | None -> Assert.Fail "Expected Some but got None"
    | Some p ->
        Assert.Equal("https://github.com/dburriss/orcai", p.RepoUrl)
        Assert.Equal("main", p.Branch)
        Assert.Equal("knowledge/github-cli.md", p.RemotePath)
        Assert.Equal("orcai", p.SourceName)

[<Fact>]
let ``GitLab blob URL is parsed correctly`` () =
    let url = "https://gitlab.com/dburriss/orcai/-/blob/main/knowledge/github-cli.md"
    match UrlParser.tryParse url with
    | None -> Assert.Fail "Expected Some but got None"
    | Some p ->
        Assert.Equal("https://gitlab.com/dburriss/orcai", p.RepoUrl)
        Assert.Equal("main", p.Branch)
        Assert.Equal("knowledge/github-cli.md", p.RemotePath)
        Assert.Equal("orcai", p.SourceName)

[<Fact>]
let ``Non-URL path returns None`` () =
    Assert.Equal(None, UrlParser.tryParse "knowledge/foo.md")

[<Fact>]
let ``Unsupported HTTPS provider returns None`` () =
    Assert.Equal(None, UrlParser.tryParse "https://bitbucket.org/owner/repo/src/main/file.md")
