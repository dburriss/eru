namespace Eru

/// Separator-agnostic path helpers. Domain paths are always '/'-separated
/// (remote paths, bundle-relative paths), so System.IO.Path — which emits '\'
/// on Windows — must not be used to build or split them.
module PathUtil =

    let private norm (p: string) = p.Replace('\\', '/')

    let combineAll (parts: string list) : string =
        parts
        |> List.filter (fun p -> p <> "")
        |> List.fold (fun acc p ->
            let p = norm p
            if acc = "" then p
            elif p.StartsWith "/" then p
            else acc.TrimEnd('/') + "/" + p) ""

    let dirName (p: string) : string =
        let p = norm p
        match p.LastIndexOf '/' with
        | -1 -> ""
        | 0 -> "/"
        | i -> p.Substring(0, i)

    let fileName (p: string) : string =
        let p = norm p
        p.Substring(p.LastIndexOf '/' + 1)

/// Call-compatible stand-in for Path.Combine that always joins with '/'.
type PathJoin =
    static member Combine([<System.ParamArray>] parts: string[]) : string = PathUtil.combineAll (List.ofArray parts)
