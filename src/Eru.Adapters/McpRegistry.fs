namespace Eru.Adapters

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Eru

/// A directory of one `<pid>.json` per running `eru mcp`, so a server can notice that a newer eru is
/// running and step aside. Entries of dead processes are pruned by whoever sees them.
module McpRegistry =

    type Entry = { Pid: int; Version: string; StartedAt: DateTimeOffset }

    let private opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private fileFor (dir: string) (pid: int) = Path.Combine(dir, $"{pid}.json")

    /// Alive means the pid exists and its process started no later than the entry was written; a
    /// process that merely reused a dead server's pid started afterwards.
    let isAlive (entry: Entry) : bool =
        try
            use p = Process.GetProcessById entry.Pid
            DateTimeOffset(p.StartTime.ToUniversalTime()) <= entry.StartedAt + TimeSpan.FromSeconds 5.0
        with _ -> false

    let registerAt (dir: string) (entry: Entry) : unit =
        try
            Directory.CreateDirectory dir |> ignore
            File.WriteAllText(fileFor dir entry.Pid, JsonSerializer.Serialize(entry, opts))
        with _ -> ()

    let unregisterAt (dir: string) (pid: int) : unit =
        try
            let f = fileFor dir pid
            if File.Exists f then File.Delete f
        with _ -> ()

    /// Every registered entry whose process is alive; entries of dead processes (and unreadable files)
    /// are deleted on the way.
    let liveEntriesAt (dir: string) (alive: Entry -> bool) : Entry list =
        if not (Directory.Exists dir) then []
        else
            [ for f in (try Directory.GetFiles(dir, "*.json") with _ -> [||]) do
                let entry =
                    try Some (JsonSerializer.Deserialize<Entry>(File.ReadAllText f, opts))
                    with _ -> None
                match entry with
                | Some e when alive e -> yield e
                | _ -> (try File.Delete f with _ -> ()) ]

    /// The newest version running that is strictly newer than `selfVersion`, if any. Equal versions
    /// never count: they are usually separate sessions of the same install.
    let newerRunningAt (dir: string) (alive: Entry -> bool) (selfVersion: string) : string option =
        liveEntriesAt dir alive
        |> List.map (fun e -> e.Version)
        |> List.filter (fun v -> SemVer.isNewer v selfVersion)
        |> List.sortWith (fun a b ->
            match SemVer.parse a, SemVer.parse b with
            | Some x, Some y -> SemVer.compareVersions y x
            | _ -> 0)
        |> List.tryHead
