module Eru.Site.FilterEngine

open System
open Eru

// Pure document selection shared by custom pages (`filter`) and document rules (`match`).

let private ci (a: string) (b: string) = String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

let private anyCi (values: string list) (candidate: string) = values |> List.exists (ci candidate)

let private normExt (e: string) = if e.StartsWith "." then e else "." + e

let private bundleMatches (exact: bool) (wanted: string list) (bundle: string option) =
    match bundle with
    | None -> false
    | Some b ->
        wanted
        |> List.exists (fun w ->
            let w = w.Trim('/')
            ci b w || (not exact && b.StartsWith(w + "/", StringComparison.OrdinalIgnoreCase)))

/// AND across fields, OR within a field's list. `not` negates a nested filter.
let rec matches (now: DateTimeOffset) (f: FilterSpec) (d: SiteDocument) : bool =
    (f.Source.IsEmpty || anyCi f.Source d.Source)
    && (f.Bundle.IsEmpty || bundleMatches f.Exact f.Bundle d.Bundle)
    && (f.Type.IsEmpty || (d.Type |> Option.exists (anyCi f.Type)))
    && (f.Tag.IsEmpty || (d.Tags |> List.exists (anyCi f.Tag)))
    && (f.Status.IsEmpty || (d.Status |> Option.exists (anyCi f.Status)))
    && (f.Ext.IsEmpty || (f.Ext |> List.exists (fun e -> ci (normExt e) d.Extension)))
    && (f.Path.IsEmpty || Patterns.matchesAny f.Path d.RemotePath)
    && (f.Stale |> Option.forall (fun s -> DocFacts.isStale now d.StaleAfter = s))
    && (f.Not |> Option.forall (fun n -> not (matches now n d)))

let apply (now: DateTimeOffset) (f: FilterSpec) (docs: SiteDocument list) : SiteDocument list =
    docs |> List.filter (matches now f)

// ── sorting, grouping, limiting ──────────────────────────────────────────────

let private utc (d: DateTimeOffset) = d.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss")

/// A comparable string for each sortable field; None sorts last in either direction.
let sortKey (field: string) (d: SiteDocument) : string option =
    match field with
    | "title"      -> Some d.Title
    | "source"     -> Some d.Source
    | "path"       -> Some d.RemotePath
    | "bundle"     -> d.Bundle
    | "type"       -> d.Type
    | "tags"       -> d.Tags |> List.tryHead
    | "status"     -> d.Status
    | "verified"   ->
        Some (match DocFacts.trustTier d.Verified with
              | "human-reviewed" -> "2" | "machine-confirmed" -> "1" | _ -> "0")
    | "staleAfter" -> d.StaleAfter |> Option.map utc
    | "generated"  -> d.Generated |> Option.bind (fun g -> g.At) |> Option.map utc
    | "syncStatus" -> Some (DocFacts.statusLabel d.SyncStatus)
    | "ext"        -> Some d.Extension
    | _            -> None

let sort (spec: SortSpec option) (docs: SiteDocument list) : SiteDocument list =
    let by, desc =
        match spec with
        | Some s -> s.By, s.Descending
        | None -> "title", false
    let keyed, missing = docs |> List.partition (fun d -> (sortKey by d).IsSome)
    let cmp (a: SiteDocument) (b: SiteDocument) =
        let c = String.Compare((sortKey by a).Value, (sortKey by b).Value, StringComparison.OrdinalIgnoreCase)
        if desc then -c else c
    List.sortWith cmp keyed @ missing

let limit (n: int option) (docs: SiteDocument list) : SiteDocument list =
    match n with
    | Some n -> List.truncate n docs
    | None -> docs

/// Headed sections by `tag`, `type`, `bundle` or `source`. A document with several tags
/// appears under each. Documents lacking the field go in a trailing group.
let group (field: string) (docs: SiteDocument list) : (string * SiteDocument list) list =
    let missingLabel =
        match field with
        | "tag" -> "Untagged"
        | "type" -> "No type"
        | "bundle" -> "No bundle"
        | _ -> "Other"
    let keysOf (d: SiteDocument) : string list =
        match field with
        | "tag" -> d.Tags
        | "type" -> d.Type |> Option.toList
        | "bundle" -> d.Bundle |> Option.toList
        | "source" -> [ d.Source ]
        | _ -> []
    let present =
        docs
        |> List.collect (fun d -> keysOf d |> List.map (fun k -> k, d))
        |> List.groupBy fst
        |> List.sortWith (fun (a, _) (b, _) -> String.Compare(a, b, StringComparison.OrdinalIgnoreCase))
        |> List.map (fun (k, pairs) -> k, pairs |> List.map snd)
    let none = docs |> List.filter (fun d -> (keysOf d).IsEmpty)
    present @ (if none.IsEmpty then [] else [ missingLabel, none ])
