module Eru.Site.DocFacts

open System

let statusLabel =
    function
    | Pulled    -> "pulled"
    | Cached    -> "cached"
    | IndexOnly -> "index-only"

/// OKF trust tier derived from `verified[].by` (OKF §5.3):
/// no entries -> unverified; any human:<id> actor -> human-reviewed; else machine-confirmed.
let trustTier (verified: Eru.Frontmatter.ActorAt list) : string =
    if verified.IsEmpty then "unverified"
    elif verified |> List.exists (fun v -> v.By.StartsWith("human:")) then "human-reviewed"
    else "machine-confirmed"

let isStale (now: DateTimeOffset) (staleAfter: DateTimeOffset option) : bool =
    staleAfter |> Option.exists (fun d -> now >= d)

let isHumanActor (by: string) = by.StartsWith("human:")

let formatDate (d: DateTimeOffset) = d.ToString("yyyy-MM-dd")
