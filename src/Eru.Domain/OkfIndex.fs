namespace Eru

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions

/// Catalog `index.md` generation, refresh and identification, shared by `eru okf init`/`fix`
/// (which write catalogs) and `eru okf validate` (which warns when one is unmarked or stale).
///
/// The machine-owned part of an index (folder list and concept table) sits between
/// `<!-- eru:catalog -->` and `<!-- /eru:catalog -->`; everything outside is left alone.
module OkfIndex =

    let openMarker = "<!-- eru:catalog -->"
    let closeMarker = "<!-- /eru:catalog -->"

    let dirOf (rel: string) =
        match rel.LastIndexOf '/' with
        | -1 -> ""
        | i -> rel.Substring(0, i)

    let parentOf (dir: string) = dirOf dir

    let indexPath (dir: string) = if dir = "" then "index.md" else dir + "/index.md"

    /// One folder's catalog: the concept rows it lists and the subfolders that have catalogs.
    type Catalog = { Dir: string; Rows: string list; Subdirs: string list }

    let private cell (s: string) = s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")

    let private conceptRow (deps: Deps) (rel: string) (content: string) =
        let fm =
            match Frontmatter.tryParse deps.ParseYamlBlock content with
            | Frontmatter.Parsed m -> m
            | _ -> Frontmatter.empty
        let file = PathUtil.fileName rel
        let title = Frontmatter.title fm |> Option.defaultValue (Path.GetFileNameWithoutExtension file)
        let typ = Frontmatter.type_ fm |> Option.defaultValue ""
        let tags = Frontmatter.tags fm |> String.concat ", "
        let stale =
            match Map.tryFind "stale_after" fm with
            | Some (Yaml.Scalar s) -> s
            | _ -> ""
        let href = file.Replace(" ", "%20")
        $"| [{cell title}]({href}) | {cell typ} | {cell tags} | {cell stale} |"

    /// Catalogs for every folder that holds concepts, plus its ancestors and the root.
    /// `current` maps bundle-relative paths to their (post-repair) contents.
    let catalogs (deps: Deps) (current: IDictionary<string, string>) : Catalog list =
        let concepts =
            current.Keys
            |> Seq.filter (fun rel -> Frontmatter.classifyContent deps.ParseYamlBlock rel current.[rel] = Frontmatter.ConceptFile)
            |> Seq.sort
            |> List.ofSeq
        let dirs = HashSet<string>([ "" ])
        for rel in concepts do
            let mutable d = dirOf rel
            while d <> "" && dirs.Add d do
                d <- parentOf d
        dirs
        |> Seq.sort
        |> List.ofSeq
        |> List.map (fun dir ->
            { Dir = dir
              Rows = concepts |> List.filter (fun c -> dirOf c = dir) |> List.map (fun c -> conceptRow deps c current.[c])
              Subdirs = dirs |> Seq.filter (fun d -> d <> "" && parentOf d = dir) |> Seq.sort |> List.ofSeq })

    let private inner (c: Catalog) =
        let sb = StringBuilder()
        if not c.Subdirs.IsEmpty then
            sb.Append("## Folders\n\n") |> ignore
            for s in c.Subdirs do
                let name = PathUtil.fileName s
                let href = name.Replace(" ", "%20")
                sb.Append($"- [{name}/]({href}/index.md)\n") |> ignore
            sb.Append("\n") |> ignore
        if not c.Rows.IsEmpty then
            sb.Append("| Concept | Type | Tags | Stale after |\n|---|---|---|---|\n") |> ignore
            for r in c.Rows do
                sb.Append(r).Append("\n") |> ignore
        sb.ToString().TrimEnd('\n')

    /// The marked catalog block for a folder.
    let catalogBlock (c: Catalog) : string =
        let body = inner c
        if body = "" then $"{openMarker}\n{closeMarker}"
        else $"{openMarker}\n\n{body}\n\n{closeMarker}"

    /// Full contents of a brand-new catalog `index.md` (the root one carries `okf_version`).
    let newIndexContent (version: string) (c: Catalog) : string =
        let heading = if c.Dir = "" then "Index" else PathUtil.fileName c.Dir
        let body = $"# {heading}\n\n{catalogBlock c}\n"
        if c.Dir = "" then $"---\nokf_version: \"{version}\"\n---\n{body}" else body

    type private MarkerState =
        | NoMarkers
        | Malformed
        | Found of start: int * endExclusive: int

    let private countOf (s: string) (sub: string) =
        let mutable n = 0
        let mutable i = s.IndexOf(sub, StringComparison.Ordinal)
        while i >= 0 do
            n <- n + 1
            i <- s.IndexOf(sub, i + sub.Length, StringComparison.Ordinal)
        n

    let private findMarkers (content: string) =
        let o = countOf content openMarker
        let c = countOf content closeMarker
        if o = 0 && c = 0 then NoMarkers
        elif o <> 1 || c <> 1 then Malformed
        else
            let s = content.IndexOf(openMarker, StringComparison.Ordinal)
            let e = content.IndexOf(closeMarker, StringComparison.Ordinal)
            if e < s then Malformed else Found(s, e + closeMarker.Length)

    type RefreshOutcome =
        /// The catalog block was out of date; carries the file's new contents.
        | Updated of string
        | UpToDate
        /// No `eru:catalog` markers: needs `--force` to adopt.
        | Unmarked
        /// A lone, reversed or duplicated marker: never rewritten.
        | BadMarkers

    let private normalizeEol (s: string) = s.Replace("\r\n", "\n")

    /// Regenerates the block between the markers, leaving the rest of the file untouched.
    let refresh (content: string) (c: Catalog) : RefreshOutcome =
        match findMarkers content with
        | NoMarkers -> Unmarked
        | Malformed -> BadMarkers
        | Found (s, e) ->
            let block = catalogBlock c
            if normalizeEol (content.Substring(s, e - s)) = block then UpToDate
            else Updated(content.Substring(0, s) + block + content.Substring e)

    let private tableHeader =
        Regex(@"^\|\s*Concept\s*\|\s*Type\s*\|\s*Tags\s*\|\s*Stale after\s*\|\s*$", RegexOptions.Compiled)
    let private tableSeparator = Regex(@"^\|[\s:|-]*-[\s:|-]*\|\s*$", RegexOptions.Compiled)
    let private folderItem = Regex(@"^- \[[^\]]+/\]\([^)]+/index\.md\)\s*$", RegexOptions.Compiled)

    /// Adopts an unmarked index: the eru-shaped `## Folders` list and/or concept table (header
    /// `| Concept | Type | Tags | Stale after |`) is replaced by the marked block; when there is
    /// none, the block is appended. Prose and frontmatter are never touched.
    let migrate (content: string) (c: Catalog) : string =
        let block = catalogBlock c
        let lines = (normalizeEol content).Split('\n')
        let isBlank (l: string) = l.Trim() = ""

        let tableAt (from: int) : (int * int) option =
            if from + 1 < lines.Length && tableHeader.IsMatch lines.[from] && tableSeparator.IsMatch lines.[from + 1] then
                let mutable e = from + 2
                while e < lines.Length && lines.[e].TrimStart().StartsWith "|" do
                    e <- e + 1
                Some(from, e)
            else None

        let foldersRegion (h: int) : (int * int) option =
            let mutable j = h + 1
            while j < lines.Length && isBlank lines.[j] do
                j <- j + 1
            let itemsStart = j
            while j < lines.Length && folderItem.IsMatch lines.[j] do
                j <- j + 1
            if j = itemsStart then None
            else
                let listEnd = j
                let mutable k = listEnd
                while k < lines.Length && isBlank lines.[k] do
                    k <- k + 1
                match tableAt k with
                | Some (_, e) -> Some(h, e)
                | None -> Some(h, listEnd)

        let region =
            let viaFolders =
                lines
                |> Array.tryFindIndex (fun l -> l.Trim() = "## Folders")
                |> Option.bind foldersRegion
            match viaFolders with
            | Some r -> Some r
            | None -> lines |> Array.indexed |> Array.tryPick (fun (i, _) -> tableAt i)

        match region with
        | Some (s, e) -> String.Join("\n", Array.concat [ lines.[.. s - 1]; block.Split('\n'); lines.[e ..] ])
        | None -> content.TrimEnd() + "\n\n" + block + "\n"

    /// Why a file named `index.md` is not treated as an OKF catalog index.
    type NotIndexReason =
        | OutsideBundle
        | ForeignFrontmatter
        | IgnoredPath
        | EmptyFolder

    let reasonText =
        function
        | OutsideBundle -> "not an OKF index: outside a bundle (the root index.md has no `okf_version`; run `eru okf fix` first)"
        | ForeignFrontmatter -> "not an OKF index: frontmatter does not match the OKF index shape"
        | IgnoredPath -> "not an OKF index: path is ignored by `okf.ignorePatterns`"
        | EmptyFolder -> "not an OKF index: the folder has no concepts or subfolders to catalog"

    /// True when the bundle-root `index.md` carries `okf_version`, which is what makes a directory a bundle.
    let isBundleRoot (rootIndexFrontmatter: Frontmatter.ParseOutcome) =
        match rootIndexFrontmatter with
        | Frontmatter.Parsed m -> (Frontmatter.okfVersion m).IsSome
        | _ -> false

    /// Decides whether an existing `index.md` is a genuine OKF catalog index that eru may rewrite.
    let classify (rootIsBundle: bool) (isRoot: bool) (fm: Frontmatter.ParseOutcome) (c: Catalog) : Result<unit, NotIndexReason> =
        if not rootIsBundle then Error OutsideBundle
        else
            let shapeOk =
                match fm, isRoot with
                | Frontmatter.NoBlock, false -> true
                | Frontmatter.Parsed m, true -> (m |> Map.toList |> List.map fst) = [ "okf_version" ]
                | _ -> false
            if not shapeOk then Error ForeignFrontmatter
            elif c.Rows.IsEmpty && c.Subdirs.IsEmpty then Error EmptyFolder
            else Ok()
