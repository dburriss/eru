namespace Eru

module Frontmatter =

    type Parsed = {
        Description : string option
        Tags        : string list
    }

    let empty = { Description = None; Tags = [] }

    let private stripQuotes (s: string) =
        let s = s.Trim()
        if s.Length >= 2 &&
           ((s.[0] = '"'  && s.[s.Length-1] = '"')  ||
            (s.[0] = '\'' && s.[s.Length-1] = '\'')) then
            s.[1..s.Length-2]
        else s

    let private parseInlineTags (value: string) : string list option =
        let v = value.Trim()
        if v.StartsWith("[") && v.EndsWith("]") then
            v.[1..v.Length-2].Split(',')
            |> Array.map (fun t -> stripQuotes t)
            |> Array.filter (fun t -> t <> "")
            |> Array.toList
            |> Some
        else None

    let parse (content: string) : Parsed =
        let lines = content.Split([| "\r\n"; "\n" |], System.StringSplitOptions.None)
        if lines.Length < 2 || lines.[0].Trim() <> "---" then
            empty
        else
            // Find closing "---"; closeIdx is 0-based within Array.skip 1
            match lines |> Array.skip 1 |> Array.tryFindIndex (fun l -> l.Trim() = "---") with
            | None -> empty
            | Some closeIdx ->
                // Frontmatter body is lines.[1 .. closeIdx] (exclusive of both delimiters)
                let fmLines = lines.[1 .. closeIdx]

                let description =
                    fmLines
                    |> Array.tryPick (fun l ->
                        let l = l.Trim()
                        if l.StartsWith("description:") then
                            let v = l.["description:".Length..].Trim() |> stripQuotes
                            if v = "" then None else Some v
                        else None)

                let tags =
                    // Try inline: tags: [a, b, c]
                    match fmLines |> Array.tryPick (fun l ->
                        let l = l.Trim()
                        if l.StartsWith("tags:") then
                            parseInlineTags (l.["tags:".Length..].Trim())
                        else None) with
                    | Some t -> t
                    | None ->
                        // Try block list: "tags:" followed by "  - item" lines
                        match fmLines |> Array.tryFindIndex (fun l -> l.Trim() = "tags:") with
                        | None -> []
                        | Some idx ->
                            fmLines
                            |> Array.skip (idx + 1)
                            |> Array.takeWhile (fun l -> l.TrimStart().StartsWith("-"))
                            |> Array.map (fun l -> l.TrimStart().TrimStart('-').Trim() |> stripQuotes)
                            |> Array.filter (fun t -> t <> "")
                            |> Array.toList

                { Description = description; Tags = tags }
