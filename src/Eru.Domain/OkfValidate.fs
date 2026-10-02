namespace Eru

open System.IO
open System.Text.RegularExpressions

module OkfValidate =

    type Command = { Path: string; IgnorePatterns: string list }

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

    let execute (deps: Deps) (cmd: Command) : Result<ValidateResult, string> =
        match deps.ListMarkdownFiles cmd.Path with
        | Error e -> Error e
        | Ok allFiles ->
            let relFiles = allFiles |> List.filter (fun rel -> not (Patterns.isOkfIgnored cmd.IgnorePatterns rel))
            let violations = ResizeArray()
            let warnings = ResizeArray()
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
                        | _ -> ()
                        if isConformant then totalConcepts <- totalConcepts + 1

            Ok { TotalConcepts = totalConcepts; Violations = violations |> List.ofSeq; Warnings = warnings |> List.ofSeq }
