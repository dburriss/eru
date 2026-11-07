module Eru.Site.Themes

open Eru
open Eru.Adapters

// A theme is a folder with a `theme.json` (default `site` config: layout slots, defaultView,
// cards, pages) and a `theme.css` laid over the built-in stylesheet. Both files are optional,
// but a folder with neither is not a theme. The user's own config and `custom.css` always win.

type ThemeOrigin =
    | BuiltIn
    | Project of string
    | User of string

type Theme = {
    Name        : string
    Description : string option
    Css         : string
    Defaults    : SiteSettings
    Origin      : ThemeOrigin
}

/// `theme.json` on disk.
type ThemeFile = {
    Description : string option
    Site        : SiteSection option
}

let originLabel =
    function
    | BuiltIn -> "built-in"
    | Project _ -> "project"
    | User _ -> "user"

// ── built-ins ────────────────────────────────────────────────────────────────

// The terminal look is the base stylesheet itself, so it adds nothing on top.
let private terminalJson = """{ "description": "Monospace, square corners, hairline grid and a query-line Browse." }"""

let private ledgerJson =
    """{
  "description": "Dense and quiet: sans-serif, rounded corners, a facet sidebar and one row per file.",
  "site": {
    "defaultView": "rows",
    "layout": { "left": [ "facets" ] }
  }
}"""

let private rule (selector: string) (body: string) = selector + " {\n  " + body + "\n}\n"

let private ledgerLight =
    "--color-bg: #fbfbfc; --color-surface: #f3f3f6; --color-border: #e6e6eb; --color-text: #16171d; "
    + "--color-muted: #6b6e7b; --color-primary: #4f46e5; --color-accent-soft: #e4e2fb; "
    + "--color-ok: #0f7b4d; --color-warn: #9a6200; --color-badge-bg: #f3f3f6; --color-badge-text: #4b4e5b;"

let private ledgerDark =
    "--color-bg: #0b0c10; --color-surface: #13141a; --color-border: #23252e; --color-text: #e9eaf0; "
    + "--color-muted: #8a8d9c; --color-primary: #8b84ff; --color-accent-soft: #24224a; "
    + "--color-ok: #4fd69c; --color-warn: #f0b650; --color-badge-bg: #1b1d26; --color-badge-text: #b4b7c6;"

