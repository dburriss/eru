namespace Eru.Mcp

open System.ComponentModel
open System.IO
open Eru
open Eru.Adapters
open ModelContextProtocol.Server

[<McpServerResourceType>]
type SourceResources(syncService: KnowledgeSyncService, deps: Deps) =

    [<McpServerResource(UriTemplate = "eru://sources", Name = "sources")>]
    [<Description("List all configured knowledge sources with name, URL, branch, and collection doc count.")>]
    member _.ListSources() : string =
        let eff = syncService.CurrentEff
        if eff.Sources.IsEmpty then "No sources configured."
        else
            eff.Sources
            |> List.map (fun s ->
                let urlPart    = s.Url    |> Option.map (fun u -> $" — {u}") |> Option.defaultValue ""
                let branchPart = s.Branch |> Option.map (fun b -> $" [{b}]")  |> Option.defaultValue ""
                let docCount   = eff.Collections |> List.filter (fun c -> c.Source = s.Name) |> List.length
                $"{s.Name}{urlPart}{branchPart} ({docCount} docs)")
            |> String.concat "\n"

    [<McpServerResource(UriTemplate = "eru://sources/{name}", Name = "source")>]
    [<Description("Config details and known collection docs for a specific source.")>]
    member _.GetSource(
        [<Description("Source name.")>] name: string) : string =
        let eff = syncService.CurrentEff
        match eff.Sources |> List.tryFind (fun s -> s.Name = name) with
        | None -> $"Source '{name}' not found."
        | Some src ->
            let docs = eff.Collections |> List.filter (fun c -> c.Source = name)
            let urlPart    = src.Url    |> Option.map (fun u -> $"\nURL: {u}") |> Option.defaultValue ""
            let branchPart = src.Branch |> Option.map (fun b -> $"\nBranch: {b}") |> Option.defaultValue ""
            let header = $"# {src.Name}{urlPart}{branchPart}"
            if docs.IsEmpty then $"{header}\n(no docs)"
            else
                let docLines =
                    docs |> List.map (fun f ->
                        let tags    = String.concat "," f.Tags
                        let tagsStr = if f.Tags = [] then "" else $" [tags: {tags}]"
                        let descStr = f.Description |> Option.map (fun d -> " — " + d) |> Option.defaultValue ""
                        $"  {f.RemotePath}{tagsStr}{descStr}")
                    |> String.concat "\n"
                $"{header}\n{docLines}"

    [<McpServerResource(UriTemplate = "eru://installed", Name = "installed")>]
    [<Description("All locally pulled artifacts from the lock file, grouped by source.")>]
    member _.ListInstalled() : string =
        let eff = syncService.CurrentEff
        let lockPath = Paths.lockFilePath (deps.GetCwd()) (Some eff.StateFile)
        match deps.ReadLockEntries lockPath with
        | Error e -> $"Error reading lock file: {e}"
        | Ok []   -> "No locally installed artifacts."
        | Ok entries ->
            entries
            |> List.groupBy (fun e -> e.SourceName)
            |> List.sortBy fst
            |> List.map (fun (source, files) ->
                let lines =
                    files |> List.map (fun e ->
                        let missing = if File.Exists(e.LocalPath) then "" else " [missing]"
                        $"  {e.RemotePath} → {e.LocalPath}{missing}")
                    |> String.concat "\n"
                $"## {source}\n{lines}")
            |> String.concat "\n\n"
