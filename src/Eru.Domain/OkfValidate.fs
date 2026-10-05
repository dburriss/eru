namespace Eru

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions

module OkfValidate =

    /// `StrictLinks` reports broken links, images, wikilinks and anchors as violations instead of warnings.
    type Command = { Path: string; IgnorePatterns: string list; StrictLinks: bool }

    type Violation = { Path: string; Rule: string; Message: string }

    /// `Violations` fail validation; `Warnings` are advisory v0.2 shape/legacy notes
    /// (consumers must not reject on them, so they never affect the exit code).
    type ValidateResult = { TotalConcepts: int; Violations: Violation list; Warnings: Violation list }

    let private dateHeading = Regex(@"^##\s+(.+?)\s*$", RegexOptions.Compiled)
    let private isoDate = Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled)

    let private validateIndex (rel: string) (fm: Frontmatter.ParseOutcome) : Violation option =
        match fm with
        | Frontmatter.NoBlock -> None
        | Frontmatter.MalformedYaml msg ->
            Some { Path = rel; Rule = "index-frontmatter"; Message = $"index.md frontmatter is not valid YAML: {msg}" }
        | Frontmatter.Parsed map ->
            let isRoot = PathUtil.dirName rel = ""
            let keys = map |> Map.toList |> List.map fst
            if isRoot && keys = [ "okf_version" ] then
                None
            elif isRoot then
                Some { Path = rel; Rule = "index-frontmatter"; Message = "bundle-root index.md frontmatter should contain only `okf_version`; this is what makes eru detect the directory as an OKF bundle" }
            else
                Some { Path = rel; Rule = "index-frontmatter"; Message = "index.md must not contain frontmatter" }

    let private validateLog (rel: string) (content: string) : Violation list =
        content.Split([| "\r\n"; "\n" |], System.StringSplitOptions.None)
        |> Array.choose (fun line ->
            let m = dateHeading.Match line
            if not m.Success then
                None
            else
                let heading = m.Groups.[1].Value
                if isoDate.IsMatch heading then None
                else Some { Path = rel; Rule = "log-date-heading"; Message = $"date heading \"{heading}\" is not ISO 8601 YYYY-MM-DD" })
        |> Array.toList

    let private validateConcept (rel: string) (fm: Frontmatter.ParseOutcome) : Violation option * bool =
        match fm with
        | Frontmatter.NoBlock ->
            Some { Path = rel; Rule = "no-frontmatter"; Message = "missing frontmatter block" }, false
        | Frontmatter.MalformedYaml msg ->
            Some { Path = rel; Rule = "malformed-yaml"; Message = $"frontmatter is not valid YAML: {msg}" }, false
        | Frontmatter.Parsed map ->
            match Frontmatter.type_ map with
            | None -> Some { Path = rel; Rule = "missing-type"; Message = "missing required `type` field" }, false
            | Some _ -> None, true

    let private versionWarning (rel: string) (fm: Frontmatter.ParseOutcome) : Violation option =
        match fm with
        | Frontmatter.Parsed map when PathUtil.dirName rel = "" ->
            match Frontmatter.okfVersion map with
            | Some "0.2" | None -> None
            | Some v -> Some { Path = rel; Rule = "okf-version"; Message = $"`okf_version` \"{v}\" is not \"0.2\", the version eru targets" }
        | _ -> None

    /// Newest-first check for log.md date headings (OKF v0.2 §4.4 example).
    let private logOrderWarnings (rel: string) (content: string) : Violation list =
        let dates =
            content.Split([| "\r\n"; "\n" |], System.StringSplitOptions.None)
            |> Array.choose (fun l ->
                let m = dateHeading.Match l
                if m.Success && isoDate.IsMatch m.Groups.[1].Value then Some m.Groups.[1].Value else None)
        if dates |> Array.pairwise |> Array.exists (fun (a, b) -> System.String.CompareOrdinal(a, b) < 0) then
            [ { Path = rel; Rule = "log-order"; Message = "log.md date headings should be newest first" } ]
        else []

    /// Broken page links, images, wikilinks and heading anchors in `docs` (`rel path`, content, title).
    /// `allFiles` is every markdown file in the bundle (including ignored ones), as relative paths.
    let private checkLinks (deps: Deps) (root: string) (allFiles: string list) (docs: (string * string * string option) list) : Violation list =
        let known = Set.ofList allFiles
        let titleIndex = { LinkGraph.titleIndexOf (docs |> List.map (fun (rel, _, title) -> rel, title)) with KnownPaths = known }

        let anchorCache = Dictionary<string, Set<string>>()
        let anchorsOf (path: string) =
            match anchorCache.TryGetValue path with
            | true, anchors -> anchors
            | _ ->
                let anchors =
                    match deps.ReadLocalFile (PathJoin.Combine(root, path)) with
                    | Ok (Some content) -> OkfLinks.headingAnchors content
                    | _ -> Set.empty
                anchorCache.[path] <- anchors
                anchors

        // Non-markdown targets (images, PDFs, folders) are checked through a per-directory listing.
        let dirCache = Dictionary<string, Set<string>>()
        let fileExists (path: string) =
            let dir = PathUtil.dirName path
            let names =
                match dirCache.TryGetValue dir with
                | true, names -> names
                | _ ->
                    let names =
                        match deps.ListLocalFiles (PathJoin.Combine(root, dir)) with
                        | Ok files -> files |> List.map PathUtil.fileName |> Set.ofList
                        | Error _ -> Set.empty
                    dirCache.[dir] <- names
                    names
            names.Contains (PathUtil.fileName path) || deps.DirectoryExists (PathJoin.Combine(root, path))

        docs
        |> List.collect (fun (rel, content, _) ->
            OkfLinks.extract content
            |> List.choose (fun link ->
                let violation rule msg = Some { Path = rel; Rule = rule; Message = $"line {link.Line}: {msg}" }
                let anchorCheck (targetPath: string) =
                    match link.Fragment with
                    | Some fragment when not (OkfLinks.anchorExists (anchorsOf targetPath) fragment) ->
                        violation "broken-anchor" $"'{link.Target}#{fragment}' -> heading '{fragment}' not found in {targetPath}"
                    | _ -> None
                match link.Kind with
                | OkfLinks.Wiki ->
                    match LinkGraph.resolveWikilink "" rel titleIndex link.Target with
                    | Some (LinkGraph.InternalNode entry) when known.Contains entry.RemotePath -> anchorCheck entry.RemotePath
                    | _ -> violation "broken-wikilink" $"wikilink '{link.Text}' does not match any note"
                | kind ->
                    let notFound () =
                        if kind = OkfLinks.Image then violation "broken-image" $"image '{link.Text}' -> '{link.Target}' not found"
                        else violation "broken-link" $"link '{link.Text}' -> '{link.Target}' not found"
                    match LinkGraph.resolveLink "" rel link.Target with
                    | Some (LinkGraph.InternalNode entry) ->
                        let isMarkdown = entry.RemotePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        if isMarkdown then
                            if known.Contains entry.RemotePath then anchorCheck entry.RemotePath else notFound ()
                        elif fileExists entry.RemotePath then None
                        else notFound ()
                    | Some (LinkGraph.ExternalNode _) -> None
                    // Resolves to nothing: the path climbs out of the bundle root.
                    | None -> notFound ()))

    let execute (deps: Deps) (cmd: Command) : Result<ValidateResult, string> =
        match deps.ListMarkdownFiles cmd.Path with
        | Error e -> Error e
        | Ok allFiles ->
            let relFiles = allFiles |> List.filter (fun rel -> not (Patterns.isOkfIgnored cmd.IgnorePatterns rel))
            let violations = ResizeArray()
            let warnings = ResizeArray()
            let linkDocs = ResizeArray<string * string * string option>()
            let mutable totalConcepts = 0

            for rel in relFiles do
                let fullPath = PathJoin.Combine(cmd.Path, rel)
                match deps.ReadLocalFile fullPath with
                | Error e -> violations.Add { Path = rel; Rule = "read-error"; Message = e }
                | Ok None -> ()
                | Ok (Some content) ->
                    match Frontmatter.classifyFile rel with
                    | Frontmatter.IndexFile ->
                        let outcome = Frontmatter.tryParse deps.ParseYamlBlock content
                        validateIndex rel outcome |> Option.iter violations.Add
                        versionWarning rel outcome |> Option.iter warnings.Add
                        linkDocs.Add((rel, content, None))
                    | Frontmatter.ReadmeFile -> ()
                    | Frontmatter.LogFile ->
                        validateLog rel content |> List.iter violations.Add
                        logOrderWarnings rel content |> List.iter warnings.Add
                    | Frontmatter.ConceptFile ->
                        let outcome = Frontmatter.tryParse deps.ParseYamlBlock content
                        let violation, isConformant = validateConcept rel outcome
                        violation |> Option.iter violations.Add
                        match outcome with
                        | Frontmatter.Parsed map ->
                            for rule, msg in Frontmatter.shapeWarnings map do
                                warnings.Add { Path = rel; Rule = rule; Message = msg }
                            linkDocs.Add((rel, content, Frontmatter.title map))
                        | _ -> linkDocs.Add((rel, content, None))
                        if isConformant then totalConcepts <- totalConcepts + 1

            let linkProblems = checkLinks deps cmd.Path allFiles (List.ofSeq linkDocs)
            if cmd.StrictLinks then violations.AddRange linkProblems else warnings.AddRange linkProblems

            Ok { TotalConcepts = totalConcepts; Violations = violations |> List.ofSeq; Warnings = warnings |> List.ofSeq }
