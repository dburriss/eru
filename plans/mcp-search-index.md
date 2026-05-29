# Plan: Indexed Word Search for MCP `search_knowledge`

## Context

`search_knowledge` reads every candidate file from disk on every MCP call. We want:
1. A **per-file inverted word index** that avoids re-reading unchanged files and returns all matching lines as excerpts.
2. A **simple scan** baseline for comparison — both wired up, one line to switch between them.

The current single-excerpt approach is replaced: a file is a hit if **any** query word appears in it (OR), and **all** matching lines are returned as excerpts so the caller sees full context rather than just the first hit.

---

## Semantics

- **Query splitting**: `"oauth token refresh"` → `["oauth", "token", "refresh"]` after stop word removal.
- **File match (OR)**: file is returned if ANY query word appears somewhere in it (path, description, or content).
- **Excerpts**: ALL matching lines are returned — every line in the file that contains at least one query word, deduplicated and sorted by line number.
- **Path/description match**: treated as a pre-filter; if any query word hits the path or description, include those lines too.

Example output for `search_knowledge "oauth token"`:
```
[collection] my-source/docs/authentication.md [tags: dotnet]
  > ## OAuth Authentication Flow
  > The OAuth token is validated against the issuer before granting access
  > Refresh token expires after 24 hours; request a new one via /token/refresh

[lock] src/Auth.fs (from platform:dotnet/Auth.fs)
  > let validateOAuthToken (issuer: string) =
  > member _.Token = jwt.RawData
```

---

## Per-File Index Format

One JSON file per source file, stored at `~/.cache/eru/index/{sha256(absPath)}.json` (XDG-aware directory). Named by SHA256 of the absolute path — uniform across all three source types, no path manipulation required.

```json
{
  "hash": "sha256:abc123def456",
  "words": [
    { "word": "authenticate", "lines": [14] },
    { "word": "oauth",        "lines": [3, 14] },
    { "word": "token",        "lines": [14, 27] }
  ],
  "lines": [
    { "num": 3,  "text": "## OAuth Authentication Flow" },
    { "num": 14, "text": "The OAuth token is validated against the issuer" },
    { "num": 27, "text": "Refresh token expires after 24 hours" }
  ]
}
```

- `hash`: SHA256 of the source file's content — used for invalidation on load.
- `words`: only non-stop-word tokens; each entry maps a word to the line numbers it appears on. Sorted by word.
- `lines`: only lines that contain at least one indexed word; sorted by line number. Blank lines, stop-word-only lines, and binary content are excluded.
- Types use lists of records (not `Map<K,V>`) so `System.Text.Json` serializes them correctly with the existing `Serialization` module.

**Invalidation**: on each query, load the file's index, compare stored `hash` with `Deps.HashContent(File.ReadAllText absPath)`. If mismatch (or index absent) → rebuild. No mtime tricks — content hash is authoritative.

---

## Stop Words

A hardcoded F# `Set<string>` of common English filler words — applied when building the index and when parsing the query. Conservative list to avoid stripping meaningful technical terms:

```fsharp
let stopWords = Set.ofList [
    "a"; "an"; "the"; "and"; "or"; "but"; "if"; "in"; "on"; "at"; "to";
    "for"; "of"; "with"; "by"; "from"; "as"; "is"; "are"; "was"; "were";
    "be"; "been"; "being"; "have"; "has"; "had"; "do"; "does"; "did";
    "will"; "would"; "could"; "should"; "may"; "might"; "that"; "this";
    "it"; "its"; "so"; "up"; "out"; "no"; "not"; "all"; "any"; "each"
]
```

Deliberately excluded from stop words: `can`, `get`, `set`, `run`, `use`, `new`, `null`, `true`, `false` — these are meaningful in code/config files.

---

## Files to Create / Modify

### 1. `src/Eru.Mcp/SearchTypes.fs` — new file (shared types + backend signature)

Defines the DU, the candidate record, and the agreed function signature all backends must implement. Placed first in `Eru.Mcp.fsproj` compile order.

```fsharp
namespace Eru.Mcp

type KnowledgeSource = Cache | Lock | Local

type CandidateFile = {
    AbsPath     : string
    RelPath     : string
    Source      : KnowledgeSource
    SourceName  : string option
    Tags        : string list
    Description : string option
}

// Agreed signature every backend module must expose as `search`
type SearchFn = string list -> CandidateFile list -> (CandidateFile * string list) list
```

### 3. `src/Eru.Adapters/Paths.fs` — add `searchIndexDir()`

```fsharp
let searchIndexDir () =
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
        let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
        IO.Path.Combine(localAppData, "eru", "index")
    else
        let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
        let cacheHome =
            if xdgCache <> null && xdgCache <> "" then xdgCache
            else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
        IO.Path.Combine(cacheHome, "eru", "index")
```

