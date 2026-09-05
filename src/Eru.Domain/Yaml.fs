namespace Eru

module Yaml =
    type Node =
        | Scalar of string
        | Seq    of Node list
        | Map    of (string * Node) list
        | Null

    /// Parses a raw YAML mapping block's text into a generic tree.
    /// Injected — the real implementation (YamlDotNet-backed) lives in Eru.Adapters.
    type Parse = string -> Result<Node, string>
