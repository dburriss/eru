module Eru.Tui.Browse.BrowseTheme

open Terminal.Gui.Configuration
open Terminal.Gui.Drawing
open Terminal.Gui.ViewBase

let [<Literal>] Main = "eru-tokyo-main"
let [<Literal>] Bar = "eru-tokyo-bar"
let [<Literal>] Muted = "eru-tokyo-muted"
let [<Literal>] Dim = "eru-tokyo-dim"
let [<Literal>] Accent = "eru-tokyo-accent"
let [<Literal>] Tracked = "eru-tokyo-tracked"
let [<Literal>] Danger = "eru-tokyo-danger"

let private bg       = Color.Parse("#1a1b26", null)
let private bgDark   = Color.Parse("#16161e", null)
let private bgPopup  = Color.Parse("#1f2335", null)
let private fg       = Color.Parse("#c0caf5", null)
let private fgMuted  = Color.Parse("#565f89", null)
let private fgDim    = Color.Parse("#414868", null)
let private blue     = Color.Parse("#7aa2f7", null)
let private cyan     = Color.Parse("#7dcfff", null)
let private green    = Color.Parse("#9ece6a", null)
let private orange   = Color.Parse("#ff9e64", null)
let private red      = Color.Parse("#f7768e", null)

let private attr (foreground: Color) (background: Color) =
    let mutable fg = foreground
    let mutable bg = background
    Terminal.Gui.Drawing.Attribute(&fg, &bg)

let private styled (foreground: Color) (background: Color) (style: TextStyle) =
    let mutable fg = foreground
    let mutable bg = background
    let mutable textStyle = style
    Terminal.Gui.Drawing.Attribute(&fg, &bg, &textStyle)

let private scheme normal focus active =
    Scheme(
        Normal = normal,
        Focus = focus,
        Active = active,
        HotNormal = styled cyan bg TextStyle.Bold,
        HotFocus = styled cyan bgPopup TextStyle.Bold,
        Highlight = attr blue bgDark,
        Editable = attr fg bgPopup,
        ReadOnly = attr fg bg,
        Disabled = attr fgDim bg)

let private addScheme name scheme =
    let mutable existing = Unchecked.defaultof<Scheme>
    if not (SchemeManager.TryGetScheme(name, &existing)) then
        SchemeManager.AddScheme(name, scheme)

let register () =
    addScheme Main (scheme (attr fg bg) (attr fg bgPopup) (attr blue bgPopup))
    addScheme Bar (scheme (attr fg bgDark) (attr cyan bgDark) (styled blue bgDark TextStyle.Bold))
    addScheme Muted (scheme (attr fgMuted bg) (attr fg bgPopup) (attr fg bgPopup))
    addScheme Dim (scheme (attr fgDim bg) (attr fgMuted bgPopup) (attr fgMuted bgPopup))
    addScheme Accent (scheme (attr blue bg) (attr cyan bgPopup) (styled cyan bgPopup TextStyle.Bold))
    addScheme Tracked (scheme (attr green bg) (attr green bgPopup) (styled green bgPopup TextStyle.Bold))
    addScheme Danger (scheme (attr red bg) (attr orange bgPopup) (styled red bgPopup TextStyle.Bold))

let apply schemeName (view: View) =
    view.SchemeName <- schemeName