### 4. `src/Eru.Adapters/SearchIndexAdapter.fs` — new file

Types and I/O. Placed after `Serialization.fs` in compile order.

```fsharp
namespace Eru.Adapters

open System
open System.IO
open System.Security.Cryptography

// Serializable types (lists of records, not Map<K,V>, for System.Text.Json compatibility)
type IndexWord = { Word: string; Lines: int list }
type IndexLine = { Num: int; Text: string }
type FileWordIndex = { Hash: string; Words: IndexWord list; Lines: IndexLine list }

module SearchIndexAdapter =

    let stopWords = Set.ofList [ "a"; "an"; "the"; "and"; "or"; ... ]

    let tokenize (text: string) : string list =
        text.Split([|' ';'\t';'\r';'\n';'.';',';':';';';'(';')';'[';']';'{';'}'|],
                   StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun w -> w.ToLowerInvariant().Trim('"', '\'', '`', '*', '#'))
        |> Array.filter (fun w -> w.Length > 1 && not (Set.contains w stopWords))
        |> Array.toList

    // Path for a file's index, keyed by SHA256 of its absolute path
    let private indexFilePath (absPath: string) : string =
        let bytes = Text.Encoding.UTF8.GetBytes absPath
        let hex   = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
        Path.Combine(Paths.searchIndexDir(), $"{hex}.json")

    let private hashFileContent (absPath: string) : string =
        let bytes = Text.Encoding.UTF8.GetBytes(File.ReadAllText absPath)
        let hex   = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
        $"sha256:{hex}"

    // Load index for a file; returns None if absent or hash mismatch (stale)
    let tryLoad (absPath: string) : FileWordIndex option =
        let idxPath = indexFilePath absPath
        if not (File.Exists idxPath) then None
        else
            try
                match Serialization.deserialize<FileWordIndex>(File.ReadAllText idxPath) with
                | Ok idx ->
                    let currentHash = hashFileContent absPath
                    if idx.Hash = currentHash then Some idx else None
                | Error _ -> None
            with _ -> None

    // Build and persist an index for a file
    let build (absPath: string) : FileWordIndex option =
        try
            let content = File.ReadAllText absPath
            if Eru.Patterns.isBinaryContent content then None
            else
                let hash  = $"sha256:{Convert.ToHexString(SHA256.HashData(Text.Encoding.UTF8.GetBytes content)).ToLowerInvariant()}"
                let rawLines = content.Split('\n')
                // Build word -> line numbers map
                let wordMap =
                    rawLines
                    |> Array.mapi (fun i line ->
                        let lineNum = i + 1
                        tokenize line |> List.map (fun w -> w, lineNum))
                    |> Array.concat
                    |> Array.groupBy fst
                    |> Array.map (fun (w, pairs) -> { Word = w; Lines = pairs |> Array.map snd |> Array.distinct |> Array.toList })
                    |> Array.sortBy (fun e -> e.Word)
                    |> Array.toList
                // Only store lines that have at least one indexed word
                let indexedLineNums = wordMap |> List.collect (fun e -> e.Lines) |> Set.ofList
                let lines =
                    rawLines
                    |> Array.mapi (fun i line -> { Num = i + 1; Text = line.Trim() })
                    |> Array.filter (fun l -> Set.contains l.Num indexedLineNums)
                    |> Array.toList
                let idx = { Hash = hash; Words = wordMap; Lines = lines }
                let idxPath = indexFilePath absPath
                let dir = Path.GetDirectoryName idxPath
                if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
                File.WriteAllText(idxPath, Serialization.serialize idx)
                Some idx
        with _ -> None

    // Load (from cache) or build (if stale/absent) an index for a file
    let getOrBuild (absPath: string) : FileWordIndex option =
        match tryLoad absPath with
        | Some idx -> Some idx
        | None     -> build absPath
```

### 5. `src/Eru.Adapters/Eru.Adapters.fsproj`

Add after `Serialization.fs`:
```xml
<Compile Include="SearchIndexAdapter.fs" />
```

### 6. `src/Eru.Mcp/SimpleScan.fs` — new file

```fsharp
module Eru.Mcp.SimpleScan

open System.IO

let search : SearchFn =
    fun termList candidates ->
        candidates |> List.choose (fun f ->
            try
                let lines     = File.ReadAllLines f.AbsPath
                let pathLower = f.RelPath.ToLowerInvariant()
                let pathHits  = termList |> List.exists pathLower.Contains
                let matchingLines =
                    lines
                    |> Array.filter (fun l ->
                        let ll = l.ToLowerInvariant()
                        termList |> List.exists ll.Contains)
                    |> Array.map (fun l -> l.Trim())
                    |> Array.toList
                if pathHits || not matchingLines.IsEmpty then Some (f, matchingLines)
                else None
            with _ -> None)
