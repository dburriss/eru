namespace Eru

/// A just-enough semantic version for comparing eru builds: `major.minor.patch[-prerelease][+build]`.
/// A prerelease sorts below the same version without one (0.15.2-alpha < 0.15.2); build metadata is ignored.
module SemVer =

    type T = { Major: int; Minor: int; Patch: int; Pre: string list }

    let parse (s: string) : T option =
        if isNull s then None
        else
            let noBuild = match s.IndexOf '+' with -1 -> s | i -> s.Substring(0, i)
            let core, pre =
                match noBuild.IndexOf '-' with
                | -1 -> noBuild, []
                | i  -> noBuild.Substring(0, i), noBuild.Substring(i + 1).Split('.') |> Array.toList
            match core.Split('.') |> Array.map System.Int32.TryParse |> Array.toList with
            | [ (true, a); (true, b); (true, c) ] when a >= 0 && b >= 0 && c >= 0 ->
                Some { Major = a; Minor = b; Patch = c; Pre = pre }
            | _ -> None

    let private comparePre (a: string list) (b: string list) : int =
        match a, b with
        | [], [] -> 0
        | [], _  -> 1
        | _, []  -> -1
        | _ ->
            let one (x: string) (y: string) =
                match System.Int64.TryParse x, System.Int64.TryParse y with   // Int64: install-alpha stamps a 14-digit timestamp
                | (true, i), (true, j) -> compare i j
                | (true, _), _ -> -1   // numeric identifiers sort below alphanumeric ones
                | _, (true, _) -> 1
                | _ -> System.String.CompareOrdinal(x, y) |> sign
            let rec go (xs: string list) (ys: string list) =
                match xs, ys with
                | [], [] -> 0
                | [], _  -> -1
                | _, []  -> 1
                | x :: xr, y :: yr -> match one x y with 0 -> go xr yr | c -> c
            go a b

    let compareVersions (a: T) (b: T) : int =
        match compare (a.Major, a.Minor, a.Patch) (b.Major, b.Minor, b.Patch) with
        | 0 -> comparePre a.Pre b.Pre
        | c -> c

    /// True only when both parse and `candidate` is strictly newer than `current`. An unparsable
    /// version is never "newer", so it can neither block a write nor stop a server.
    let isNewer (candidate: string) (current: string) : bool =
        match parse candidate, parse current with
        | Some c, Some cur -> compareVersions c cur > 0
        | _ -> false

/// The error a write returns when the file on disk was written by a newer eru. Callers match on the
/// prefix to tell "this process is stale" apart from an ordinary I/O failure.
module StaleWriter =

    let prefix = "stale eru:"

    let message (writtenBy: string) (self: string) =
        $"{prefix} index was written by newer eru {writtenBy}; this process ({self}) is stale and should be restarted"

    let isStale (error: string) = not (isNull error) && error.StartsWith prefix
