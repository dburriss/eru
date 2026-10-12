module Eru.Site.MarkdownRenderer

open System.Text.RegularExpressions
open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines

let private pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

let private stripFrontmatter (markdown: string) : string =
    if not (markdown.StartsWith("---")) then markdown
    else
        // find the end of the opening --- line
        let firstNl = markdown.IndexOfAny([| '\n'; '\r' |], 3)
        if firstNl < 0 then markdown
        else
            // search for closing --- after the first line
            let searchFrom = if markdown.[firstNl] = '\r' && firstNl + 1 < markdown.Length && markdown.[firstNl + 1] = '\n' then firstNl + 2 else firstNl + 1
            let closeIdx = markdown.IndexOf("\n---", searchFrom)
            if closeIdx < 0 then markdown
            else
                // skip past \n---
                let afterClose = closeIdx + 4
                // skip optional \r\n or \n after the closing delimiter
                let body =
                    if afterClose < markdown.Length && markdown.[afterClose] = '\r' then
                        let next = afterClose + 1
                        if next < markdown.Length && markdown.[next] = '\n' then afterClose + 2 else afterClose + 1
                    elif afterClose < markdown.Length && markdown.[afterClose] = '\n' then afterClose + 1
                    else afterClose
                markdown.Substring(body)

let render (markdown: string) : string =
    Markdown.ToHtml(stripFrontmatter markdown, pipeline)

/// Renders Markdown, letting `rewrite` replace any link target (None leaves it alone).
let renderWith (rewrite: string -> string option) (markdown: string) : string =
    let doc = Markdown.Parse(stripFrontmatter markdown, pipeline)
    for link in doc.Descendants<LinkInline>() |> Seq.toList do
        if not (isNull link.Url) && not link.IsImage then
            match rewrite link.Url with
            | Some url -> link.Url <- url
            | None -> ()
    doc.ToHtml(pipeline)

let private headingRx =
    Regex("""<h([1-6])\s+id="([^"]*)"[^>]*>(.*?)</h\1>""", RegexOptions.Singleline ||| RegexOptions.Compiled)

let private tagRx = Regex("<[^>]+>", RegexOptions.Compiled)

/// (level, id, plain-text label) for every heading that carries an id (Markdig adds ids
/// through its auto-identifier extension).
let headings (html: string) : (int * string * string) list =
    headingRx.Matches(html)
    |> Seq.map (fun m ->
        int m.Groups.[1].Value, m.Groups.[2].Value, System.Net.WebUtility.HtmlDecode(tagRx.Replace(m.Groups.[3].Value, "")).Trim())
    |> List.ofSeq
