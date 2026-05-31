namespace Eru

type LockEntry = {
    LocalPath: string
    SourceName: string
    RemotePath: string
    ContentHash: string
}

module LockFile =
    let private versionComment = "# eru.lock v1"

    let parse (content: string) : Result<LockEntry list, string> =
        let lines =
            content.Split('\n')
            |> Array.toList
            |> List.filter (fun l ->
                let trimmed = l.Trim()
                trimmed <> "" && not (trimmed.StartsWith('#')))

        let folder (acc: Result<LockEntry list, string>) (line: string) =
            match acc with
            | Error e -> Error e
            | Ok entries ->
                let parts : string[] = line.Split('\t')
                if parts.Length <> 3 then
                    Error $"Malformed lock entry (expected 3 tab-separated fields): {line}"
                else
                    let origin : string = parts.[1]
                    let colonIdx = origin.IndexOf(':')
                    if colonIdx < 0 then
                        Error $"Malformed origin in lock entry (expected sourceName:remotePath): {origin}"
                    else
                        Ok ({
                            LocalPath   = parts.[0].Trim()
                            SourceName  = origin.[..colonIdx - 1]
                            RemotePath  = origin.[colonIdx + 1..]
                            ContentHash = parts.[2].Trim()
                        } :: entries)

        List.fold folder (Ok []) lines |> Result.map List.rev

    let findByLocalPath (path: string) (entries: LockEntry list) : LockEntry option =
        entries |> List.tryFind (fun e -> e.LocalPath = path)

    let findByPathHash (prefix: string) (entries: LockEntry list) : Result<LockEntry, string> =
        let matches = entries |> List.filter (fun e -> (Patterns.pathShortHash e.RemotePath).StartsWith prefix)
        match matches with
        | []  -> Error $"no tracked file has path hash starting with '{prefix}'"
        | [e] -> Ok e
        | _   -> Error $"ambiguous hash '{prefix}' — {matches.Length} files match, be more specific"

    let write (entries: LockEntry list) : string =
        let sorted = entries |> List.sortBy (fun e -> e.LocalPath)
        let lines =
            sorted
            |> List.map (fun e ->
                $"{e.LocalPath}\t{e.SourceName}:{e.RemotePath}\t{e.ContentHash}")
        String.concat "\n" (versionComment :: lines) + "\n"
