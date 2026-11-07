module Eru.Tests.ThemeTests

open Xunit
open Eru
open Eru.Site
open Eru.Tests.SiteTestSupport

let private sourceIndex =
    Map.ofList [ "dev/setup.md", { makeEntry (Some "files/h2") "Setup" with Type = Some "guide"; Tags = [ "dev" ]; Description = Some "Install it" } ]

let private content = Map.ofList [ "files/h2", "# Setup\n\n## Install\n\ngo" ]

let private depsWith (files: (string * string) list) = makeDeps sourceIndex content (Map.ofList files)

let private generate (d: Deps) (siteJson: string) (check: string -> unit) =
    withOutDir (fun outDir ->
        match SiteGenerator.generate d (cfgWithSite siteJson) { SiteGenerator.GenerateOptions.defaults with OutputDir = outDir } with
        | Error e -> Assert.Fail e
        | Ok () -> check outDir)

let private generateError (d: Deps) (siteJson: string) : string =
    withOutDir (fun outDir ->
        match SiteGenerator.generate d (cfgWithSite siteJson) { SiteGenerator.GenerateOptions.defaults with OutputDir = outDir } with
        | Error e -> e
        | Ok () -> failwith "expected generation to fail")

let private fooTheme =
    [ ".eru/themes/foo/theme.css", "body { color: hotpink; }"
      ".eru/themes/foo/theme.json", """{ "description": "Foo", "site": { "defaultView": "cards", "layout": { "left": ["facets"] } } }""" ]

[<Fact>]
let ``terminal is the default and adds no css of its own`` () =
    generate (depsWith []) "{}" (fun out ->
        let css = readOut out "assets/css/style.css"
        Assert.DoesNotContain("ledger theme", css)
        Assert.Contains(":root:where(.theme-dark)", css))

[<Fact>]
let ``a built-in theme css follows the base stylesheet and precedes custom css`` () =
    generate (depsWith []) """{ "theme": "ledger" }""" (fun out ->
        let css = readOut out "assets/css/style.css"
        let base' = css.IndexOf ":root:where(.theme-dark)"
        let ledger = css.IndexOf "/* ledger theme */"
        Assert.True(base' >= 0 && ledger > base', "ledger css must come after the base stylesheet")
        // the ledger palette repeats the toggle-class selectors so it wins in light and dark
        Assert.True(css.LastIndexOf ":root:where(.theme-dark)" > ledger)
        Assert.Contains("#4f46e5", css)
        Assert.DoesNotContain("ledger theme", readOut out "assets/css/custom.css"))

[<Fact>]
let ``ledger defaults to the rows view with a facet sidebar`` () =
    generate (depsWith []) """{ "theme": "ledger" }""" (fun out ->
        let index = readOut out "index.html"
        Assert.Contains("doc-rows", index)
        Assert.Contains("doc-row-side", index)
        Assert.Contains("data-facet-mode=\"filter\"", index)
        // the row carries the filter contract and the description under the name
        Assert.Contains("data-doc ", index)
        Assert.Contains("cell-desc", index))

[<Fact>]
let ``the user's config wins over a theme's defaults`` () =
    generate (depsWith []) """{ "theme": "ledger", "defaultView": "table" }""" (fun out ->
        let index = readOut out "index.html"
        Assert.Contains("doc-table", index)
        Assert.DoesNotContain("<ul class=\"doc-rows\">", index))

[<Fact>]
let ``a project theme adds its css and default config`` () =
    generate (depsWith fooTheme) """{ "theme": "foo" }""" (fun out ->
        let css = readOut out "assets/css/style.css"
        Assert.EndsWith("body { color: hotpink; }", css.TrimEnd())
        let index = readOut out "index.html"
        Assert.Contains("card-grid", index)
        Assert.Contains("data-facet-mode=\"filter\"", index))

