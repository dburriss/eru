module Eru.Site.PageSpecs

open Eru

// Resolves which index pages exist and where they live: the built-ins (unless dropped)
// followed by custom pages. A custom page whose id is a built-in id overrides that
// built-in's index page and keeps its URL. The home page is written to index.html.

type PageSpec = {
    Id      : string
    Url     : string              // site-root-relative, e.g. "tags/index.html"
    Label   : string              // default nav label
    Title   : string              // default page heading
    BuiltIn : bool
    Def     : PageDef option      // the custom definition or override, if any
}

let depthOf (url: string) : int = url |> Seq.filter ((=) '/') |> Seq.length

let builtInLabel =
    function
    | "browse" -> "Browse"
    | "sources" -> "Sources"
    | "tags" -> "Tags"
    | "types" -> "Types"
    | "bundles" -> "Bundles"
    | id -> id

let private builtInTitle =
    function
    | "browse" -> "All files"
    | id -> builtInLabel id

let homeId (settings: SiteSettings) : string = settings.Home |> Option.defaultValue "browse"

let resolve (settings: SiteSettings) : PageSpec list =
    let home = homeId settings
    let defOf id =
        settings.Pages |> List.tryPick (function pid, Defined d when pid = id -> Some d | _ -> None)
    SiteSettings.knownPageIds settings
    |> List.map (fun id ->
        let builtIn = SiteSettings.isBuiltIn id
        let def = defOf id
        let natural = if builtIn then $"{id}/index.html" else $"pages/{id}/index.html"
        let label = def |> Option.bind (fun d -> d.Title) |> Option.defaultValue (if builtIn then builtInLabel id else id)
        let title = def |> Option.bind (fun d -> d.Title) |> Option.defaultValue (if builtIn then builtInTitle id else id)
        { Id = id
          Url = if id = home then "index.html" else natural
          Label = label
          Title = title
          BuiltIn = builtIn
          Def = def })

let pageLinks (specs: PageSpec list) : Map<string, string * string> =
    specs |> List.map (fun s -> s.Id, (s.Label, s.Url)) |> Map.ofList
