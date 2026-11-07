namespace Eru.Adapters

open Eru
open System
open System.IO

// Plain local folders attached as sources: read in place, never cloned or copied into the cache.
module FolderAdapter =

    /// An existing directory that is not a git repository (no `.git` directory or worktree file).
    let isPlainFolder (path: string) : bool =
        try
            Directory.Exists path
            && not (Directory.Exists (Path.Combine(path, ".git")) || File.Exists (Path.Combine(path, ".git")))
        with _ -> false

    /// Stat-only walk of `bundlePath` under `root`: every .md file as (root-relative '/' path,
    /// "mtimeTicks:size"). Dot-directories and directories matching `ignorePatterns` (anchored at
    /// the bundle root) are not entered.
    let scan (root: string) (bundlePath: string) (ignorePatterns: string list) : Result<(string * string) list, string> =
        try
            let start = if bundlePath = "" then root else Path.Combine(root, bundlePath)
            if not (Directory.Exists start) then Error $"folder not found: {start}"
            else
                let results = System.Collections.Generic.List<string * string>()
                let rec walk (dir: string) =
                    let rel = Path.GetRelativePath(start, dir).Replace(Path.DirectorySeparatorChar, '/')
                    for file in Directory.GetFiles dir do
                        if file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) then
                            let name = Path.GetFileName file
                            let bundleRel = if rel = "." then name else $"{rel}/{name}"
                            if not (Patterns.isOkfIgnored ignorePatterns bundleRel) then
                                let info = FileInfo file
                                let rootRel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')
                                results.Add((rootRel, $"{info.LastWriteTimeUtc.Ticks}:{info.Length}"))
                    for sub in Directory.GetDirectories dir do
                        let name = Path.GetFileName sub
                        let subRel = if rel = "." then name else $"{rel}/{name}"
                        // A directory is pruned when a file directly inside it would be ignored.
                        if not (name.StartsWith ".") && not (Patterns.isOkfIgnored ignorePatterns (subRel + "/_.md")) then
                            walk sub
                walk start
                results |> Seq.sortBy fst |> Seq.toList |> Ok
        with ex -> Error ex.Message
