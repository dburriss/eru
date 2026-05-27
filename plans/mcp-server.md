---
status: todo
---

# Plan: `eru mcp` — MCP stdio server

## Context

`eru` manages knowledge artifacts that are equally useful to AI coding assistants (Claude Code, GitHub Copilot, etc.). Adding an MCP (Model Context Protocol) stdio server as an `eru mcp` subcommand lets AI agents search and read the same knowledge sources, collections, and local knowledge directories that `eru` already manages.

The server is a long-running stdio process — the MCP client spawns it once at session start and communicates via JSON-RPC over stdin/stdout for the duration of the session.

**SDK**: `ModelContextProtocol` v1.x (official Microsoft/MCP package, works from F# via standard .NET attributes). Transport: stdio.

**Depends on**: the collection cache described in `plans/collection-cache.md`. The cache must be implemented first as it is the data source for content search.

---

## Search strategy

| Data source | How accessed |
|---|---|
| Collection files | Full-text content search against `~/.cache/eru/collections/` (populated and refreshed by `CollectionCacheService`) |
| Lock file entries | Read from `LocalPath` on disk — already present, no caching needed |
| Local `knowledge/` / `KNOWLEDGE/` | Scanned directly from the filesystem at CWD |

Search matches against file content, not just paths and tags. Metadata (source, path, tags, description) is still surfaced in results.

---

## New project: `src/Eru.Mcp/`

### `Eru.Mcp.fsproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Eru.Mcp</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Eru.Domain/Eru.Domain.fsproj" />
    <ProjectReference Include="../Eru.Adapters/Eru.Adapters.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="1.*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="CollectionCacheService.fs" />
    <Compile Include="McpTools.fs" />
    <Compile Include="McpServer.fs" />
  </ItemGroup>
</Project>
```

Add to `eru.slnx` inside `/src/`:

```xml
<Project Path="src/Eru.Mcp/Eru.Mcp.fsproj" />
```

---

## `src/Eru.Mcp/CollectionCacheService.fs`

See `plans/collection-cache.md` for full detail. This `BackgroundService`:

- Populates `~/.cache/eru/collections/` on startup by fetching all `CollectionFileRef` files via `deps.FetchRemoteContent`
- Re-fetches on a `PeriodicTimer` using `EffectiveConfig.McpRefreshIntervalMinutes` (default 60)
- Serves stale content during a refresh; keeps stale content on failure

---

## `src/Eru.Mcp/McpTools.fs` — tool definitions

Two MCP tools on a class that receives `Deps` and `GlobalConfig` via DI:

### `search_knowledge`

Searches all three data sources and returns a unified result list:

1. **Cached collection files** — walk `~/.cache/eru/collections/`; for each file, check if content contains any search term (case-insensitive); enrich matched files with metadata from `GlobalConfig.Collections` (source, tags, description)
2. **Lock file entries** — read lock entries via `deps.ReadLockEntries`; for files that exist at `LocalPath`, check content contains any search term
3. **Local knowledge directories** — scan `{cwd}/knowledge/` and `{cwd}/KNOWLEDGE/` if present; check content of each file

Output: one result per matching file showing source, path, tags, description, and a short content excerpt (first matching line).

### `read_artifact`

Returns the full content of one artifact. Resolution order:

1. Local file path (relative to CWD or absolute) — `File.ReadAllText`
2. `LocalPath` match in lock file — `File.ReadAllText`
3. Cache hit at `~/.cache/eru/collections/<sourceName>/<remotePath>` — `File.ReadAllText`
4. `sourceName:remotePath` — look up source in `EffectiveConfig`, call `deps.FetchRemoteContent` (live fetch as fallback)
5. No match — return descriptive error string

```fsharp
[<McpServerToolType>]
type KnowledgeTools(deps: Deps, globalCfg: GlobalConfig, eff: EffectiveConfig) =

    [<McpServerTool(Name = "search_knowledge")>]
    [<Description("Full-text search across cached collection files, locally pulled artifacts (.eru/eru.lock), and local knowledge/ directories. Returns matching file paths, metadata, and a content excerpt.")>]
    member _.Search(
        [<Description("Search terms (space-separated, OR semantics). Matched against file content, path, and description. Leave empty to list all known artifacts.")>] query: string,
        [<Description("Comma-separated tags to filter by (AND semantics). Leave empty to skip tag filtering.")>] tags: string) = ...

    [<McpServerTool(Name = "read_artifact")>]
    [<Description("Read the full content of a knowledge artifact by local path, lock-file path, cached collection path, or 'sourceName:remotePath' reference.")>]
    member _.Read(
        [<Description("Artifact path: a local file path (relative or absolute), 'sourceName:remotePath', or a path from search_knowledge results.")>] path: string) = ...
```

---

## `src/Eru.Mcp/McpServer.fs` — server bootstrap

```fsharp
module Eru.Mcp.Server

let run (deps: Deps) : Task<unit> =
    task {
        let globalCfg = deps.ReadGlobalConfig() |> ...  // unwrap or use None
        let eff       = Config.merge globalCfg (deps.ReadLocalConfig() |> ...) |> ...

        let builder = Host.CreateApplicationBuilder()
        builder.Services
            .AddSingleton(deps)
            .AddSingleton(globalCfg)
            .AddSingleton(eff)
            .AddHostedService<CollectionCacheService>()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof<KnowledgeTools>.Assembly)
        |> ignore

        do! builder.Build().RunAsync()
    }
