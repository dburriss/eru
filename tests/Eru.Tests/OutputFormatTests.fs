module Eru.Tests.OutputFormatTests

open System.IO
open Xunit
open Spectre.Console
open Eru.Cli.OutputFormat

let private renderToString (t: Table) : string =
    use writer = new StringWriter()
    let console =
        AnsiConsole.Create(AnsiConsoleSettings(Out = AnsiConsoleOutput(writer), Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors))
    console.Profile.Width <- 200
    console.Write(t)
    writer.ToString()

[<Fact>]
let ``table cells containing square brackets render literally`` () =
    let t = makeTable ["Status"; "Path"; "Rule"; "Message"]
    t.AddRowEscaped("⚠", "a[1]/note.md", "broken-link", "link '[Go here](missing/README.md)' -> 'missing/README.md' not found") |> ignore
    let output = renderToString t
    Assert.Contains("[Go here](missing/README.md)", output)
    Assert.Contains("a[1]/note.md", output)

[<Fact>]
let ``AddRowEscaped accepts an array of cells`` () =
    let t = makeTable ["A"; "B"]
    t.AddRowEscaped([| "[x]"; "[https://x.y]" |]) |> ignore
    let output = renderToString t
    Assert.Contains("[x]", output)
    Assert.Contains("[https://x.y]", output)
