module Eru.Tests.SemVerTests

open Xunit
open Eru

[<Theory>]
[<InlineData("0.15.2", "0.15.1", true)>]
[<InlineData("0.15.1", "0.15.1", false)>]
[<InlineData("0.15.1", "0.15.2", false)>]
[<InlineData("0.16.0", "0.15.9", true)>]
[<InlineData("1.0.0", "0.99.99", true)>]
[<InlineData("0.10.0", "0.9.0", true)>]                 // numeric, not lexical
[<InlineData("0.15.2-alpha", "0.15.1", true)>]          // an alpha of the next patch is newer than the last release
[<InlineData("0.15.2-alpha", "0.15.2", false)>]         // a prerelease sorts below its release
[<InlineData("0.15.2", "0.15.2-alpha", true)>]
[<InlineData("0.15.2-alpha.2", "0.15.2-alpha.1", true)>]
[<InlineData("0.15.2-alpha.10", "0.15.2-alpha.9", true)>]
[<InlineData("0.15.2-alpha.20261107143005", "0.15.2-alpha.20261107120000", true)>]   // install-alpha timestamps
[<InlineData("0.15.2-alpha.20261107120000", "0.15.2-alpha.20261107143005", false)>]
[<InlineData("0.15.2-alpha.20261107143005", "0.15.2-alpha.20261107143005", false)>]
[<InlineData("0.15.2-alpha.20261107143005", "0.15.2-alpha", true)>]                  // an old un-stamped alpha is older
[<InlineData("0.15.3-alpha.20261107120000", "0.15.2-alpha.20261107143005", true)>]
[<InlineData("0.15.2+abc", "0.15.2+def", false)>]       // build metadata is ignored
[<InlineData("0.15.2+abc", "0.15.1", true)>]
let ``isNewer compares eru versions`` (candidate: string, current: string, expected: bool) =
    Assert.Equal(expected, SemVer.isNewer candidate current)

[<Theory>]
[<InlineData("unknown", "0.15.1")>]
[<InlineData("0.15.1", "unknown")>]
[<InlineData("", "0.15.1")>]
[<InlineData("1.2", "0.15.1")>]
let ``an unparsable version is never newer`` (candidate: string, current: string) =
    Assert.False(SemVer.isNewer candidate current)

[<Fact>]
let ``StaleWriter recognises its own message`` () =
    let m = StaleWriter.message "0.16.0" "0.15.1"
    Assert.True(StaleWriter.isStale m)
    Assert.False(StaleWriter.isStale "disk full")
