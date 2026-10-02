namespace Eru

module Frontmatter =

    type FrontmatterMap = Map<string, Yaml.Node>

    type ActorAt = { By: string; At: System.DateTimeOffset option }

    /// One entry of the OKF v0.2 `sources` list (§5.1).
    type SourceRef =
        { Resource: string
          Id: string option
          Title: string option
          Author: string option
          UsageCount: int option
          LastModified: System.DateTimeOffset option }

    /// The top-level OKF v0.2 `usage_window` that frames `usage_count`.
    type UsageWindow = { From: System.DateTimeOffset option; To: System.DateTimeOffset option }

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

    /// `generated: {by, at}`. When `generated` is absent, falls back to a
    /// legacy v0.1 scalar `timestamp` (OKF v0.2 §13) with an unknown producer.
    let generated (fm: FrontmatterMap) : ActorAt option =
        match Map.tryFind "generated" fm with
        | Some node -> actorAt node
        | None ->
            scalar "timestamp" fm
            |> Option.bind parseDate
            |> Option.map (fun d -> { By = "unknown"; At = Some d })

    let verified (fm: FrontmatterMap) : ActorAt list =
        match Map.tryFind "verified" fm with
        | Some (Yaml.Seq items) -> items |> List.choose actorAt
        | Some (Yaml.Map _ as m) -> actorAt m |> Option.toList
        | _ -> []

    let staleAfter (fm: FrontmatterMap) : System.DateTimeOffset option =
        scalar "stale_after" fm |> Option.bind parseDate

    let private sourceRef (node: Yaml.Node) : SourceRef option =
        let empty r = { Resource = r; Id = None; Title = None; Author = None; UsageCount = None; LastModified = None }
        match node with
        | Yaml.Scalar s when s <> "" -> Some (empty s)   // tolerated; not spec-shaped
        | Yaml.Map kvs ->
            let m = Map.ofList kvs
            match scalar "resource" m with
            | None -> None
            | Some r ->
                Some { Resource = r
                       Id = scalar "id" m
                       Title = scalar "title" m
                       Author = scalar "author" m
                       UsageCount =
                           scalar "usage_count" m
                           |> Option.bind (fun s -> match System.Int32.TryParse s with | true, n -> Some n | _ -> None)
                       LastModified = scalar "last_modified" m |> Option.bind parseDate }
        | _ -> None

    /// `sources`: list of `{resource, id?, title?, author?, usage_count?, last_modified?}`.
    let sources (fm: FrontmatterMap) : SourceRef list =
        match Map.tryFind "sources" fm with
        | Some (Yaml.Seq items) -> items |> List.choose sourceRef
        | Some (Yaml.Map _ as m) -> sourceRef m |> Option.toList
        | _ -> []

    let usageWindow (fm: FrontmatterMap) : UsageWindow option =
        match Map.tryFind "usage_window" fm with
        | Some (Yaml.Map kvs) ->
            let m = Map.ofList kvs
            Some { From = scalar "from" m |> Option.bind parseDate
                   To = scalar "to" m |> Option.bind parseDate }
        | _ -> None

    // --- v0.2 shape diagnostics (non-fatal; consumers must not reject on these) ---

    let hasUtcOffset (s: string) =
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}[Tt ].*(Z|z|[+-]\d{2}:?\d{2})$")

    let private checkDateTime (field: string) (s: string) : string option =
        if (parseDate s).IsNone then Some $"`{field}` \"{s}\" is not a valid ISO 8601 datetime"
        elif not (hasUtcOffset s) then Some $"`{field}` \"{s}\" must be an ISO 8601 datetime with an explicit UTC offset"
        else None

    let private checkActorAt (field: string) (node: Yaml.Node) : string list =
        match node with
        | Yaml.Map kvs ->
            let m = Map.ofList kvs
            [ if (scalar "by" m).IsNone then Some $"`{field}.by` is required"
              match scalar "at" m with
              | None -> Some $"`{field}.at` is required"
              | Some at -> checkDateTime $"{field}.at" at ]
            |> List.choose id
        | _ -> [ $"`{field}` must be a mapping with `by` and `at`" ]

    /// Placeholder actors that stand in for "nobody"; never a real confirmation.
    let private placeholderActors = set [ "unknown"; "none"; "null"; "n/a"; "na"; "false"; "true" ]

    let private verifiedEntryProblems (field: string) (node: Yaml.Node) : string list =
        match checkActorAt field node, node with
        | [], Yaml.Map kvs ->
            match scalar "by" (Map.ofList kvs) with
            | Some by when placeholderActors.Contains(by.ToLowerInvariant()) ->
                [ $"`{field}.by` \"{by}\" is a placeholder; use `human:<id>` or a machine actor name" ]
            | _ -> []
        | problems, _ -> problems

    /// The well-formed `verified` entries as raw (by, at) strings, and how many
    /// entries (or non-list values) were malformed and so cannot be kept.
    let verifiedEntries (fm: FrontmatterMap) : (string * string) list * int =
        let nodes, bad =
            match Map.tryFind "verified" fm with
            | Some (Yaml.Seq items) -> items, 0
            | Some (Yaml.Map _ as node) -> [ node ], 0
            | Some _ -> [], 1
            | None -> [], 0
        let valid, invalid =
            nodes
            |> List.partition (fun n -> (verifiedEntryProblems "verified" n).IsEmpty)
        let raw =
            valid
            |> List.choose (function
                | Yaml.Map kvs ->
                    let m = Map.ofList kvs
                    match scalar "by" m, scalar "at" m with
                    | Some by, Some at -> Some(by, at)
                    | _ -> None
                | _ -> None)
        raw, bad + invalid.Length

    /// Warnings about frontmatter that is readable but not OKF v0.2-shaped.
    /// Returns (rule, message) pairs.
    let shapeWarnings (fm: FrontmatterMap) : (string * string) list =
        [ match Map.tryFind "generated" fm with
          | Some node -> for m in checkActorAt "generated" node -> "generated-shape", m
          | None when Map.containsKey "timestamp" fm ->
              yield "legacy-timestamp", "legacy `timestamp` is superseded by `generated: {by, at}` in OKF v0.2"
          | None -> ()

          match Map.tryFind "verified" fm with
          | Some (Yaml.Seq items) ->
              for item in items do
                  for m in verifiedEntryProblems "verified[]" item -> "verified-shape", m
          | Some (Yaml.Map _ as node) ->
              for m in verifiedEntryProblems "verified" node -> "verified-shape", m
          | Some _ -> yield "verified-shape", "`verified` must be a list of `{by, at}` entries (or a single `{by, at}` mapping); run `eru okf fix` to strip it or `eru okf verify` to set it"
          | None -> ()

          match scalar "stale_after" fm with
          | Some s ->
              match checkDateTime "stale_after" s with
              | Some m -> yield "stale-after-shape", m
              | None -> ()
          | None -> ()

          match scalar "status" fm with
          | Some s when not (List.contains s [ "draft"; "stable"; "deprecated" ]) ->
              yield "status-value", $"`status` \"{s}\" is not one of draft | stable | deprecated"
          | _ -> ()

          match Map.tryFind "sources" fm with
          | Some (Yaml.Seq items) ->
              for item in items do
                  match item with
                  | Yaml.Map kvs ->
                      let m = Map.ofList kvs
                      if (scalar "resource" m).IsNone then
                          yield "sources-shape", "each `sources` entry requires a `resource`"
                      match scalar "usage_count" m with
                      | Some uc when not (fst (System.Int32.TryParse uc)) ->
                          yield "sources-shape", $"`sources[].usage_count` \"{uc}\" must be an integer"
                      | _ -> ()
                      match scalar "last_modified" m with
                      | Some lm ->
                          match checkDateTime "sources[].last_modified" lm with
                          | Some msg -> yield "sources-shape", msg
                          | None -> ()
                      | None -> ()
                  | _ -> yield "sources-shape", "each `sources` entry must be a mapping with a `resource`"
          | Some _ -> yield "sources-shape", "`sources` must be a list of mappings, each with a `resource`"
          | None -> () ]

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
