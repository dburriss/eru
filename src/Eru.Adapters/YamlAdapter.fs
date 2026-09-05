namespace Eru.Adapters

open System.IO
open YamlDotNet.RepresentationModel

module YamlAdapter =

    let rec private convert (node: YamlNode) : Eru.Yaml.Node =
        match node with
        | :? YamlScalarNode as s -> Eru.Yaml.Scalar (if isNull s.Value then "" else s.Value)
        | :? YamlSequenceNode as sq ->
            sq.Children |> Seq.map convert |> List.ofSeq |> Eru.Yaml.Seq
        | :? YamlMappingNode as m ->
            m.Children
            |> Seq.map (fun kv ->
                let key =
                    match kv.Key with
                    | :? YamlScalarNode as ks -> ks.Value
                    | other -> other.ToString()
                key, convert kv.Value)
            |> List.ofSeq
            |> Eru.Yaml.Map
        | _ -> Eru.Yaml.Null

    /// Parses a YAML mapping block's text (the content between "---" delimiters)
    /// into a generic Eru.Yaml.Node tree. Malformed YAML -> Error, never throws.
    let parse (yamlText: string) : Result<Eru.Yaml.Node, string> =
        try
            let stream = YamlStream()
            use reader = new StringReader(yamlText)
            stream.Load(reader)
            if stream.Documents.Count = 0 then Ok Eru.Yaml.Null
            else Ok (convert stream.Documents.[0].RootNode)
        with ex -> Error ex.Message
