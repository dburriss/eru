module Eru.Site.MarkdownRenderer

open Markdig

let private pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

let render (markdown: string) : string =
    Markdown.ToHtml(markdown, pipeline)