[<Fact>]
let ``a theme may ship css only or config only`` () =
    let cssOnly = [ ".eru/themes/c/theme.css", "p { margin: 0; }" ]
    let jsonOnly = [ ".eru/themes/j/theme.json", """{ "site": { "defaultView": "cards" } }""" ]
    generate (depsWith cssOnly) """{ "theme": "c" }""" (fun out ->
        Assert.Contains("p { margin: 0; }", readOut out "assets/css/style.css"))
    generate (depsWith jsonOnly) """{ "theme": "j" }""" (fun out ->
        Assert.Contains("card-grid", readOut out "index.html"))

[<Fact>]
let ``an unknown theme is rejected and the message lists what is available`` () =
    let msg = generateError (depsWith []) """{ "theme": "nope" }"""
    Assert.Contains("unknown theme 'nope'", msg)
    Assert.Contains("terminal", msg)
    Assert.Contains("ledger", msg)

[<Fact>]
let ``a folder with neither theme file is not a theme`` () =
    let msg = generateError (depsWith [ ".eru/themes/empty/notes.txt", "x" ]) """{ "theme": "empty" }"""
    Assert.Contains("unknown theme 'empty'", msg)

[<Fact>]
let ``a malformed theme json names the theme`` () =
    let msg = generateError (depsWith [ ".eru/themes/bad/theme.json", "{ not json" ]) """{ "theme": "bad" }"""
    Assert.Contains("theme 'bad'", msg)
    Assert.Contains("theme.json", msg)

[<Fact>]
let ``theme defaults go through the same shape checks as site config`` () =
    let files = [ ".eru/themes/bad/theme.json", """{ "site": { "layout": { "left": ["nope"] }, "cards": { "colums": 2 } } }""" ]
    let msg = generateError (depsWith files) """{ "theme": "bad" }"""
    Assert.Contains("theme 'bad'", msg)

[<Fact>]
let ``matrix css follows the base stylesheet and precedes custom css`` () =
    generate (depsWith []) """{ "theme": "matrix" }""" (fun out ->
        let css = readOut out "assets/css/style.css"
        let base' = css.IndexOf ":root:where(.theme-dark)"
        let matrix = css.IndexOf "/* matrix theme */"
        Assert.True(base' >= 0 && matrix > base', "matrix css must come after the base stylesheet")
        Assert.True(css.LastIndexOf ":root:where(.theme-dark)" > matrix)
        Assert.Contains("#3dff6e", css)
        Assert.Contains("repeating-linear-gradient", css)
        // scanlines are dark-mode only: never an unscoped rule
        Assert.DoesNotContain("\n.site-nav, .region-main {", css)
        Assert.DoesNotContain("matrix theme", readOut out "assets/css/custom.css"))

[<Fact>]
let ``matrix keeps the terminal layout defaults`` () =
    match Themes.resolve (depsWith []) "matrix" with
    | Ok t ->
        Assert.Equal(Themes.BuiltIn, t.Origin)
        Assert.Equal(Themes.resolve (depsWith []) "terminal" |> Result.map (fun x -> x.Defaults), Ok t.Defaults)
    | Error e -> failwith e

[<Fact>]
let ``smiley css follows the base stylesheet and precedes custom css`` () =
    generate (depsWith []) """{ "theme": "smiley" }""" (fun out ->
        let css = readOut out "assets/css/style.css"
        let base' = css.IndexOf ":root:where(.theme-dark)"
        let smiley = css.IndexOf "/* smiley theme */"
        Assert.True(base' >= 0 && smiley > base', "smiley css must come after the base stylesheet")
        Assert.True(css.LastIndexOf ":root:where(.theme-dark)" > smiley)
        Assert.Contains("#0090e3", css)
        Assert.Contains("#ff6600", css)
        Assert.DoesNotContain("smiley theme", readOut out "assets/css/custom.css"))

[<Fact>]
let ``smiley keeps the terminal layout defaults`` () =
    match Themes.resolve (depsWith []) "smiley" with
    | Ok t ->
        Assert.Equal(Themes.BuiltIn, t.Origin)
        Assert.Equal(Themes.resolve (depsWith []) "terminal" |> Result.map (fun x -> x.Defaults), Ok t.Defaults)
    | Error e -> failwith e

