module Eru.Tests.SiteFilterTests

open System
open Xunit
open Eru
open Eru.Site
open Eru.Tests.SiteTestSupport

let private now = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)

let private doc (id: string) (f: SiteDocument -> SiteDocument) : SiteDocument =
    f { baseDoc with Id = $"src:{id}"; RemotePath = id; Title = id }

let private docs =
    [ doc "ops/restart.md" (fun d ->
        { d with Type = Some "runbook"; Tags = [ "ops"; "db" ]; Bundle = Some "kb/ops"; Status = Some "stable" })
      doc "ops/backup.md" (fun d ->
        { d with
            Type = Some "runbook"
            Tags = [ "ops" ]
            Bundle = Some "kb/ops/db"
            Status = Some "deprecated"
            StaleAfter = Some (now.AddDays -1.0) })
      doc "dev/setup.md" (fun d ->
        { d with
            Source = "other"
            Type = Some "guide"
            Tags = [ "dev" ]
            Bundle = Some "kb/dev"
            StaleAfter = Some (now.AddDays 30.0) })
      doc "notes.txt" (fun d -> { d with Extension = ".txt" }) ]

let private ids (f: FilterSpec) =
    FilterEngine.apply now f docs |> List.map (fun d -> d.RemotePath)

[<Fact>]
let ``an empty filter matches everything`` () =
    Assert.Equal(4, (ids FilterSpec.empty).Length)

[<Fact>]
let ``values within a field are ORed`` () =
    Assert.Equal<string list>([ "ops/restart.md"; "ops/backup.md"; "dev/setup.md" ],
                              ids { FilterSpec.empty with Type = [ "runbook"; "guide" ] })

[<Fact>]
let ``fields are ANDed`` () =
    Assert.Equal<string list>([ "ops/restart.md" ],
                              ids { FilterSpec.empty with Type = [ "runbook" ]; Tag = [ "db" ] })

[<Fact>]
let ``not negates a nested filter`` () =
    let f = { FilterSpec.empty with Type = [ "runbook" ]; Not = Some { FilterSpec.empty with Status = [ "deprecated" ] } }
    Assert.Equal<string list>([ "ops/restart.md" ], ids f)

[<Fact>]
let ``path accepts globs`` () =
    Assert.Equal<string list>([ "ops/restart.md"; "ops/backup.md" ], ids { FilterSpec.empty with Path = [ "ops/**" ] })
    Assert.Equal<string list>([ "notes.txt" ], ids { FilterSpec.empty with Path = [ "*.txt" ] })

[<Fact>]
let ``bundle matches children by prefix unless exact`` () =
    Assert.Equal<string list>([ "ops/restart.md"; "ops/backup.md" ], ids { FilterSpec.empty with Bundle = [ "kb/ops" ] })
    Assert.Equal<string list>([ "ops/restart.md" ], ids { FilterSpec.empty with Bundle = [ "kb/ops" ]; Exact = true })

[<Fact>]
let ``bundle prefix does not match a sibling that merely shares the start`` () =
    let extra = doc "x.md" (fun d -> { d with Bundle = Some "kb/operations" })
    Assert.Empty(FilterEngine.apply now { FilterSpec.empty with Bundle = [ "kb/ops" ] } [ extra ])

[<Fact>]
let ``stale matches documents past stale_after`` () =
    Assert.Equal<string list>([ "ops/backup.md" ], ids { FilterSpec.empty with Stale = Some true })
    Assert.DoesNotContain("ops/backup.md", ids { FilterSpec.empty with Stale = Some false })

[<Fact>]
let ``source, ext and status filter`` () =
    Assert.Equal<string list>([ "dev/setup.md" ], ids { FilterSpec.empty with Source = [ "other" ] })
    Assert.Equal<string list>([ "notes.txt" ], ids { FilterSpec.empty with Ext = [ "txt" ] })
    Assert.Equal<string list>([ "notes.txt" ], ids { FilterSpec.empty with Ext = [ ".txt" ] })
    Assert.Equal<string list>([ "ops/backup.md" ], ids { FilterSpec.empty with Status = [ "deprecated" ] })

[<Fact>]
let ``a filter that matches nothing yields an empty list`` () =
    Assert.Empty(ids { FilterSpec.empty with Type = [ "nonexistent" ] })

[<Fact>]
let ``sort orders by field, puts missing values last and honours direction`` () =
    let byStale = FilterEngine.sort (Some { By = "staleAfter"; Descending = false }) docs |> List.map (fun d -> d.RemotePath)
    Assert.Equal<string list>([ "ops/backup.md"; "dev/setup.md" ], byStale |> List.take 2)
    let desc = FilterEngine.sort (Some { By = "staleAfter"; Descending = true }) docs |> List.map (fun d -> d.RemotePath)
    Assert.Equal<string list>([ "dev/setup.md"; "ops/backup.md" ], desc |> List.take 2)
    Assert.Equal<string list>([ "notes.txt"; "ops/restart.md" ], (byStale |> List.skip 2 |> List.sort))

[<Fact>]
let ``default sort is by title`` () =
    let sorted = FilterEngine.sort None docs |> List.map (fun d -> d.Title)
    Assert.Equal<string list>(List.sortWith (fun a b -> String.Compare(a, b, StringComparison.OrdinalIgnoreCase)) sorted, sorted)

[<Fact>]
let ``limit caps the document count`` () =
    Assert.Equal(2, (FilterEngine.limit (Some 2) docs).Length)
    Assert.Equal(4, (FilterEngine.limit None docs).Length)

[<Fact>]
let ``group by tag lists a document under each tag and collects untagged last`` () =
    let groups = FilterEngine.group "tag" docs
    Assert.Equal<string list>([ "db"; "dev"; "ops"; "Untagged" ], groups |> List.map fst)
    Assert.Equal(2, groups |> List.find (fun (k, _) -> k = "ops") |> snd |> List.length)

[<Fact>]
let ``group by type collects documents without a type last`` () =
    let groups = FilterEngine.group "type" docs
    Assert.Equal<string list>([ "guide"; "runbook"; "No type" ], groups |> List.map fst)
