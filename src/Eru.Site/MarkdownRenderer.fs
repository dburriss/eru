module Eru.Site.MarkdownRenderer

open Markdig

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
