namespace Eru

open System
open System.Text
open System.Text.RegularExpressions

/// Pure link extraction for `eru okf validate`: the page, image and wikilink references in a
/// markdown note, with line numbers and verbatim text so a broken one can be found and edited.
module OkfLinks =

    type Kind =
        | Page
        | Image
        | Wiki

    type Link =
        { Kind: Kind
          /// Link target without any `#fragment` or `?query`; percent-decoded for `Page`/`Image`.
          Target: string
          Fragment: string option
          /// 1-based line in the file the link appears on.
          Line: int
          /// The link exactly as written, e.g. `[text](foo.md)`.
          Text: string }

    let private wikilinkPattern =
        Regex(@"(!?)\[\[\s*([^\[\]|]+?)\s*(?:\|\s*([^\[\]]+?)\s*)?\]\]", RegexOptions.Compiled)

    // One level of nested brackets in the text so `[![alt](img.png)](page.md)` is seen as a page link.
    let private linkPattern =
        Regex(@"(!?)\[((?:[^\[\]]|\[[^\]]*\])*)\]\(\s*(<[^>]*>|[^)\s]+)(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)", RegexOptions.Compiled)

    let private inlineCode = Regex(@"(`+)(?:(?!\1).)+?\1", RegexOptions.Compiled)
    let private uriScheme = Regex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:", RegexOptions.Compiled)
    let private fence = Regex(@"^\s{0,3}(`{3,}|~{3,})", RegexOptions.Compiled)
    let private heading = Regex(@"^\s{0,3}#{1,6}\s+(.*?)(?:\s+#+)?\s*$", RegexOptions.Compiled)

    let private blank (s: string) = String(' ', s.Length)

    /// The lines of `content` with frontmatter and fenced code blanked out (line numbers are kept)
    /// and, when `stripInline`, inline code spans blanked too.
    let private proseLines (stripInline: bool) (content: string) : string[] =
        let lines = content.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let mutable i = 0
        if lines.Length > 0 && lines.[0].Trim() = "---" then
            match lines |> Array.skip 1 |> Array.tryFindIndex (fun l -> l.Trim() = "---") with
            | Some close ->
                for j in 0 .. close + 1 do lines.[j] <- ""
                i <- close + 2
            | None -> ()
        let mutable fenceMarker : string option = None
        while i < lines.Length do
            let line = lines.[i]
            let m = fence.Match line
            match fenceMarker with
            | Some marker ->
                if m.Success && m.Groups.[1].Value.[0] = marker.[0] && m.Groups.[1].Value.Length >= marker.Length then
                    fenceMarker <- None
                lines.[i] <- ""
            | None when m.Success ->
                fenceMarker <- Some m.Groups.[1].Value
                lines.[i] <- ""
            | None ->
                if stripInline then lines.[i] <- inlineCode.Replace(line, fun mm -> blank mm.Value)
            i <- i + 1
        lines

    let private splitTarget (raw: string) : (string * string option) option =
        let t = raw.Trim().TrimStart('<').TrimEnd('>').Trim()
        if t = "" || t.StartsWith "#" || t.StartsWith "//" || uriScheme.IsMatch t then None
        else
            let beforeFragment, fragment =
                match t.IndexOf '#' with
                | -1 -> t, None
                | h ->
                    let f = t.Substring(h + 1)
                    t.Substring(0, h), (if f = "" then None else Some f)
            let path =
                match beforeFragment.IndexOf '?' with
                | -1 -> beforeFragment
                | q -> beforeFragment.Substring(0, q)
            let decode (s: string) = try Uri.UnescapeDataString s with _ -> s
            if path = "" then None else Some (decode path, fragment |> Option.map decode)

    let rec private scanLine (lineNo: int) (text: string) (acc: ResizeArray<Link>) : unit =
        // Wikilinks first; blank them so the markdown-link pattern cannot see inside them.
        let afterWiki =
            wikilinkPattern.Replace(text, fun m ->
                // `![[file]]` is an embed, not a note reference; leave it alone.
                if m.Groups.[1].Value = "" then
                    let raw = m.Groups.[2].Value
                    let target, fragment =
                        match raw.IndexOf '#' with
                        | -1 -> raw, None
                        | h -> raw.Substring(0, h).Trim(), (let f = raw.Substring(h + 1).Trim() in if f = "" then None else Some f)
                    if target <> "" then
                        acc.Add { Kind = Wiki; Target = target; Fragment = fragment; Line = lineNo; Text = m.Value }
                blank m.Value)
        for m in linkPattern.Matches afterWiki do
            match splitTarget m.Groups.[3].Value with
            | Some (target, fragment) ->
                let kind = if m.Groups.[1].Value = "!" then Image else Page
                acc.Add { Kind = kind; Target = target; Fragment = fragment; Line = lineNo; Text = m.Value }
            | None -> ()
            // An image nested inside a link's text is its own reference.
            let inner = m.Groups.[2].Value
            if inner.Contains "](" then scanLine lineNo inner acc

    /// Page, image and wikilink references in a markdown file's content, ordered by line. Frontmatter,
    /// fenced code and inline code are ignored; external (`scheme:`) and anchor-only links are skipped.
    let extract (content: string) : Link list =
        let acc = ResizeArray<Link>()
        proseLines true content
        |> Array.iteri (fun i line -> if line.Trim() <> "" then scanLine (i + 1) line acc)
        acc |> List.ofSeq

    // --- heading anchors -------------------------------------------------------------------

    let private stripMarkup (s: string) =
        let noImages = Regex.Replace(s, @"!\[([^\]]*)\]\([^)]*\)", "$1")
        let noLinks = Regex.Replace(noImages, @"\[([^\]]*)\]\([^)]*\)", "$1")
        noLinks.Replace("`", "").Replace("*", "").Trim()

    /// GitHub's heading anchor: lowercase, drop punctuation except `-` and `_`, spaces to `-`.
    let githubSlug (text: string) : string =
        let sb = StringBuilder()
        for c in (stripMarkup text).ToLowerInvariant() do
            if Char.IsLetterOrDigit c || c = '-' || c = '_' then sb.Append c |> ignore
            elif c = ' ' then sb.Append '-' |> ignore
        sb.ToString()

    /// Markdig's auto-identifier (what `eru site generate` emits): lowercase letters, digits and
    /// `-_.`, whitespace runs to a single `-`, other punctuation dropped, edge dashes trimmed.
    let markdigSlug (text: string) : string =
        let sb = StringBuilder()
        for c in (stripMarkup text).ToLowerInvariant() do
            if Char.IsLetterOrDigit c || c = '-' || c = '_' || c = '.' then sb.Append c |> ignore
            elif Char.IsWhiteSpace c && (sb.Length = 0 || sb.[sb.Length - 1] <> '-') then sb.Append '-' |> ignore
        sb.ToString().Trim('-')

    /// Every anchor a link may use to reach a heading in `content`, in either slug style.
    let headingAnchors (content: string) : Set<string> =
        proseLines false content
        |> Array.choose (fun line ->
            let m = heading.Match line
            if m.Success then Some m.Groups.[1].Value else None)
        |> Array.collect (fun h -> [| githubSlug h; markdigSlug h |])
        |> Array.filter (fun s -> s <> "")
        |> Set.ofArray

    /// True when `fragment` names one of `anchors`. A `-N` suffix (how repeated headings are
    /// disambiguated) is accepted when the unsuffixed anchor exists.
    let anchorExists (anchors: Set<string>) (fragment: string) : bool =
        let f = fragment.Trim().ToLowerInvariant()
        anchors.Contains f
        || (let m = Regex.Match(f, @"^(.+)-\d+$") in m.Success && anchors.Contains m.Groups.[1].Value)
