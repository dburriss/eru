module Eru.Tests.SourceIndexAdapterTests

open System
open System.IO
open Xunit
open Eru
open Eru.Adapters

let private index entries : SourceIndex =
    { Version = 1; SourceHeadSha = None; ConsecutiveShaCheckFailures = 0; WrittenBy = None
      Entries = entries |> List.map (fun p -> p, IndexEntry.empty) |> Map.ofList }

let private withTempIndex (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), "eru-idx-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f (Path.Combine(dir, "sources", "kb", "index.json"))
    finally try Directory.Delete(dir, true) with _ -> ()

let private read path =
    File.ReadAllText path |> Serialization.deserialize<SourceIndex> |> function Ok i -> i | Error e -> failwith e

[<Fact>]
let ``writeIndexToPath stamps the writing version and creates the directory`` () =
    withTempIndex (fun path ->
        Assert.Equal(Ok (), SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index [ "a.md" ]))
        let idx = read path
        Assert.Equal(Some "0.15.1", idx.WrittenBy)
        Assert.Equal(1, idx.Entries.Count))

[<Fact>]
let ``writeIndexToPath refuses to overwrite an index written by a newer eru`` () =
    withTempIndex (fun path ->
        SourceIndexAdapter.writeIndexToPath (Some "0.16.0") path (index [ "a.md"; "b.md" ]) |> ignore
        let before = File.ReadAllBytes path
        match SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index []) with
        | Ok () -> Assert.Fail "expected the stale write to be refused"
        | Error e -> Assert.True(StaleWriter.isStale e, e)
        Assert.Equal<byte[]>(before, File.ReadAllBytes path))

[<Fact>]
let ``writeIndexToPath overwrites an older, an equal and an unstamped index`` () =
    withTempIndex (fun path ->
        SourceIndexAdapter.writeIndexToPath (Some "0.15.0") path (index [ "a.md" ]) |> ignore
        Assert.Equal(Ok (), SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index [ "b.md" ]))
        Assert.Equal(Ok (), SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index [ "c.md" ]))
        SourceIndexAdapter.writeIndexToPath None path (index [ "d.md" ]) |> ignore   // unstamped on disk
        Assert.Equal(Ok (), SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index [ "e.md" ]))
        Assert.True((read path).Entries.ContainsKey "e.md"))

[<Fact>]
let ``writeIndexToPath without a known version writes unstamped and never refuses`` () =
    withTempIndex (fun path ->
        SourceIndexAdapter.writeIndexToPath (Some "9.9.9") path (index [ "a.md" ]) |> ignore
        Assert.Equal(Ok (), SourceIndexAdapter.writeIndexToPath None path (index [ "b.md" ]))
        Assert.Equal(None, (read path).WrittenBy))

[<Fact>]
let ``writeIndexToPath leaves no temp file behind`` () =
    withTempIndex (fun path ->
        SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index [ "a.md" ]) |> ignore
        Assert.Equal<string list>([ "index.json" ], Directory.GetFiles(Path.GetDirectoryName path) |> Array.map Path.GetFileName |> List.ofArray))

[<Fact>]
let ``a failed write keeps the previous index and removes the temp file`` () =
    withTempIndex (fun path ->
        SourceIndexAdapter.writeIndexToPath (Some "0.15.1") path (index [ "a.md" ]) |> ignore
        let before = File.ReadAllBytes path
        // Make the rename fail: the temp file name for this process is already taken by a directory.
        let tmp = $"{path}.{Environment.ProcessId}.tmp"
        Directory.CreateDirectory tmp |> ignore
        match SourceIndexAdapter.writeIndexToPath (Some "0.15.2") path (index [ "b.md" ]) with
        | Ok () -> Assert.Fail "expected the write to fail"
        | Error _ -> ()
        Assert.Equal<byte[]>(before, File.ReadAllBytes path))