// Laid over the base stylesheet, so the palette rules repeat its four selectors (plain, OS dark,
// and the two toggle classes) in the same order; same specificity, later wins.
let private ledgerCss =
    "/* ledger theme */\n"
    + rule ":root" (ledgerLight
                    + " --font-family: ui-sans-serif, -apple-system, BlinkMacSystemFont, \"Geist\", \"Segoe UI\", Helvetica, Arial, sans-serif; --radius: 6px;")
    + "@media (prefers-color-scheme: dark) {\n" + rule "  :root" ledgerDark + "}\n"
    + rule ":root:where(.theme-light)" ledgerLight
    + rule ":root:where(.theme-dark)" ledgerDark
    + """
body { font-size: 13.5px; line-height: 1.45; }
h1 { font-size: 1.1rem; letter-spacing: -0.01em; }
h3 { font-size: 0.72rem; font-weight: 500; letter-spacing: 0.08em; opacity: 1; color: var(--color-muted); }

/* nav */
.site-nav { gap: 0.25rem; padding: 0 1.25rem; min-height: 48px; }
.nav-brand { font-size: 1rem; font-weight: 600; letter-spacing: -0.01em; margin-right: 1rem; }
.nav-brand::before {
  content: ""; display: inline-block; width: 18px; height: 18px; border-radius: 5px;
  background: var(--color-primary); margin-right: 0.5rem; vertical-align: -3px;
}
.nav-brand::after { content: none; }
.site-nav a:not(.nav-brand) { padding: 0.3rem 0.65rem; border-radius: 6px; color: var(--color-muted); }
.site-nav a:not(.nav-brand):hover { background: var(--color-surface); color: var(--color-text); text-decoration: none; }
#theme-toggle { border-radius: 6px; font-size: 0.9rem; }

/* sidebar facets */
.region-left { padding-right: 0.9rem; border-right: 1px solid var(--color-border); }
.sidebar-section li { padding: 0.2rem 0.4rem; border-radius: 6px; }
.sidebar-section li:hover { background: var(--color-surface); }
.sidebar-section li input[type="checkbox"] { accent-color: var(--color-primary); }
.sidebar-section li .count { margin-left: auto; font-family: var(--font-mono); font-size: 0.72rem; }
.sidebar-section li.facet-empty { opacity: 0.4; }

/* query line */
.q-bar { border-color: var(--color-border); background: var(--color-surface); border-radius: 7px; padding: 0.3rem 0.4rem 0.3rem 0.75rem; }
.q-bar:focus-within { border-color: var(--color-primary); box-shadow: 0 0 0 3px var(--color-accent-soft); }
.q-prompt { color: var(--color-muted); font-weight: 400; }
.q-tok { border-radius: 999px; padding: 0 0.6rem; }
.q-pop { border: 1px solid var(--color-border); border-radius: 8px; background: var(--color-bg); box-shadow: 0 10px 30px -12px rgba(0, 0, 0, 0.35); overflow: hidden; }
.q-opt.q-on { background: var(--color-surface); color: var(--color-text); }
.q-opt.q-on u, .q-opt.q-on em { color: var(--color-muted); }
.q-key { border-radius: 6px; }
kbd { border-radius: 4px; }

/* one row per file */
.doc-rows { border-top: 0; }
.doc-row { padding: 0.7rem 0.6rem; border-bottom: 0; border-top: 1px solid var(--color-border); }
.doc-row.q-sel { box-shadow: inset 2px 0 0 var(--color-primary); }
.doc-row .cell-name { font-weight: 500; align-items: center; }
.doc-row .cell-path { font-family: var(--font-mono); font-size: 0.75rem; }
.doc-row.q-sel .cell-path { opacity: 0.9; }
.card-path { font-family: var(--font-mono); margin: -0.15rem 0 0.4rem; }
.doc-row .cell-desc { padding-left: 14px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.doc-row-side { font-family: var(--font-mono); font-size: 0.78rem; color: var(--color-muted); }
.doc-row .add-filter { padding: 0 0.2rem; margin: 0; border-radius: 4px; }
.doc-row .badge { border-radius: 999px; background: none; padding: 0; }

/* sync state as a dot */
.gl { font-size: 0; width: 6px; height: 6px; margin-right: 8px; border-radius: 50%; background: currentColor; align-self: center; }

/* other lists and tables */
.doc-table th { background: none; text-transform: none; letter-spacing: 0; font-size: 0.8rem; font-weight: 500; }
.doc-table th + th, .doc-table td + td { border-left: 0; }
.badge, .add-filter { border-radius: 4px; }
.region-footer { border-top-color: var(--color-border); }
"""

let private matrixJson =
    """{ "description": "The terminal layout in phosphor green: pale mint in light mode, black with a faint glow and scanlines in dark mode." }"""

let private matrixLight =
    "--color-bg: #f2faf3; --color-surface: #e2f3e5; --color-border: #a9d8b2; --color-text: #0a4d1c; "
    + "--color-muted: #3d7a4b; --color-primary: #00872a; --color-accent-soft: #cdecd3; "
    + "--color-ok: #00a832; --color-warn: #4d8f5c; --color-badge-bg: #e2f3e5; --color-badge-text: #0a4d1c; "
    + "--color-graph-incoming: #00872a; --color-graph-outgoing: #3d7a4b; --color-graph-external: #6b9a76;"

let private matrixDark =
    "--color-bg: #020a04; --color-surface: #06170b; --color-border: #0f3a1a; --color-text: #3dff6e; "
    + "--color-muted: #1f9a42; --color-primary: #7dff9c; --color-accent-soft: #0b2a14; "
    + "--color-ok: #5dff85; --color-warn: #22c24f; --color-badge-bg: #06170b; --color-badge-text: #7dff9c; "
    + "--color-graph-incoming: #5dff85; --color-graph-outgoing: #22c24f; --color-graph-external: #1f9a42;"

// Glow and scanlines belong to dark mode only: the OS preference unless the toggle forces light,
// or the toggle forced dark.
let private matrixDarkEffects (root: string) (indent: string) =
    rule (indent + root + " body") "text-shadow: 0 0 6px rgba(61, 255, 110, 0.35);"
    + rule (indent + root + " .site-nav, " + indent + root + " .region-main")
           "background-image: repeating-linear-gradient(0deg, rgba(61, 255, 110, 0.035) 0 1px, transparent 1px 3px);"

