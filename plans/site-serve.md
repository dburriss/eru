---
status: proposed
---

# Plan: `eru site serve`

## Context

`eru site serve` starts a local HTTP server that serves the generated static site and
provides a live search API backed by the same search backends used by the MCP server
(`SimpleScan`, `IndexedSearch`, `CkSearch`). A `PeriodicTimer` background task mirrors
the MCP's `CollectionCacheService` — it syncs the cache and regenerates the site on
each tick. An SSE endpoint pushes a reload event to all connected browser tabs after
each regeneration.

The search backends, candidate-building logic, and sync service currently live in
`Eru.Mcp`. Because `Eru.Serve` cannot (and should not) reference `Eru.Mcp`, these
components move to a new `Eru.Search` project that both `Eru.Mcp` and `Eru.Serve`
reference.

---

## New project: `Eru.Search`

Holds all content-search concerns that have no MCP or HTTP dependency.

### `src/Eru.Search/Eru.Search.fsproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Eru.Search</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Eru.Domain/Eru.Domain.fsproj" />
    <ProjectReference Include="../Eru.Adapters/Eru.Adapters.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="SearchTypes.fs" />
    <Compile Include="SimpleScan.fs" />
    <Compile Include="IndexedSearch.fs" />
    <Compile Include="CkSearch.fs" />
    <Compile Include="CandidateBuilder.fs" />
    <Compile Include="KnowledgeSyncService.fs" />
  </ItemGroup>
</Project>
```

### Files moved from `Eru.Mcp` → `Eru.Search` (namespace rename only)

| Old path | New path | Change |
|---|---|---|
| `Eru.Mcp/SearchTypes.fs` | `Eru.Search/SearchTypes.fs` | `namespace Eru.Mcp` → `namespace Eru.Search` |
| `Eru.Mcp/SimpleScan.fs` | `Eru.Search/SimpleScan.fs` | `module Eru.Mcp.SimpleScan` → `Eru.Search.SimpleScan` |
| `Eru.Mcp/IndexedSearch.fs` | `Eru.Search/IndexedSearch.fs` | same |
| `Eru.Mcp/CkSearch.fs` | `Eru.Search/CkSearch.fs` | same |
| `Eru.Mcp/KnowledgeSyncService.fs` | `Eru.Search/KnowledgeSyncService.fs` | same |

### New file: `Eru.Search/CandidateBuilder.fs`

Extracts the three-pass candidate-building logic currently inlined in
`McpTools.KnowledgeTools.search_knowledge` so it can be reused by both the MCP tool
and the serve API handler.

```fsharp
module Eru.Search.CandidateBuilder

open Eru.Domain
open Eru.Search.SearchTypes

/// Build the full candidate list from the source index cache, lock file entries not
/// in any index, and local knowledge/ directories. Mirrors the three passes in
/// McpTools.search_knowledge but returns CandidateFile list directly.
val build : deps: Deps -> eff: EffectiveConfig -> cwd: string -> CandidateFile list
```

### `Eru.Mcp` changes after extraction

- Remove moved files from `Eru.Mcp.fsproj` compile list
- Add `<ProjectReference Include="../Eru.Search/Eru.Search.fsproj" />`
- `McpTools.fs`: add `open Eru.Search` (replace deleted local opens); call
  `CandidateBuilder.build` instead of the inlined candidate loop;
  reference `SearchTypes`, `SimpleScan`, `IndexedSearch`, `CkSearch`,
  `KnowledgeSyncService` from `Eru.Search` namespace
- `CollectionCacheService.fs` and `IndexService.fs` stay in `Eru.Mcp` — they
  are `BackgroundService` wrappers specific to the MCP hosting model

---

## New project: `Eru.Serve`

### `src/Eru.Serve/Eru.Serve.fsproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Eru.Serve</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Eru.Domain/Eru.Domain.fsproj" />
    <ProjectReference Include="../Eru.Adapters/Eru.Adapters.fsproj" />
    <ProjectReference Include="../Eru.Search/Eru.Search.fsproj" />
    <ProjectReference Include="../Eru.Site/Eru.Site.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="SiteServeServer.fs" />
  </ItemGroup>
</Project>
```

### `src/Eru.Serve/SiteServeServer.fs`

One module; uses ASP.NET Core Minimal APIs.

```fsharp
module Eru.Serve.SiteServeServer

open Eru.Domain
open Eru.Search
open Eru.Site

