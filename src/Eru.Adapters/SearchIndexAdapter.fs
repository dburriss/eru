namespace Eru.Adapters

open System
open System.IO
open System.Security.Cryptography

type IndexWord = { Word: string; Lines: int list }
type IndexLine = { Num: int; Text: string }
type FileWordIndex = { Hash: string; Words: IndexWord list; Lines: IndexLine list }

module SearchIndexAdapter =

    let stopWords =
        Set.ofList [
            "a"; "an"; "the"; "and"; "or"; "but"; "if"; "in"; "on"; "at"; "to";
            "for"; "of"; "with"; "by"; "from"; "as"; "is"; "are"; "was"; "were";
            "be"; "been"; "being"; "have"; "has"; "had"; "do"; "does"; "did";
            "will"; "would"; "could"; "should"; "may"; "might"; "that"; "this";
            "it"; "its"; "so"; "up"; "out"; "no"; "not"; "all"; "any"; "each"
        ]

    let tokenize (text: string) : string list =
        text.Split([| ' '; '\t'; '\r'; '\n'; '.'; ','; ':'; ';'; '('; ')'; '['; ']'; '{'; '}' |],
                   StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun w -> w.ToLowerInvariant().Trim('"', '\'', '`', '*', '#'))
        |> Array.filter (fun w -> w.Length > 1 && not (Set.contains w stopWords))
        |> Array.toList

    let private indexFilePath (absPath: string) : string =
        let bytes = Text.Encoding.UTF8.GetBytes absPath
        let hex   = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
        Path.Combine(Paths.searchIndexDir(), $"{hex}.json")

    let private hashFileContent (content: string) : string =
        let bytes = Text.Encoding.UTF8.GetBytes content
        let hex   = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
        $"sha256:{hex}"

    let tryLoad (absPath: string) : FileWordIndex option =
        let idxPath = indexFilePath absPath
        if not (File.Exists idxPath) then None
        else
            try
                match Serialization.deserialize<FileWordIndex>(File.ReadAllText idxPath) with
                | Ok idx ->
                    let currentHash = hashFileContent (File.ReadAllText absPath)
                    if idx.Hash = currentHash then Some idx else None
                | Error _ -> None
            with _ -> None

    let build (absPath: string) : FileWordIndex option =
        try
            let content = File.ReadAllText absPath
            if Eru.Patterns.isBinaryContent content then None
            else
                let hash     = hashFileContent content
                let rawLines = content.Split('\n')
                let wordMap =
                    rawLines
                    |> Array.mapi (fun i line ->
                        let lineNum = i + 1
                        tokenize line |> List.map (fun w -> w, lineNum) |> List.toArray)
                    |> Array.concat
                    |> Array.groupBy fst
                    |> Array.map (fun (w, pairs) ->
                        { Word = w; Lines = pairs |> Array.map snd |> Array.distinct |> Array.toList })
                    |> Array.sortBy (fun e -> e.Word)
                    |> Array.toList
                let indexedLineNums = wordMap |> List.collect (fun e -> e.Lines) |> Set.ofList
                let lines =
                    rawLines
                    |> Array.mapi (fun i line -> { Num = i + 1; Text = line.Trim() })
                    |> Array.filter (fun l -> Set.contains l.Num indexedLineNums)
                    |> Array.toList
                let idx     = { Hash = hash; Words = wordMap; Lines = lines }
                let idxPath = indexFilePath absPath
                let dir     = Path.GetDirectoryName idxPath
                if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
                File.WriteAllText(idxPath, Serialization.serialize idx)
                Some idx
        with _ -> None

    let getOrBuild (absPath: string) : FileWordIndex option =
        match tryLoad absPath with
        | Some idx -> Some idx
        | None     -> build absPath