[<Fact>]
let ``cyberpunk css follows the base stylesheet and precedes custom css`` () =
    generate (depsWith []) """{ "theme": "cyberpunk" }""" (fun out ->
        let css = readOut out "assets/css/style.css"
        let base' = css.IndexOf ":root:where(.theme-dark)"
        let cyberpunk = css.IndexOf "/* cyberpunk theme */"
        Assert.True(base' >= 0 && cyberpunk > base', "cyberpunk css must come after the base stylesheet")
        Assert.True(css.LastIndexOf ":root:where(.theme-dark)" > cyberpunk)
        Assert.Contains("#ff2a6d", css)
        Assert.Contains("#05d9e8", css)
        Assert.DoesNotContain("cyberpunk theme", readOut out "assets/css/custom.css"))

[<Fact>]
let ``cyberpunk keeps the terminal layout defaults`` () =
    match Themes.resolve (depsWith []) "cyberpunk" with
    | Ok t ->
        Assert.Equal(Themes.BuiltIn, t.Origin)
        Assert.Equal(Themes.resolve (depsWith []) "terminal" |> Result.map (fun x -> x.Defaults), Ok t.Defaults)
    | Error e -> failwith e

[<Fact>]
let ``cyberpunk glow and grid are scoped to dark mode`` () =
    generate (depsWith []) """{ "theme": "cyberpunk" }""" (fun out ->
        let css = readOut out "assets/css/style.css"
        let theme = css.Substring(css.IndexOf "/* cyberpunk theme */")
        let lightPalette = theme.Substring(0, theme.IndexOf "@media (prefers-color-scheme: dark)")
        Assert.DoesNotContain("text-shadow", lightPalette)
        Assert.DoesNotContain("linear-gradient", lightPalette)
        Assert.Contains(":root:not(.theme-light) body", theme)
        Assert.Contains(":root.theme-dark body", theme)
        Assert.Contains(":root.theme-dark .nav-brand", theme)
        Assert.Contains("kbd, input, button, .q-opt.q-on { text-shadow: none; }", theme))

[<Fact>]
let ``a built-in theme cannot be shadowed by an installed one`` () =
    let files = [ ".eru/themes/ledger/theme.css", "/* impostor */" ]
    generate (depsWith files) """{ "theme": "ledger" }""" (fun out ->
        Assert.DoesNotContain("impostor", readOut out "assets/css/style.css"))

[<Fact>]
let ``discover lists built-in and installed themes once each`` () =
    let d =
        { depsWith (fooTheme @ [ ".eru/themes/ledger/theme.css", "x" ]) with
            ListLocalDirectories = fun dir -> if dir.EndsWith ".eru/themes" then Ok [ dir + "/foo"; dir + "/ledger" ] else Ok [] }
    let found = Themes.discover d
    Assert.Equal<string list>([ "terminal"; "ledger"; "matrix"; "smiley"; "cyberpunk"; "foo" ], found |> List.map fst)
    match found |> List.find (fun (n, _) -> n = "foo") |> snd with
    | Ok t ->
        Assert.Equal(Some "Foo", t.Description)
        Assert.Equal("project", Themes.originLabel t.Origin)
    | Error e -> Assert.Fail e
    match found |> List.find (fun (n, _) -> n = "ledger") |> snd with
    | Ok t -> Assert.Equal(Themes.BuiltIn, t.Origin)
    | Error e -> Assert.Fail e

[<Fact>]
let ``theme defaults sit under the user's layout`` () =
    match Themes.resolve (depsWith fooTheme) "foo" with
    | Error e -> Assert.Fail e
    | Ok theme ->
        let user = siteSettings """{ "layout": { "left": false } }"""
        let merged = Themes.apply theme user
        Assert.Equal(Some Disabled, merged.Layout.Left)
        let untouched = Themes.apply theme SiteSettings.empty
        Assert.Equal(Some "cards", untouched.DefaultView)
        Assert.True(untouched.Layout.Left.IsSome)

[<Fact>]
let ``theme names are validated and parsed from config`` () =
    Assert.Equal(Some "ledger", (siteSettings """{ "theme": "ledger" }""").Theme)
    Assert.Equal(None, (siteSettings "{}").Theme)
    Assert.Contains("theme names may only contain", siteError """{ "theme": "../escape" }""")
