module Eru.Adapters.MarkdownLinkAdapter

open System.Text.RegularExpressions
open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines
open Eru

let private pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

let private wikilinkPattern =
    Regex(@"\[\[\s*([^\[\]|]+?)\s*(?:\|\s*(.+?)\s*)?\]\]", RegexOptions.Compiled)

let rec private inlineToPlainText (inline_: Inline) : string =
    match inline_ with
    | :? LiteralInline as lit -> lit.Content.ToString()
    | :? CodeInline as code -> code.Content
    | :? LineBreakInline -> " "
    | :? ContainerInline as ci ->
        ci |> Seq.map inlineToPlainText |> String.concat ""
    | _ -> ""

let extractLinks (content: string) : ExtractedLink list =
    let document = Markdown.Parse(content, pipeline)

    let markdownLinks = ResizeArray<ExtractedLink>()
    let excludedSpans = ResizeArray<int * int>()

    let rec walkInlines (container: Inline) =
        match container with
        | :? LinkInline as link ->
            if not (System.String.IsNullOrEmpty link.Url) then
                let description =
                    let text = inlineToPlainText link |> fun s -> s.Trim()
                    if text = "" then None else Some text
                markdownLinks.Add { Target = link.Url; Description = description; Kind = MarkdownLink }
            link |> Seq.iter walkInlines
        | :? CodeInline as code ->
            excludedSpans.Add(code.Span.Start, code.Span.End)
        | :? ContainerInline as ci ->
            ci |> Seq.iter walkInlines
        | _ -> ()

    let rec walkBlocks (block: Block) =
        match block with
        | :? CodeBlock as cb ->
            excludedSpans.Add(cb.Span.Start, cb.Span.End)
        | :? ContainerBlock as cb ->
            cb |> Seq.iter walkBlocks
        | :? LeafBlock as lb ->
            match lb.Inline with
            | null -> ()
            | inlines -> inlines |> Seq.iter walkInlines
        | _ -> ()

    document |> Seq.iter walkBlocks

    let isExcluded (index: int) =
        excludedSpans |> Seq.exists (fun (s, e) -> index >= s && index <= e)

    let wikilinks =
        wikilinkPattern.Matches(content)
        |> Seq.cast<Match>
        |> Seq.filter (fun m -> not (isExcluded m.Index))
        |> Seq.map (fun m ->
            let target = m.Groups.[1].Value.Trim()
            let description =
                if m.Groups.[2].Success then
                    let d = m.Groups.[2].Value.Trim()
                    if d = "" then None else Some d
                else None
            { Target = target; Description = description; Kind = Wikilink })
        |> List.ofSeq

    (markdownLinks |> List.ofSeq) @ wikilinks
