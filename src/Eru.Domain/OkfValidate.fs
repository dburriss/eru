namespace Eru

open System.IO
open System.Text.RegularExpressions

module OkfValidate =

    type Command = { Path: string }

    type Violation = { Path: string; Rule: string; Message: string }

    type ValidateResult = { TotalConcepts: int; Violations: Violation list }

    let private dateHeading = Regex(@"^##\s+(.+?)\s*$", RegexOptions.Compiled)
    let private isoDate = Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled)

    let private validateIndex (rel: string) (fm: Frontmatter.ParseOutcome) : Violation option =
        match fm with
        | Frontmatter.NoBlock -> None
        | Frontmatter.MalformedYaml msg ->
            Some { Path = rel; Rule = "index-frontmatter"; Message = $"index.md frontmatter is not valid YAML: {msg}" }
        | Frontmatter.Parsed map ->
            let isRoot = Path.GetDirectoryName(rel: string) = ""
            let keys = map |> Map.toList |> List.map fst
            if isRoot && keys = [ "okf_version" ] then
                None
            elif isRoot then
                Some { Path = rel; Rule = "index-frontmatter"; Message = "bundle-root index.md frontmatter may only contain okf_version" }
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

    let execute (deps: Deps) (cmd: Command) : Result<ValidateResult, string> =
        match deps.ListMarkdownFiles cmd.Path with
        | Error e -> Error e
        | Ok relFiles ->
            let violations = ResizeArray()
            let mutable totalConcepts = 0

            for rel in relFiles do
                let fullPath = Path.Combine(cmd.Path, rel)
                match deps.ReadLocalFile fullPath with
                | Error e -> violations.Add { Path = rel; Rule = "read-error"; Message = e }
                | Ok None -> ()
                | Ok (Some content) ->
                    match Frontmatter.classifyFile rel with
                    | Frontmatter.IndexFile ->
                        Frontmatter.tryParse deps.ParseYamlBlock content
                        |> validateIndex rel
                        |> Option.iter violations.Add
                    | Frontmatter.LogFile ->
                        validateLog rel content |> List.iter violations.Add
                    | Frontmatter.ConceptFile ->
                        let violation, isConformant =
                            Frontmatter.tryParse deps.ParseYamlBlock content
                            |> validateConcept rel
                        violation |> Option.iter violations.Add
                        if isConformant then totalConcepts <- totalConcepts + 1

            Ok { TotalConcepts = totalConcepts; Violations = violations |> List.ofSeq }