type ServeOptions = {
    OutputDir   : string   // site output dir, default "./cache-site/"
    Port        : int      // HTTP port, default 5173
    OpenBrowser : bool     // auto-open after first generate, default true
    SyncInterval: int      // minutes between auto-sync, mirrors McpRefreshIntervalMinutes
}

module ServeOptions =
    let defaults = {
        OutputDir    = "./cache-site/"
        Port         = 5173
        OpenBrowser  = true
        SyncInterval = 15
    }

val run : deps: Deps -> cfg: EffectiveConfig -> opts: ServeOptions -> Task<int>
```

#### Startup sequence inside `run`

1. **Initial generate** — call `SiteGenerator.generate` with `opts.OutputDir`; fail fast on error
2. **Build WebApplication**
   - `app.UseStaticFiles(StaticFileOptions(FileProvider = PhysicalFileProvider(absOutputDir)))`
   - Register routes (see below)
3. **Start background sync loop** — `Task.Run` with `PeriodicTimer(TimeSpan.FromMinutes opts.SyncInterval)`:
   - On each tick: `Sync.populateIndex deps`, then `SiteGenerator.generate`; on success broadcast `"rebuild"` SSE event to all live connections
4. **Open browser** if `opts.OpenBrowser`
5. `app.RunAsync($"http://localhost:{opts.Port}")`

#### HTTP routes

| Route | Behaviour |
|---|---|
| `GET /` | redirect 302 → `/index.html` |
| `GET /api/search` | content search (see below) |
| `GET /api/sync` | trigger immediate sync (fire-and-forget, returns 202) |
| `GET /api/events` | SSE stream; sends `data: rebuild\n\n` after each site regeneration |
| `GET /**` | static files from `opts.OutputDir`; 404 if not found |

#### `/api/search` handler

Query params: `q` (required), `tags` (repeatable, optional).

```
GET /api/search?q=logging+tracing&tags=dotnet
```

1. Parse `q` into terms (split on whitespace)
2. Select backend from `ERU_SEARCH_BACKEND` env var — same logic as `McpTools.search_knowledge`
3. Call `CandidateBuilder.build deps eff cwd` to get candidates
4. Run chosen `SearchFn terms candidates`
5. Serialize `SearchResult` as JSON (`application/json`)

Response schema matches `Eru.Search.SearchTypes.SearchResult` — same type the MCP tool
returns in its `structuredContent` — so any future tooling that works with MCP search
results also works with serve search results.

#### SSE live reload

```fsharp
// module-level
let private sseClients = System.Collections.Concurrent.ConcurrentBag<HttpResponse>()

// /api/events handler
app.MapGet("/api/events", fun (ctx: HttpContext) -> task {
    ctx.Response.Headers.ContentType <- "text/event-stream"
    ctx.Response.Headers.CacheControl <- "no-cache"
    sseClients.Add(ctx.Response)
    // keep connection alive until client disconnects
    do! Task.Delay(Timeout.Infinite, ctx.RequestAborted) |> Task.ignore
})

// broadcast helper (called after each regeneration)
let private broadcast (msg: string) =
    for r in sseClients do
        try r.WriteAsync($"data: {msg}\n\n") |> ignore
        with _ -> ()   // stale connections silently dropped
```

---

## Changes to `app.js` (in `Eru.Site/SiteGenerator.fs`)

Two additions to the existing `appJs` string (these build on the `site-search-json` plan
which adds JSON-backed search; this plan layers serve-mode behaviour on top):

### 1. API search when running on HTTP

In `applyFilters`, when `window.location.protocol !== 'file:'` and `q` is non-empty,
issue `fetch('/api/search?q='+encodeURIComponent(q))` instead of querying the local
JSON index. The JSON index path remains the fallback when the fetch fails or when
running from `file://`.

```js
function runApiSearch(q, onComplete) {
  fetch('/api/search?q=' + encodeURIComponent(q))
    .then(function(r) { return r.ok ? r.json() : Promise.reject(); })
    .then(function(result) {
      var ids = new Set((result.hits || []).map(function(h) { return h.path; }));
      onComplete(ids);
    })
    .catch(function() { onComplete(null); }); // null → fall back to JSON index
}
```

`h.path` corresponds to `SearchHit.Path` (the `RelPath` field in `CandidateFile`),
which needs to be cross-referenced against `card.dataset.id`. Because `CandidateFile.RelPath`
is the remote path (matching `SiteDocument.RemotePath`) and `card.dataset.id` is
`"<source>:<remotePath>"`, the match is:
`ids.has(card.dataset.source + ':' + card.dataset.id.split(':').slice(1).join(':'))`
— or simpler: expose the `remotePath` as a separate `data-path` attribute on each card.

To avoid `data-id` / `data-path` confusion, the card gets:

```html
<article class="file-card"
         data-id="{doc.Id}"
         data-path="{doc.RemotePath}"
         data-source="..."
         data-ext="..."
         data-tags="...">
```

API search matches on `card.dataset.path`; JSON search matches on `card.dataset.id`.

### 2. SSE live reload

Appended to `appJs` after the existing DOMContentLoaded handler:

```js
(function () {
  if (window.location.protocol === 'file:') return;
  var es = new EventSource('/api/events');
  es.onmessage = function (e) { if (e.data === 'rebuild') location.reload(); };
  es.onerror   = function ()  { es.close(); };   // server not running — silently drop
})();
```

---

## CLI changes

### `src/Eru.Cli/Eru.Cli.fsproj`

Add:
```xml
<ProjectReference Include="../Eru.Serve/Eru.Serve.fsproj" />
```
Add compile entry:
```xml
<Compile Include="SiteServeCli.fs" />
```
(after `SiteGenerateCli.fs`)

### `src/Eru.Cli/Args.fs`

Extend `SiteArgs`:

```fsharp
type SiteServeArgs =
    | [<AltCommandLine("-o")>] Output of string
    | [<AltCommandLine("-p")>] Port   of int
    | No_Open
    | Sync_Interval of int
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Output _        -> "Site output directory (default: ./cache-site/)"
            | Port _          -> "HTTP port (default: 5173)"
            | No_Open         -> "Do not open the browser automatically"
            | Sync_Interval _ -> "Minutes between background cache syncs (default: 15)"

type SiteArgs =
    | [<SubCommand>] Generate of ParseResults<SiteGenerateArgs>
    | [<SubCommand>] Serve    of ParseResults<SiteServeArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Generate _ -> "Generate a static HTML site from the local cache index"
            | Serve _    -> "Serve the site locally with live reload and search API"
```

### New: `src/Eru.Cli/SiteServeCli.fs`

```fsharp
module Eru.Cli.SiteServeCli

open Eru.Serve

let run (deps: Deps) (eff: EffectiveConfig) (args: ParseResults<SiteServeArgs>) =
    let opts = {
        ServeOptions.defaults with
            OutputDir    = args.TryGetResult Output |> Option.defaultValue "./cache-site/"
            Port         = args.TryGetResult Port   |> Option.defaultValue 5173
            OpenBrowser  = not (args.Contains No_Open)
            SyncInterval = args.TryGetResult Sync_Interval |> Option.defaultValue 15
    }
    SiteServeServer.run deps eff opts |> Async.AwaitTask |> Async.RunSynchronously
```

### `src/Eru.Cli/Program.fs`

Add dispatch arm for `Site (Serve args)`.

---

## Solution file (`eru.slnx`)

Add two new projects:

```xml
<Project Path="src/Eru.Search/Eru.Search.fsproj" />
<Project Path="src/Eru.Serve/Eru.Serve.fsproj" />
```

---

## Implementation sequence

1. **Create `Eru.Search`** — copy files from `Eru.Mcp`, rename namespaces, add
   `CandidateBuilder.fs`. `dotnet build` to confirm.
2. **Update `Eru.Mcp`** — remove moved files, add project reference, update `McpTools.fs`
   to call `CandidateBuilder.build`. All MCP tests must still pass.
3. **Create `Eru.Serve`** — scaffold `SiteServeServer.fs`; wire up static files,
   `/api/search`, `/api/events`, background sync loop.
4. **`app.js` additions** — API search path, SSE snippet. These depend on `site-search-json`
   plan being implemented first (for the JSON search fallback path).
5. **CLI wiring** — `Args.fs`, `SiteServeCli.fs`, `Program.fs`.
6. **Smoke test** (see verification below).

---

## Verification

```bash
dotnet build
dotnet test

# Start serve
dotnet run --project src/Eru -- site serve --port 5173

# Check static files
curl http://localhost:5173/                        # 302 → /index.html
curl http://localhost:5173/index.html              # 200 HTML

# Check API search
curl "http://localhost:5173/api/search?q=logging"  # JSON SearchResult

# Check sync trigger
curl http://localhost:5173/api/sync                # 202

# Manual browser checks:
# - Open http://localhost:5173 — site loads
# - Type into search box — results filter via /api/search (check Network tab)
# - Trigger sync from another terminal (eru sync); site reloads automatically (SSE)
# - Kill the server; re-open index.html from file:// — site falls back to
#   documents.json search, SSE snippet errors silently, no console noise
```
