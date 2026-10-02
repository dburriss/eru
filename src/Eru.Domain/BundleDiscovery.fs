namespace Eru

// Discovers the files an OKF bundle publishes by walking its tree and reading
// frontmatter, rather than relying on a declared .eru/manifest.json.
module BundleDiscovery =

    type DiscoveredFile = {
        RemotePath   : string
        Content      : string
        Contribution : Contribution
        Type         : string option
        Title        : string option
        OkfStatus    : string option
        Generated    : Frontmatter.ActorAt option
        Verified     : Frontmatter.ActorAt list
        StaleAfter   : System.DateTimeOffset option
        Resource     : string option
    }

    let private toDiscoveredFile (parseYaml: Yaml.Parse) (remotePath: string) (content: string) : DiscoveredFile =
        match Frontmatter.classifyFile remotePath with
        | Frontmatter.IndexFile | Frontmatter.LogFile | Frontmatter.ReadmeFile ->
            { RemotePath   = remotePath
              Content      = content
              Contribution = { Tags = []; Description = None }
              Type         = None
              Title        = None
              OkfStatus    = None
              Generated    = None
              Verified     = []
              StaleAfter   = None
              Resource     = None }
        | Frontmatter.ConceptFile ->
            let fm = Frontmatter.parse parseYaml content
            { RemotePath   = remotePath
              Content      = content
              Contribution =
                { Tags        = Frontmatter.tags fm |> List.map (fun t -> t.ToLowerInvariant()) |> List.distinct
                  Description = Frontmatter.description fm }
              Type         = Frontmatter.type_ fm
              Title        = Frontmatter.title fm
              OkfStatus    = Frontmatter.status fm
              Generated    = Frontmatter.generated fm
              Verified     = Frontmatter.verified fm
              StaleAfter   = Frontmatter.staleAfter fm
              Resource     = Frontmatter.resource fm }

    // Walks one Okf-kind bundle: lists its .md files, fetches them all in a single
    // batched call, and classifies/extracts frontmatter for each.
    let walkBundle
        (deps: Deps) (ignorePatterns: string list) (sourceName: string) (sourceUrl: string) (branch: string) (bundle: Bundle)
        : Result<DiscoveredFile list, string> =
        let bundlePathOpt = if bundle.Path = "" then None else Some bundle.Path
        match deps.ListRemoteFiles sourceUrl (Some branch) bundlePathOpt with
        | Error e -> Error e
        | Ok relativePaths ->
            // ListRemoteFiles returns bundle-relative paths; RemotePath must stay
            // repo-root-relative, so re-prefix every candidate with the bundle's own path.
            let candidatePaths =
                relativePaths
                |> List.filter (fun p -> p.EndsWith(".md"))
                |> List.filter (fun p -> not (Patterns.isOkfIgnored ignorePatterns p))
                |> List.map (fun p ->
                    match bundlePathOpt with
                    | None    -> p
                    | Some bp -> bp + "/" + p)
            match candidatePaths with
            | [] -> Ok []
            | _  ->
                match deps.FetchRemoteContent sourceUrl branch candidatePaths with
                | Error e -> Error e
                | Ok files ->
                    // OKF concepts are typed files: untyped concept files are not published.
                    files
                    |> List.map (fun (remotePath, content) -> toDiscoveredFile deps.ParseYamlBlock remotePath content)
                    |> List.filter (fun d ->
                        match Frontmatter.classifyFile d.RemotePath with
                        | Frontmatter.ConceptFile -> d.Type |> Option.exists (fun t -> not (System.String.IsNullOrWhiteSpace t))
                        | _ -> true)
                    |> Ok
