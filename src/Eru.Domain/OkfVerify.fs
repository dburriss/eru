namespace Eru

open System
open System.IO

/// `eru okf verify`: records a confirmation in a concept file's `verified` list
/// (OKF v0.2 §5.2). Also owns the text edit `OkfFix` uses to strip bad `verified` values.
module OkfVerify =

    type Command =
        { Path: string
          By: string
          At: DateTimeOffset
          DryRun: bool }

    type VerifyResult =
        { Path: string
          By: string
          At: string
          /// An existing malformed `verified` value was discarded.
          ReplacedInvalid: bool
          DryRun: bool }

    /// ISO 8601 with an explicit offset; `Z` for UTC.
    let formatAt (at: DateTimeOffset) : string =
        if at.Offset = TimeSpan.Zero then at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        else at.ToString("yyyy-MM-dd'T'HH:mm:sszzz")

    /// Why `by` cannot be recorded, if it cannot.
    let checkBy (by: string) : string option =
        let t = by.Trim()
        if t = "" then Some "--by must not be empty"
        elif t.Contains '"' || t.Contains '\\' || t.Contains '\n' then Some "--by must not contain quotes, backslashes or newlines"
        elif t.StartsWith "human:" && t.Length = "human:".Length then Some "--by `human:` needs an id, e.g. human:jsmith@acme"
        elif ["unknown"; "none"; "null"; "n/a"; "na"; "false"; "true"] |> List.contains (t.ToLowerInvariant()) then
            Some $"--by \"{t}\" is a placeholder; use `human:<id>` or a machine actor name"
        else None

    /// Index of the closing "---" line, when `lines` starts with a frontmatter block.
    let private blockClose (lines: string[]) : int option =
        if lines.Length < 2 || lines.[0].Trim() <> "---" then None
        else
            lines
            |> Array.skip 1
            |> Array.tryFindIndex (fun l -> l.Trim() = "---")
            |> Option.map (fun i -> i + 1)

    /// Replaces the top-level `verified` key with `entries` (removing it when empty) as a
    /// text edit, so other keys, ordering and comments are untouched.
    let setVerified (entries: (string * string) list) (content: string) : string =
        let lines = content.Split('\n')
        match blockClose lines with
        | None -> content
        | Some close ->
            let eol = if lines.[0].EndsWith "\r" then "\r" else ""
            let isKey (l: string) = l.StartsWith "verified:"
            let startIdx = lines |> Array.take close |> Array.skip 1 |> Array.tryFindIndex isKey |> Option.map (fun i -> i + 1)
            // The key's block runs until the next top-level, non-sequence line.
            let endIdx =
                match startIdx with
                | None -> close
                | Some s ->
                    let isContinuation (l: string) =
                        let t = l.TrimEnd('\r')
                        t = "" || t.StartsWith " " || t.StartsWith "\t" || t.StartsWith "-"
                    let mutable e = s + 1
                    while e < close && isContinuation lines.[e] do
                        e <- e + 1
                    // Leave trailing blank lines to whatever follows.
                    while e > s + 1 && lines.[e - 1].TrimEnd('\r') = "" do
                        e <- e - 1
                    e
            let replacement =
                if entries.IsEmpty then [||]
                else
                    Array.append
                        [| "verified:" + eol |]
                        (entries |> List.map (fun (by, at) -> $"  - {{ by: \"{by}\", at: \"{at}\" }}" + eol) |> Array.ofList)
            let from = defaultArg startIdx close
            Array.concat [ lines.[.. from - 1]; replacement; lines.[endIdx ..] ]
            |> String.concat "\n"

    let execute (deps: Deps) (cmd: Command) : Result<VerifyResult, string> =
        match checkBy cmd.By with
        | Some e -> Error e
        | None ->
        let by = cmd.By.Trim()
        let fileName = Path.GetFileName cmd.Path
        if Frontmatter.classifyFile fileName <> Frontmatter.ConceptFile then
            Error $"{fileName} is not a concept file; `verified` only applies to concept .md files"
        else
            match deps.ReadLocalFile cmd.Path with
            | Error e -> Error e
            | Ok None -> Error $"file not found: {cmd.Path}"
            | Ok (Some content) ->
                match Frontmatter.tryParse deps.ParseYamlBlock content with
                | Frontmatter.NoBlock -> Error $"{cmd.Path} has no frontmatter; run `eru okf fix` first"
                | Frontmatter.MalformedYaml msg -> Error $"{cmd.Path} frontmatter is not valid YAML ({msg}); fix it by hand"
                | Frontmatter.Parsed fm ->
                    if (Frontmatter.type_ fm).IsNone then Error $"{cmd.Path} has no `type`; run `eru okf fix` first"
                    else
                        let existing, invalid = Frontmatter.verifiedEntries fm
                        let at = formatAt cmd.At
                        let updated = setVerified (existing @ [ by, at ]) content
                        let write = if cmd.DryRun then Ok () else deps.WriteLocalFile cmd.Path updated
                        match write with
                        | Error e -> Error e
                        | Ok () -> Ok { Path = cmd.Path; By = by; At = at; ReplacedInvalid = invalid > 0; DryRun = cmd.DryRun }
