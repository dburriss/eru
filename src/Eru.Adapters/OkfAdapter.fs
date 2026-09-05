namespace Eru.Adapters

open System.IO

module OkfAdapter =

    let listMarkdownFiles (root: string) : Result<string list, string> =
        try
            Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            |> Seq.map (fun f -> Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            |> Seq.filter (fun rel -> not (rel.Split('/') |> Array.exists (fun seg -> seg = ".git")))
            |> Seq.sort
            |> Seq.toList
            |> Ok
        with ex -> Error ex.Message