```

---

## Changes to `Eru.Cli`

### `Eru.Cli.fsproj`

```xml
<ProjectReference Include="../Eru.Mcp/Eru.Mcp.fsproj" />
```

### `Args.fs`

```fsharp
type McpArgs =
    | [<Hidden>] Placeholder
    interface IArgParserTemplate with
        member a.Usage = match a with Placeholder -> ""

// In EruArgs:
| [<SubCommand>] Mcp of ParseResults<McpArgs>
| Mcp _ -> "Start an MCP stdio server for AI agent use."
```

### `CommandMapper.fs`

```fsharp
let (|McpCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Mcp _ -> Some ()
        | _             -> None)
```

### `Program.fs`

```fsharp
| McpCmd ->
    McpServer.run deps |> Async.AwaitTask |> Async.RunSynchronously
    0
```

---

## MCP client registration

```bash
claude mcp add eru -- eru mcp
```

Or manually in `.claude/mcp.json`:

```json
{
  "mcpServers": {
    "eru": { "type": "stdio", "command": "eru", "args": ["mcp"] }
  }
}
```

---

## Files to create / modify

| File | Change |
|------|--------|
| `src/Eru.Mcp/Eru.Mcp.fsproj` | NEW |
| `src/Eru.Mcp/CollectionCacheService.fs` | NEW — see `plans/collection-cache.md` |
| `src/Eru.Mcp/McpTools.fs` | NEW — `KnowledgeTools` with `search_knowledge` and `read_artifact` |
| `src/Eru.Mcp/McpServer.fs` | NEW — `Server.run` |
| `eru.slnx` | Add `Eru.Mcp` project |
| `src/Eru.Domain/Config.fs` | Add `McpRefreshIntervalMinutes` — see `plans/collection-cache.md` |
| `src/Eru.Adapters/Paths.fs` | Add `collectionCachePath()` — see `plans/collection-cache.md` |
| `src/Eru.Cli/Eru.Cli.fsproj` | Add project reference to `Eru.Mcp` |
| `src/Eru.Cli/Args.fs` | Add `McpArgs` and `Mcp` case |
| `src/Eru.Cli/CommandMapper/CommandMapper.fs` | Add `(|McpCmd|_|)` |
| `src/Eru.Cli/Program.fs` | Add `McpCmd` dispatch branch |

---

## Verification

```bash
dotnet build
dotnet test

# Start MCP server; confirm cache is populated and server responds to initialize:
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"0.1"}}}' \
  | dotnet run --project src/Eru -- mcp

# Register in Claude Code and exercise tools:
claude mcp add eru -- dotnet run --project src/Eru -- mcp
claude mcp list   # should show "eru"
# Use search_knowledge and read_artifact in a Claude Code session
```
