namespace Eru

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text.RegularExpressions

/// Scaffolds (`CreateOnly`, i.e. `eru okf init`) and repairs (`Fix`, i.e. `eru okf fix`)
/// OKF structure so that `OkfValidate` passes. Rule ids match `OkfValidate` where a
/// violation is being repaired.
module OkfFix =

    /// Value written to the bundle-root `index.md` when none is present.
    let okfVersion = "0.2"

    type Mode =
        | CreateOnly
        | Fix

    type Command =
        { Path: string
          IgnorePatterns: string list
          Mode: Mode
          DefaultType: string
          DryRun: bool }

    type Change = { Path: string; Rule: string; Description: string; Created: bool }

    type FixResult =
        { Changes: Change list
          /// Problems that could not be repaired automatically.
          Manual: OkfValidate.Violation list
          DryRun: bool }

    let private isoDate = Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled)
    let private dateHeading = Regex(@"^##\s+(.+?)\s*$", RegexOptions.Compiled)

    let private dirOf (rel: string) =
        match rel.LastIndexOf '/' with
        | -1 -> ""
        | i -> rel.Substring(0, i)

    let private parentOf (dir: string) = dirOf dir

    /// Index of the closing "---" line, when `lines` starts with a frontmatter block.
    let private blockClose (lines: string[]) : int option =
        if lines.Length < 2 || lines.[0].Trim() <> "---" then None
        else
            lines
            |> Array.skip 1
            |> Array.tryFindIndex (fun l -> l.Trim() = "---")
            |> Option.map (fun i -> i + 1)

    let private withRootFrontmatter (version: string) (content: string) =
        $"---\nokf_version: \"{version}\"\n---\n{Frontmatter.body content}"

    /// Sets `type:` as a text edit so other keys, order and comments are untouched.
    let private setType (typ: string) (content: string) : string =
        let lines = content.Split('\n')
        match blockClose lines with
        | None -> $"---\ntype: {typ}\n---\n{content}"
        | Some close ->
            match lines.[1 .. close - 1] |> Array.tryFindIndex (fun l -> l.StartsWith "type:") with
            | Some i ->
                lines.[i + 1] <- $"type: {typ}"
                String.Join("\n", lines)
            | None -> String.Join("\n", Array.insertAt 1 $"type: {typ}" lines)

    let private fixLog (rel: string) (content: string) : string * OkfValidate.Violation list =
        let manual = ResizeArray<OkfValidate.Violation>()
        let lines =
            content.Split('\n')
            |> Array.map (fun line ->
                let m = dateHeading.Match line
                if not m.Success then line
                else
                    let heading = m.Groups.[1].Value
                    if isoDate.IsMatch heading then line
                    else
                        match DateTime.TryParse(heading, CultureInfo.InvariantCulture, DateTimeStyles.None) with
                        | true, d ->
                            "## " + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            + (if line.EndsWith "\r" then "\r" else "")
                        | _ ->
                            manual.Add { Path = rel; Rule = "log-date-heading"; Message = $"date heading \"{heading}\" cannot be parsed as a date; fix it by hand" }
                            line)
        String.Join("\n", lines), List.ofSeq manual

    let private cell (s: string) = s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")

    let private conceptRow (rel: string) (content: string) (deps: Deps) =
        let fm =
            match Frontmatter.tryParse deps.ParseYamlBlock content with
            | Frontmatter.Parsed m -> m
            | _ -> Frontmatter.empty
        let file = Path.GetFileName rel
        let title = Frontmatter.title fm |> Option.defaultValue (Path.GetFileNameWithoutExtension file)
        let typ = Frontmatter.type_ fm |> Option.defaultValue ""
        let tags = Frontmatter.tags fm |> String.concat ", "
        let stale =
            match Map.tryFind "stale_after" fm with
            | Some (Yaml.Scalar s) -> s
            | _ -> ""
        let href = file.Replace(" ", "%20")
        $"| [{cell title}]({href}) | {cell typ} | {cell tags} | {cell stale} |"

    let private catalogBody (dir: string) (rows: string list) (subdirs: string list) =
        let heading = if dir = "" then "Index" else Path.GetFileName dir
        let sb = Text.StringBuilder()
        sb.Append($"# {heading}\n\n") |> ignore
        if not subdirs.IsEmpty then
            sb.Append("## Folders\n\n") |> ignore
            for s in subdirs do
                let name = Path.GetFileName s
                let href = name.Replace(" ", "%20")
                sb.Append($"- [{name}/]({href}/index.md)\n") |> ignore
            sb.Append("\n") |> ignore
        if not rows.IsEmpty then
            sb.Append("| Concept | Type | Tags | Stale after |\n|---|---|---|---|\n") |> ignore
            for r in rows do
                sb.Append(r).Append("\n") |> ignore
        sb.ToString()

    /// The repair for one existing file, as (new content, rule, description).
    let private repairFile
        (deps: Deps)
        (cmd: Command)
        (rel: string)
        (content: string)
        (manual: ResizeArray<OkfValidate.Violation>)
        : (string * string * string) option =
        match Frontmatter.classifyFile rel with
        | Frontmatter.ReadmeFile -> None
        | Frontmatter.LogFile ->
            let fixedContent, unfixable = fixLog rel content
            manual.AddRange unfixable
            if fixedContent <> content then Some(fixedContent, "log-date-heading", "rewrote date headings to YYYY-MM-DD")
            else None
        | Frontmatter.IndexFile ->
            let isRoot = dirOf rel = ""
            match Frontmatter.tryParse deps.ParseYamlBlock content, isRoot with
            | Frontmatter.NoBlock, true ->
                Some(withRootFrontmatter okfVersion content, "root-okf-version", "added okf_version so the directory is detected as an OKF bundle")
            | Frontmatter.NoBlock, false -> None
            | Frontmatter.Parsed m, true when (m |> Map.toList |> List.map fst) = [ "okf_version" ] -> None
            | Frontmatter.Parsed m, true ->
                let version = Frontmatter.okfVersion m |> Option.defaultValue okfVersion
                Some(withRootFrontmatter version content, "index-frontmatter", "reduced root index.md frontmatter to okf_version")
            | Frontmatter.MalformedYaml _, true ->
                Some(withRootFrontmatter okfVersion content, "index-frontmatter", "replaced invalid root index.md frontmatter with okf_version")
            | _, false ->
                Some(Frontmatter.body content, "index-frontmatter", "removed frontmatter from index.md")
        | Frontmatter.ConceptFile ->
            match Frontmatter.tryParse deps.ParseYamlBlock content with
            | Frontmatter.NoBlock ->
                Some(setType cmd.DefaultType content, "no-frontmatter", $"added frontmatter with type: {cmd.DefaultType}")
            | Frontmatter.MalformedYaml msg ->
                manual.Add { Path = rel; Rule = "malformed-yaml"; Message = $"frontmatter is not valid YAML ({msg}); fix it by hand" }
                None
            | Frontmatter.Parsed m ->
                match Frontmatter.type_ m with
                | Some _ -> None
                | None -> Some(setType cmd.DefaultType content, "missing-type", $"added type: {cmd.DefaultType}")

    let execute (deps: Deps) (cmd: Command) : Result<FixResult, string> =
        match deps.ListMarkdownFiles cmd.Path with
        | Error e -> Error e
        | Ok allFiles ->
            let rels = allFiles |> List.filter (fun rel -> not (Patterns.isOkfIgnored cmd.IgnorePatterns rel))
            let changes = ResizeArray<Change>()
            let manual = ResizeArray<OkfValidate.Violation>()
            // File contents after repairs, so generated catalogs reflect them.
            let current = Dictionary<string, string>()

            let write (rel: string) (content: string) (rule: string) (description: string) (created: bool) =
                let result =
                    if cmd.DryRun then Ok ()
                    else deps.WriteLocalFile (Path.Combine(cmd.Path, rel)) content
                match result with
                | Ok () -> changes.Add { Path = rel; Rule = rule; Description = description; Created = created }
                | Error e -> manual.Add { Path = rel; Rule = "write-error"; Message = e }

            for rel in rels do
                match deps.ReadLocalFile (Path.Combine(cmd.Path, rel)) with
                | Error e -> manual.Add { Path = rel; Rule = "read-error"; Message = e }
                | Ok None -> ()
                | Ok (Some content) ->
                    current.[rel] <- content
                    if cmd.Mode = Fix then
                        match repairFile deps cmd rel content manual with
                        | Some (updated, rule, description) when updated <> content ->
                            write rel updated rule description false
                            current.[rel] <- updated
                        | _ -> ()

            // Directories that hold concepts (plus their ancestors and the root) get a catalog index.md.
            let concepts =
                current.Keys
                |> Seq.filter (fun rel -> Frontmatter.classifyFile rel = Frontmatter.ConceptFile)
                |> Seq.sort
                |> List.ofSeq

            let dirs = HashSet<string>([ "" ])
            for rel in concepts do
                let mutable d = dirOf rel
                while d <> "" && dirs.Add d do
                    d <- parentOf d

            let hasIndex dir = current.ContainsKey(if dir = "" then "index.md" else dir + "/index.md")

            for dir in dirs |> Seq.sort |> List.ofSeq do
                if not (hasIndex dir) then
                    let rel = if dir = "" then "index.md" else dir + "/index.md"
                    let rows =
                        concepts
                        |> List.filter (fun c -> dirOf c = dir)
                        |> List.map (fun c -> conceptRow c current.[c] deps)
                    let subdirs = dirs |> Seq.filter (fun d -> d <> "" && parentOf d = dir) |> Seq.sort |> List.ofSeq
                    let body = catalogBody dir rows subdirs
                    let content =
                        if dir = "" then $"---\nokf_version: \"{okfVersion}\"\n---\n{body}" else body
                    write rel content "missing-index" "created catalog index.md" true

            Ok { Changes = List.ofSeq changes; Manual = List.ofSeq manual; DryRun = cmd.DryRun }
