module Eru.Tests.OkfVerifyTests

open System
open System.Collections.Generic
open Xunit
open Eru

let private at = DateTimeOffset(2026, 7, 1, 16, 0, 0, TimeSpan.Zero)
let private entry = "  - { by: \"human:me@x\", at: \"2026-07-01T16:00:00Z\" }\n"

let private run (file: string) (content: string) (by: string) (dryRun: bool) =
    let writes = Dictionary<string, string>()
    let deps = OkfFixTests.makeDeps (Map.ofList [ file, content ]) writes
    let cmd : OkfVerify.Command = { Path = "/bundle/" + file; By = by; At = at; DryRun = dryRun }
    OkfVerify.execute deps cmd, writes

let private ok file content =
    match run file content "human:me@x" false with
    | Ok r, writes -> r, writes.[file]
    | Error e, _ -> failwith e

[<Fact>]
let ``adds verified when absent preserving other keys`` () =
    let _, out = ok "a.md" "---\ntype: table\ntags: [a]\n---\nbody\n"
    Assert.Equal("---\ntype: table\ntags: [a]\nverified:\n" + entry + "---\nbody\n", out)

[<Fact>]
let ``appends to an existing list`` () =
    let old = "  - { by: bot/1, at: \"2026-06-01T00:00:00Z\" }\n"
    let _, out = ok "a.md" ("---\ntype: table\nverified:\n" + old + "---\nbody\n")
    Assert.Equal("---\ntype: table\nverified:\n  - { by: \"bot/1\", at: \"2026-06-01T00:00:00Z\" }\n" + entry + "---\nbody\n", out)

[<Fact>]
let ``normalizes a bare mapping to a list`` () =
    let _, out = ok "a.md" "---\ntype: table\nverified: { by: bot/1, at: 2026-06-01T00:00:00Z }\n---\nbody\n"
    Assert.Equal("---\ntype: table\nverified:\n  - { by: \"bot/1\", at: \"2026-06-01T00:00:00Z\" }\n" + entry + "---\nbody\n", out)

[<Fact>]
let ``replaces a malformed scalar and reports it`` () =
    let r, out = ok "a.md" "---\ntype: table\nverified: unknown\nstatus: draft\n---\nbody\n"
    Assert.True r.ReplacedInvalid
    Assert.Equal("---\ntype: table\nverified:\n" + entry + "status: draft\n---\nbody\n", out)

[<Fact>]
let ``keys after a list are preserved`` () =
    let _, out = ok "a.md" "---\ntype: table\nverified:\n- { by: bot/1, at: 2026-06-01T00:00:00Z }\nstatus: draft\n---\nbody\n"
    Assert.Contains("status: draft\n---\nbody\n", out)
    Assert.Contains("\"human:me@x\"", out)

[<Fact>]
let ``crlf line endings are kept`` () =
    let _, out = ok "a.md" "---\r\ntype: table\r\n---\r\nbody\r\n"
    Assert.Equal("---\r\ntype: table\r\nverified:\r\n  - { by: \"human:me@x\", at: \"2026-07-01T16:00:00Z\" }\r\n---\r\nbody\r\n", out)

[<Fact>]
let ``dry run writes nothing`` () =
    match run "a.md" "---\ntype: table\n---\nbody\n" "human:me@x" true with
    | Ok r, writes ->
        Assert.True r.DryRun
        Assert.Empty writes
    | Error e, _ -> failwith e

[<Fact>]
let ``errors without frontmatter or type`` () =
    Assert.True(match run "a.md" "body\n" "human:me@x" false with Error _, _ -> true | _ -> false)
    Assert.True(match run "a.md" "---\ndescription: x\n---\nbody\n" "human:me@x" false with Error _, _ -> true | _ -> false)

[<Fact>]
let ``errors for non-concept files`` () =
    Assert.True(match run "index.md" "---\ntype: x\n---\n" "human:me@x" false with Error _, _ -> true | _ -> false)

[<Fact>]
let ``rejects placeholder and empty actors`` () =
    for by in [ ""; "unknown"; "false"; "human:" ] do
        Assert.True(match run "a.md" "---\ntype: table\n---\n" by false with Error _, _ -> true | _ -> false)

[<Fact>]
let ``formats non-UTC offsets`` () =
    Assert.Equal("2026-07-01T16:00:00+02:00", OkfVerify.formatAt (DateTimeOffset(2026, 7, 1, 16, 0, 0, TimeSpan.FromHours 2.0)))
