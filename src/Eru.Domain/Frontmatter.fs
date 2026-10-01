namespace Eru

module Frontmatter =

    type FrontmatterMap = Map<string, Yaml.Node>

    type ActorAt = { By: string; At: System.DateTimeOffset option }

    let empty : FrontmatterMap = Map.empty

    /// Locates the "---"..."---" delimited block at the start of `content`
    /// and returns its inner text, or None if there is no well-formed block.
    let private extractBlock (content: string) : string option =
        let lines = content.Split([| "\r\n"; "\n" |], System.StringSplitOptions.None)
        if lines.Length < 2 || lines.[0].Trim() <> "---" then
            None
        else
            match lines |> Array.skip 1 |> Array.tryFindIndex (fun l -> l.Trim() = "---") with
            | None -> None
            | Some closeIdx ->
                lines.[1 .. closeIdx] |> String.concat "\n" |> Some

    /// Returns `content` with a leading "---"..."---" frontmatter block (if any)
    /// stripped, verbatim otherwise — for building an agent prompt from a raw
    /// `.md` capture without re-embedding its YAML.
    let body (content: string) : string =
        let lines = content.Split([| "\r\n"; "\n" |], System.StringSplitOptions.None)
        if lines.Length < 2 || lines.[0].Trim() <> "---" then
            content
        else
            match lines |> Array.skip 1 |> Array.tryFindIndex (fun l -> l.Trim() = "---") with
            | None -> content
            | Some closeIdx ->
                let bodyStartLine = closeIdx + 2
                if bodyStartLine >= lines.Length then ""
                else lines.[bodyStartLine ..] |> String.concat "\n" |> fun s -> s.TrimStart('\n', '\r')

    let parse (parseYaml: Yaml.Parse) (content: string) : FrontmatterMap =
        match extractBlock content with
        | None -> empty
        | Some blockText ->
            match parseYaml blockText with
            | Ok (Yaml.Map kvs) -> Map.ofList kvs
            | _ -> empty

    /// Distinguishes "no frontmatter block at all" from "block present but
    /// malformed/not-a-mapping" from "well-formed" — needed by conformance
    /// checks that must report which of these occurred, unlike `parse`
    /// which treats all three as equally "no data".
    type ParseOutcome =
        | NoBlock
        | MalformedYaml of string
        | Parsed of FrontmatterMap

    let tryParse (parseYaml: Yaml.Parse) (content: string) : ParseOutcome =
        match extractBlock content with
        | None -> NoBlock
        | Some blockText ->
            match parseYaml blockText with
            | Ok (Yaml.Map kvs) -> Parsed (Map.ofList kvs)
            | Ok _ -> MalformedYaml "frontmatter block is not a YAML mapping"
            | Error e -> MalformedYaml e

    // --- Lenses over well-known fields ---

    let private scalar (key: string) (fm: FrontmatterMap) : string option =
        match Map.tryFind key fm with
        | Some (Yaml.Scalar s) when s <> "" -> Some s
        | _ -> None

    let description (fm: FrontmatterMap) = scalar "description" fm
    let type_       (fm: FrontmatterMap) = scalar "type" fm
    let title       (fm: FrontmatterMap) = scalar "title" fm
    let status      (fm: FrontmatterMap) = scalar "status" fm
    let resource    (fm: FrontmatterMap) = scalar "resource" fm
    let okfVersion  (fm: FrontmatterMap) = scalar "okf_version" fm

    let tags (fm: FrontmatterMap) : string list =
        match Map.tryFind "tags" fm with
        | Some (Yaml.Seq items) ->
            items |> List.choose (function Yaml.Scalar s -> Some s | _ -> None)
        | _ -> []

    let private parseDate (s: string) : System.DateTimeOffset option =
        match System.DateTimeOffset.TryParse s with
        | true, d -> Some d
        | false, _ -> None

    let private actorAt (node: Yaml.Node) : ActorAt option =
        match node with
        | Yaml.Map kvs ->
            let m = Map.ofList kvs
            match Map.tryFind "by" m with
            | Some (Yaml.Scalar by) when by <> "" ->
                let at =
                    match Map.tryFind "at" m with
                    | Some (Yaml.Scalar s) -> parseDate s
                    | _ -> None
                Some { By = by; At = at }
            | _ -> None
        | _ -> None

    let generated (fm: FrontmatterMap) : ActorAt option =
        Map.tryFind "generated" fm |> Option.bind actorAt

    let verified (fm: FrontmatterMap) : ActorAt list =
        match Map.tryFind "verified" fm with
        | Some (Yaml.Seq items) -> items |> List.choose actorAt
        | Some (Yaml.Map _ as m) -> actorAt m |> Option.toList
        | _ -> []

    let staleAfter (fm: FrontmatterMap) : System.DateTimeOffset option =
        scalar "stale_after" fm |> Option.bind parseDate

    type FileClass =
        | IndexFile
        | LogFile
        | ReadmeFile
        | ConceptFile

    // Classifies a bundle-relative path by its filename, per OKF §11 conventions.
    // README.md is human prose rather than a concept, so it is excluded like the
    // reserved index.md/log.md (case-insensitive, any depth).
    let classifyFile (relPath: string) : FileClass =
        match System.IO.Path.GetFileName(relPath: string) with
        | "index.md" -> IndexFile
        | "log.md"   -> LogFile
        | name when name.Equals("readme.md", System.StringComparison.OrdinalIgnoreCase) -> ReadmeFile
        | _          -> ConceptFile
