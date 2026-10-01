module Eru.Tests.VersionCliTests

open Xunit
open Eru.Cli.VersionCli

[<Fact>]
let ``parseVersion splits version and commit`` () =
    Assert.Equal(("0.8.0", Some "abc123"), parseVersion "0.8.0+abc123")

[<Fact>]
let ``parseVersion keeps prerelease suffix`` () =
    Assert.Equal(("0.10.6-alpha.1", Some "f9cca04"), parseVersion "0.10.6-alpha.1+f9cca04")

[<Fact>]
let ``parseVersion without commit returns None`` () =
    Assert.Equal(("0.8.0", None), parseVersion "0.8.0")
