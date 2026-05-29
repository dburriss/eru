# Plan: MCP Resources for Sources and Installed Artifacts

## Context

Users want to browse what eru knows about — configured sources and locally installed docs — without running a search query. MCP Resources are the right abstraction: sources are URI-addressable entities that clients can enumerate and read. A separate "installed" resource covers locally pulled lock file entries. No new tool is needed.

---

## Design

Three resources in a new `McpResources.fs` file:

| Resource | Type | Data |
|---|---|---|
| `eru://sources` | Static | All configured sources: name, URL, branch, doc count |
| `eru://sources/{name}` | URI template | Config details + collection/manifest docs for one source |
| `eru://installed` | Static | All locally pulled artifacts from the lock file, grouped by source |

**Data sources:**
- `syncService.CurrentEff.Sources` — configured `SourceConfig list`
- `syncService.CurrentEff.Collections` — flat `CollectionFileRef list` (config + manifests), each has `Source`
- `deps.ReadLockEntries` + `Paths.lockFilePath` — lock file entries (`LockEntry list`), each has `SourceName`, `RemotePath`, `LocalPath`

The SDK (`ModelContextProtocol 1.3.0`) supports resources via `[<McpServerResourceType>]` on the class and `[<McpServerResource(UriTemplate = "...")>]` on methods. Static resources (no `{param}` in URI) appear in `resources/list`; template resources appear in `resources/templates/list`. Methods returning `string` are auto-wrapped as `TextResourceContents`.

---

## Changes

### 1. New `src/Eru.Mcp/McpResources.fs`

```fsharp
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
                        let tagsStr = if f.Tags = [] then "" else $" [tags: {String.concat "," f.Tags}]"
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
        | Ok [] -> "No locally installed artifacts."
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
```

### 2. `src/Eru.Mcp/Eru.Mcp.fsproj`

Add before the existing `McpTools.fs` entry:
```xml
<Compile Include="McpResources.fs" />
```

### 3. `src/Eru.Mcp/McpServer.fs`

After `.WithToolsFromAssembly(typeof<KnowledgeTools>.Assembly)`, add:
```fsharp
.WithResourcesFromAssembly(typeof<SourceResources>.Assembly)
```

---

## Key reuse

- `syncService.CurrentEff` — same pattern as all three existing tools (`McpTools.fs`)
- `deps.ReadLockEntries` / `Paths.lockFilePath` — same as `Search` method `McpTools.fs:79–94`
- `[<McpServerResourceType>]` / `[<McpServerResource(UriTemplate = "...")>]` mirrors existing `[<McpServerToolType>]` / `[<McpServerTool>]` pattern

---

## Verification

1. `dotnet build` — zero errors
2. Start MCP server; call `resources/list` → should return `eru://sources` and `eru://installed`
3. Call `resources/templates/list` → should return template `eru://sources/{name}`
4. Read `eru://sources` → formatted list of source names with URLs and doc counts
5. Read `eru://sources/<name>` → source details with collection docs
6. Read `eru://installed` → lock file entries grouped by source, or "No locally installed artifacts."
