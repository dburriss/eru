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
          /// Fallback `type` for concepts that have none and match no `FixTypes` rule.
          DefaultType: string
          /// Explicit `--default-type`: beats a matching `FixTypes` rule's type.
          TypeOverride: string option
          /// Ordered `okf.fixTypes` rules; the first matching pattern wins.
          FixTypes: OkfFixTypeRule list
          DryRun: bool
          /// Regenerate the `eru:catalog` block of existing indexes (always on in `Fix` mode).
          Update: bool
          /// With `Update`: adopt indexes that have no markers yet (identified OKF indexes only).
          Force: bool }

    type Change = { Path: string; Rule: string; Description: string; Created: bool }

    type FixResult =
        { Changes: Change list
          /// Problems that could not be repaired automatically.
          Manual: OkfValidate.Violation list
          /// Existing indexes left alone, with the reason (not a failure).
          Skipped: (string * string) list
          DryRun: bool }

    let private isoDate = Regex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled)
    let private dateHeading = Regex(@"^##\s+(.+?)\s*$", RegexOptions.Compiled)

    let private dirOf = OkfIndex.dirOf

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

    /// The `type` rule for `rel`: `None` when no `FixTypes` pattern matches, `Some None` when the
    /// matching rule excludes the file (`type: null`), else `Some (Some type)`.
    let private ruleFor (cmd: Command) (rel: string) : string option option =
        cmd.FixTypes
        |> List.tryFind (fun r -> Patterns.matchesGlob r.Pattern rel)
        |> Option.map (fun r ->
            match r.Type with
            | Some t when not (String.IsNullOrWhiteSpace t) -> Some t
            | _ -> None)

    /// The repair for one existing file, as (new content, rule, description).
    let private repairFile
        (deps: Deps)
        (cmd: Command)
        (rel: string)
        (content: string)
        (manual: ResizeArray<OkfValidate.Violation>)
        : (string * string * string) option =
        let rule = ruleFor cmd rel
        // A README is only repaired when a `FixTypes` rule gives it a type; an untyped README
        // that matches no rule (or an excluding one) is left alone.
        let cls =
            match Frontmatter.classifyContent deps.ParseYamlBlock rel content, rule with
            | Frontmatter.ReadmeFile, Some (Some _) -> Frontmatter.ConceptFile
            | c, _ -> c
        let typ =
            match cmd.TypeOverride, rule with
            | Some t, _ | None, Some (Some t) -> t
            | None, _ -> cmd.DefaultType
        match cls with
        | Frontmatter.ReadmeFile -> None
        | Frontmatter.ConceptFile when rule = Some None -> None
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
                Some(setType typ content, "no-frontmatter", $"added frontmatter with type: {typ}")
            | Frontmatter.MalformedYaml msg ->
                manual.Add { Path = rel; Rule = "malformed-yaml"; Message = $"frontmatter is not valid YAML ({msg}); fix it by hand" }
                None
            | Frontmatter.Parsed m ->
                let typed, typeNote =
                    match Frontmatter.type_ m with
                    | None -> setType typ content, [ $"added type: {typ}" ]
                    | Some _ -> content, []
                let valid, invalid = Frontmatter.verifiedEntries m
                let final, verifiedNote =
                    if invalid = 0 then typed, []
                    else
                        let kept = if valid.IsEmpty then "removed" else $"kept {valid.Length} valid"
                        OkfVerify.setVerified valid typed,
                        [ $"stripped {invalid} malformed `verified` value(s) ({kept}); use `eru okf verify` to record a confirmation" ]
                match typeNote, verifiedNote with
                | [], [] -> None
                | _, [] -> Some(final, "missing-type", String.Join("; ", typeNote))
                | [], _ -> Some(final, "verified-shape", String.Join("; ", verifiedNote))
                | _ -> Some(final, "missing-type", String.Join("; ", typeNote @ verifiedNote))

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
                    else deps.WriteLocalFile (PathJoin.Combine(cmd.Path, rel)) content
                match result with
                | Ok () -> changes.Add { Path = rel; Rule = rule; Description = description; Created = created }
                | Error e -> manual.Add { Path = rel; Rule = "write-error"; Message = e }

            for rel in rels do
                match deps.ReadLocalFile (PathJoin.Combine(cmd.Path, rel)) with
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

            let refresh = cmd.Update || cmd.Mode = Fix
            let allSet = HashSet<string>(allFiles)
            let skipped = ResizeArray<string * string>()
            let rootIsBundle =
                match current.TryGetValue "index.md" with
                | true, c -> OkfIndex.isBundleRoot (Frontmatter.tryParse deps.ParseYamlBlock c)
                | _ -> false

            // Directories that hold concepts (plus their ancestors and the root) get a catalog index.md.
            for catalog in OkfIndex.catalogs deps current do
                let rel = OkfIndex.indexPath catalog.Dir
                match current.TryGetValue rel with
                | true, content ->
                    if refresh then
                        let fm = Frontmatter.tryParse deps.ParseYamlBlock content
                        match OkfIndex.classify rootIsBundle (catalog.Dir = "") fm catalog with
                        | Error reason -> skipped.Add((rel, OkfIndex.reasonText reason))
                        | Ok () ->
                            match OkfIndex.refresh content catalog with
                            | OkfIndex.UpToDate -> ()
                            | OkfIndex.Updated updated ->
                                write rel updated "index-refreshed" "refreshed catalog" false
                            | OkfIndex.BadMarkers ->
                                skipped.Add((rel, "unmatched or duplicated eru:catalog markers; fix them by hand"))
                            | OkfIndex.Unmarked when cmd.Force ->
                                let migrated = OkfIndex.migrate content catalog
                                if migrated <> content then
                                    write rel migrated "index-migrated" "added eru:catalog markers and refreshed catalog" false
                            | OkfIndex.Unmarked ->
                                skipped.Add((rel, "no eru:catalog markers (use `eru okf init --update --force` to adopt)"))
                | _ ->
                    if allSet.Contains rel then
                        // An ignored index.md: never overwritten.
                        if refresh then skipped.Add((rel, OkfIndex.reasonText OkfIndex.IgnoredPath))
                    else
                        write rel (OkfIndex.newIndexContent okfVersion catalog) "missing-index" "created catalog index.md" true

            Ok { Changes = List.ofSeq changes; Manual = List.ofSeq manual; Skipped = List.ofSeq skipped; DryRun = cmd.DryRun }