let private matrixCss =
    "/* matrix theme */\n"
    + rule ":root" matrixLight
    + "@media (prefers-color-scheme: dark) {\n" + rule "  :root" matrixDark + "}\n"
    + rule ":root:where(.theme-light)" matrixLight
    + rule ":root:where(.theme-dark)" matrixDark
    + "@media (prefers-color-scheme: dark) {\n" + matrixDarkEffects ":root:not(.theme-light)" "  " + "}\n"
    + matrixDarkEffects ":root.theme-dark" ""
    + """
/* the status glyphs and selection carry meaning, so keep the glow off the controls */
kbd, input, button, .q-opt.q-on { text-shadow: none; }

/* badges stay green-on-green in dark mode */
body.theme-dark .badge-manifest, body.theme-dark .badge-okf,
body.theme-dark .badge-human-reviewed, body.theme-dark .badge-machine-confirmed,
body.theme-dark .doc-stale-warning { background: var(--color-surface); color: var(--color-primary); border: 1px solid var(--color-border); }
"""

let private builtIns : (string * string * string) list =
    [ "terminal", terminalJson, ""
      "ledger", ledgerJson, ledgerCss
      "matrix", matrixJson, matrixCss ]

let builtInNames = builtIns |> List.map (fun (n, _, _) -> n)

// ── loading ──────────────────────────────────────────────────────────────────

let private build (name: string) (origin: ThemeOrigin) (json: string) (css: string) : Result<Theme, string> =
    Serialization.deserialize<ThemeFile> json
    |> Result.bind (fun f ->
        if obj.ReferenceEquals(f, null) then Error "theme.json is empty"
        else
            f.Site |> Option.defaultValue SiteSection.empty |> SiteSettings.parse
            |> Result.map (fun defaults -> { Name = name; Description = f.Description; Css = css; Defaults = defaults; Origin = origin }))
    |> Result.mapError (fun e -> $"theme '{name}': theme.json: {e}")

let private fromDir (deps: Deps) (name: string) (origin: ThemeOrigin) (dir: string) : Result<Theme option, string> =
    let read file = deps.ReadLocalFile (PathJoin.Combine(dir, file))
    match read "theme.json", read "theme.css" with
    | Error e, _ | _, Error e -> Error $"theme '{name}': {e}"
    | Ok None, Ok None -> Ok None
    | Ok json, Ok css -> build name origin (json |> Option.defaultValue "{}") (css |> Option.defaultValue "") |> Result.map Some

let private roots (deps: Deps) : (string -> ThemeOrigin) * string list =
    let project = Paths.localThemesDir (deps.GetCwd())
    let user = Paths.userThemesDir ()
    (fun dir -> if dir = project then Project dir else User dir), [ project; user ]

/// Looks a theme up by name: built-in first, then `.eru/themes/<name>/`, then the user's
/// themes directory.
let resolve (deps: Deps) (name: string) : Result<Theme, string> =
    match builtIns |> List.tryFind (fun (n, _, _) -> n = name) with
    | Some (_, json, css) -> build name BuiltIn json css
    | None ->
        let origin, dirs = roots deps
        let rec search =
            function
            | [] -> Ok None
            | dir :: rest ->
                match fromDir deps name (origin dir) (PathJoin.Combine(dir, name)) with
                | Ok None -> search rest
                | other -> other
        match search dirs with
        | Ok (Some theme) -> Ok theme
        | Error e -> Error e
        | Ok None ->
            let installed =
                dirs
                |> List.collect (fun d -> deps.ListLocalDirectories d |> Result.defaultValue [])
                |> List.map PathUtil.fileName
            let available = builtInNames @ installed |> List.distinct |> String.concat ", "
            Error $"site.theme: unknown theme '{name}' (available: {available})"

/// Every theme that can be selected, in resolution order; a name shadowed by an earlier
/// location is listed once.
let discover (deps: Deps) : (string * Result<Theme, string>) list =
    let _, dirs = roots deps
    let installed =
        dirs
        |> List.collect (fun d -> deps.ListLocalDirectories d |> Result.defaultValue [])
        |> List.map PathUtil.fileName
    builtInNames @ installed
    |> List.distinct
    |> List.map (fun name -> name, resolve deps name)

/// Lays the user's settings over the theme's defaults.
let apply (theme: Theme) (settings: SiteSettings) : SiteSettings =
    SiteSettings.merge theme.Defaults settings
