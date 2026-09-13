module Eru.Adapters.MarkdownLinkAdapter

open Markdig
open Markdig.Syntax
open Markdig.Syntax.Inlines

let private pipeline = MarkdownPipelineBuilder().UseAdvancedExtensions().Build()

let extractLinks (content: string) : string list =
    let document = Markdown.Parse(content, pipeline)

    let links = ResizeArray<string>()

    let rec walkInlines (container: Inline) =
        match container with
        | :? LinkInline as link ->
            if not (System.String.IsNullOrEmpty link.Url) then
                links.Add link.Url
            link |> Seq.iter walkInlines
        | :? ContainerInline as ci ->
            ci |> Seq.iter walkInlines
        | _ -> ()

    let rec walkBlocks (block: Block) =
        match block with
        | :? ContainerBlock as cb ->
            cb |> Seq.iter walkBlocks
        | :? LeafBlock as lb ->
            match lb.Inline with
            | null -> ()
            | inlines -> inlines |> Seq.iter walkInlines
        | _ -> ()

    document |> Seq.iter walkBlocks

    links |> List.ofSeq
