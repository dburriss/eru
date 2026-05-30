namespace Eru

open System.IO
open System.Text.RegularExpressions

module Patterns =

    let isBinaryContent (content: string) : bool =
        content.IndexOf('\000') >= 0

    // Convert a gitignore-style glob to a Regex pattern.
    // Patterns with no '/' match against filename only.
    // Patterns with '/' match against the full relative path.
    // ** semantics: /**/  matches zero or more path segments; ** elsewhere matches anything.
    let private globToRegex (pattern: string) : bool * Regex =
        let matchFullPath = pattern.Contains('/')
        let mutable p = Regex.Escape(pattern)
                            .Replace(@"\*\*", "\x00SS\x00")
                            .Replace(@"\*",   "\x00S\x00")
                            .Replace(@"\?",   "\x00Q\x00")
        // /**/  → zero or more path segments (e.g. docs/**/file matches docs/file and docs/a/b/file)
        p <- p.Replace("/" + "\x00SS\x00" + "/", "/([^/]+/)*")
        p <- p
                .Replace("\x00SS\x00", ".*")
                .Replace("\x00S\x00",  "[^/]*")
                .Replace("\x00Q\x00",  "[^/]")
        matchFullPath, Regex("^" + p + "$", RegexOptions.IgnoreCase)

    let matchesGlob (pattern: string) (path: string) : bool =
        let matchFullPath, rx = globToRegex pattern
        let subject = if matchFullPath then path else Path.GetFileName(path)
        rx.IsMatch(subject)

    let private matchesAny (patterns: string list) (path: string) : bool =
        patterns |> List.exists (fun p -> matchesGlob p path)

    // Path-only check (no content needed); used as a fast pre-filter
    let isPathBlocked (blockPatterns: string list) (allowPatterns: string list) (path: string) : bool =
        if matchesAny allowPatterns path then false
        else matchesAny blockPatterns path

    let pathShortHash (path: string) : string =
        let bytes = System.Text.Encoding.UTF8.GetBytes path
        let hex   = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes).ToLowerInvariant()
        hex.[..7]

    // allow wins over block; binary check applied when allowBinaries=false
    let isBlocked
        (blockPatterns : string list)
        (allowPatterns : string list)
        (allowBinaries : bool)
        (path          : string)
        (content       : string) : bool =
        if matchesAny allowPatterns path then false
        elif matchesAny blockPatterns path then true
        elif not allowBinaries && isBinaryContent content then true
        else false