```

### 7. `src/Eru.Mcp/IndexedSearch.fs` — new file

```fsharp
module Eru.Mcp.IndexedSearch

open Eru.Adapters

let search : SearchFn =
    fun termList candidates ->
        candidates |> List.choose (fun f ->
            let pathLower = f.RelPath.ToLowerInvariant()
            let pathHits  = termList |> List.exists pathLower.Contains
            let queryTokens = termList |> List.collect SearchIndexAdapter.tokenize |> List.distinct
            match SearchIndexAdapter.getOrBuild f.AbsPath with
            | None when not pathHits -> None
            | idxOpt ->
                let excerpts =
                    match idxOpt with
                    | None -> []
                    | Some idx ->
                        queryTokens
                        |> List.collect (fun t ->
                            idx.Words
                            |> List.tryFind (fun w -> w.Word = t)
                            |> Option.map (fun w -> w.Lines)
                            |> Option.defaultValue [])
                        |> List.distinct
                        |> List.sort
                        |> List.choose (fun n ->
                            idx.Lines |> List.tryFind (fun l -> l.Num = n)
                            |> Option.map (fun l -> l.Text))
                if pathHits || not excerpts.IsEmpty then Some (f, excerpts)
                else None)
```

### 8. `src/Eru.Mcp/Eru.Mcp.fsproj` — add new files

```xml
<Compile Include="SearchTypes.fs" />
<Compile Include="SimpleScan.fs" />
<Compile Include="IndexedSearch.fs" />
<Compile Include="CollectionCacheService.fs" />
<Compile Include="McpTools.fs" />
<Compile Include="McpServer.fs" />
```

### 9. `src/Eru.Mcp/McpTools.fs` — refactor `search_knowledge`

Enumerate candidates, filter by tags, select backend via env var, format results:

```fsharp
// Backend selection — set ERU_SEARCH_BACKEND=simple to use the baseline
let private backend : SearchFn =
    match System.Environment.GetEnvironmentVariable "ERU_SEARCH_BACKEND" with
    | "simple" -> SimpleScan.search
    | _        -> IndexedSearch.search
```

The `Search` member builds the `CandidateFile list` (same enumeration logic as today, now using `KnowledgeSource` DU), applies the `hasTags` filter, calls `backend termList candidates`, then formats results:

```fsharp
hits |> List.map (fun (f, excerpts) ->
    let excerptBlock = excerpts |> String.concat "\n  > "
    let label =
        match f.Source with
        | Lock  -> $"[lock] {f.RelPath} (from {f.SourceName |> Option.defaultValue ""}:{f.RelPath})"
        | Local -> $"[local] {f.RelPath}"
        | Cache ->
            let tagsStr = if f.Tags = [] then "" else " [tags: " + String.concat "," f.Tags + "]"
            let descStr = f.Description |> Option.map (fun d -> " — " + d) |> Option.defaultValue ""
            $"[collection] {f.RelPath}{tagsStr}{descStr}"
    $"{label}\n  > {excerptBlock}")
|> (fun results ->
    if results.IsEmpty then "No matching artifacts found."
    else String.concat "\n\n" results)
```

---

## Extensibility Hook for `ck`

Adding `ck` later means adding a new module `src/Eru.Mcp/CkSearch.fs` with `let search : SearchFn = ...` and extending the env var selection:

```fsharp
let private backend : SearchFn =
    match System.Environment.GetEnvironmentVariable "ERU_SEARCH_BACKEND" with
    | "simple"  -> SimpleScan.search
    | "indexed" -> IndexedSearch.search
    | _         -> // detect ck on PATH, fall back to IndexedSearch if absent
        if CkSearch.isAvailable () then CkSearch.search
        else IndexedSearch.search
```

No changes to `SimpleScan`, `IndexedSearch`, or `SearchTypes` — the agreed `SearchFn` signature is the only contract.

---

## Verification

```bash
# Build
dotnet build

# Tests
dotnet test

# Start MCP server
dotnet run --project src/Eru -- mcp

# Confirm index files are created
ls ~/.cache/eru/index/

# Search via MCP inspector or Claude — confirm:
# 1. A file matching any query word is returned (OR semantics)
# 2. All matching lines returned as excerpts, not just the first
# 3. Second query is faster (index cache hit, no disk reads for unchanged files)
# 4. Modify a source file, re-query — confirm index rebuilds for that file only
```
