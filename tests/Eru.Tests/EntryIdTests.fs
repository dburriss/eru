module Eru.Tests.EntryIdTests

open Xunit
open Eru

[<Fact>]
let ``toString formats as source:remotePath`` () =
    let id = { Source = "main"; RemotePath = "docs/guide.md" }
    Assert.Equal("main:docs/guide.md", EntryId.toString id)

[<Fact>]
let ``tryParse round-trips toString output`` () =
    let id = { Source = "main"; RemotePath = "docs/guide.md" }
    Assert.Equal(Some id, EntryId.toString id |> EntryId.tryParse)

[<Fact>]
let ``tryParse splits on the first colon only`` () =
    let parsed = EntryId.tryParse "main:docs/a:b.md"
    Assert.Equal(Some { Source = "main"; RemotePath = "docs/a:b.md" }, parsed)

[<Fact>]
let ``tryParse returns None when there is no colon`` () =
    Assert.Equal(None, EntryId.tryParse "no-colon-here")
