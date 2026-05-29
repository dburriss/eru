# Plan: Fire-and-Forget `refresh_knowledge` with Serilog File Logging

## Context

`refresh_knowledge` blocks the MCP response thread while `KnowledgeSyncService.Sync()` performs multiple `git clone --sparse` network calls. This causes the MCP client to hit its request timeout (`-32001: Request timed out`). The tool should return immediately after kicking off the sync in the background. Since the MCP server runs on stdio, console logging is not available — errors from background syncs must go to a rolling log file via `ILogger` / Serilog.

---

## Design

Add a `TriggerBackgroundSync()` method to `KnowledgeSyncService` that fires the sync on a thread-pool thread and returns immediately. An `Interlocked.CompareExchange` flag prevents concurrent syncs. `ILogger<KnowledgeSyncService>` (resolved from DI) is used for error and exception logging, backed by a Serilog rolling-file sink configured in `McpServer.fs`. Both `Refresh()` and `CollectionCacheService` go through the new method so the guard is global.

---

## New packages — `src/Eru.Mcp/Eru.Mcp.fsproj`

```xml
<PackageReference Include="Serilog.Extensions.Logging" Version="9.*" />
<PackageReference Include="Serilog.Sinks.File" Version="6.*" />
```

`Serilog.Extensions.Logging` bridges Serilog to `Microsoft.Extensions.Logging.ILogger`. `Serilog.Sinks.File` provides rolling-file output with retention.

---

## Changes

### 1. `src/Eru.Adapters/Paths.fs` — add log path helper

Add `mcpLogPath ()` following the existing XDG branching pattern (same as `collectionCachePath`):

- Unix: `$XDG_CACHE_HOME/eru/mcp-.log` or `~/.cache/eru/mcp-.log`
- Windows: `%LOCALAPPDATA%\eru\mcp-.log`

The `-` suffix is a placeholder where Serilog inserts the rolling date token (e.g. `mcp-20260529.log`). Use `Serilog.Sinks.File`'s date token convention: pass the path with no date token; Serilog appends the date when `rollingInterval` is set.

Actually: pass the base path `~/.cache/eru/mcp.log` and Serilog will produce `mcp-20260529.log` automatically with `rollingInterval = RollingInterval.Day`. No suffix token needed in the path.

```fsharp
let mcpLogPath () =
    // same XDG branching as collectionCachePath, but returns the log file path
    // Unix: ~/.cache/eru/mcp.log  |  Windows: %LOCALAPPDATA%\eru\mcp.log
```

### 2. `src/Eru.Mcp/McpServer.fs` — configure Serilog

Add `open Serilog` and configure the logger before building the host. Replace the bare `ClearProviders()` call with:

```fsharp
open Serilog

// in run(), after computing eff:
let logFile = Paths.mcpLogPath ()
Directory.CreateDirectory(Path.GetDirectoryName logFile) |> ignore
Log.Logger <-
    LoggerConfiguration()
        .WriteTo.File(
            logFile,
            rollingInterval          = RollingInterval.Day,
            retainedFileCountLimit   = System.Nullable 7,
            outputTemplate           = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .MinimumLevel.Warning()
        .CreateLogger()

builder.Logging.ClearProviders() |> ignore
builder.Logging.AddSerilog(dispose = true) |> ignore
```

`MinimumLevel.Warning` keeps the log file focused on errors and warnings only.

### 3. `src/Eru.Mcp/KnowledgeSyncService.fs`

**Constructor**: add `ILogger<KnowledgeSyncService>` parameter:

```fsharp
type KnowledgeSyncService(deps: Deps, startupEff: EffectiveConfig, logger: ILogger<KnowledgeSyncService>) =
```

**Dedup flag** alongside `currentEff`:

```fsharp
let mutable syncRunning = 0   // 0 = idle, 1 = running; Interlocked-guarded
```

**Volatile access on `currentEff`** (add `open System.Threading`):

- `CurrentEff` getter: `Volatile.Read(&currentEff)`
- Assignment at end of `Sync()`: `Volatile.Write(&currentEff, freshEff)`

**`TriggerBackgroundSync()` member**:

```fsharp
member this.TriggerBackgroundSync() : bool =
    if Interlocked.CompareExchange(&syncRunning, 1, 0) = 0 then
        Task.Run(fun () ->
            try
                try
                    let result = this.Sync()
                    if result.Errors <> [] then
                        logger.LogWarning("Sync completed with errors: {Errors}", String.concat "; " result.Errors)
                with ex ->
                    logger.LogError(ex, "Sync threw an unhandled exception")
            finally
                Interlocked.Exchange(&syncRunning, 0) |> ignore) |> ignore
        true
    else
        false
```

Requires `open System.Threading` and `open System.Threading.Tasks`.

### 4. `src/Eru.Mcp/McpTools.fs`

Replace `Refresh()` body — return type stays `string`:

```fsharp
[<McpServerTool(Name = "refresh_knowledge")>]
[<Description("Trigger a background refresh of the knowledge cache. Returns immediately; errors are written to the eru log file.")>]
member _.Refresh() : string =
    if syncService.TriggerBackgroundSync() then
        "Knowledge refresh started in the background."
    else
        "A knowledge refresh is already in progress."
```

### 5. `src/Eru.Mcp/CollectionCacheService.fs`

Replace both `sync.Sync() |> ignore` calls with `sync.TriggerBackgroundSync() |> ignore`:

```fsharp
override _.ExecuteAsync(ct: CancellationToken) =
    task {
        sync.TriggerBackgroundSync() |> ignore
        use timer = new PeriodicTimer(System.TimeSpan.FromMinutes(float sync.CurrentEff.McpRefreshIntervalMinutes))
        while! timer.WaitForNextTickAsync(ct) do
            sync.TriggerBackgroundSync() |> ignore
    }
```

---

## Log file location

- Unix: `~/.cache/eru/mcp.log` → rolling files: `mcp-20260529.log`, etc. (respects `$XDG_CACHE_HOME`)
- Windows: `%LOCALAPPDATA%\eru\mcp.log`
- Retention: 7 daily files
- Level: Warning and above only

---

## Verification

1. `dotnet build` — no errors
2. `dotnet test` — all tests pass
3. Restart MCP server, call `refresh_knowledge` — returns in under a second, no timeout
4. Check `~/.cache/eru/mcp-<date>.log` — file exists; errors from failed syncs appear
5. Call `refresh_knowledge` twice rapidly — second returns "already in progress"
6. Wait for timer tick — log confirms timer path also uses the dedup guard
