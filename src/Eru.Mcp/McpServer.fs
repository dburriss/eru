module Eru.Mcp.Server

open Eru
open Eru.Adapters
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Serilog

let run (deps: Deps) : System.Threading.Tasks.Task<unit> =
    task {
        let globalCfgOpt =
            match deps.ReadGlobalConfig() with
            | Ok cfgOpt -> cfgOpt
            | _         -> None

        let localCfgOpt =
            match deps.ReadLocalConfig() with
            | Ok cfgOpt -> cfgOpt
            | Error _   -> None

        let eff =
            Config.merge globalCfgOpt localCfgOpt
            |> Result.defaultWith (fun _ ->
                { Sources                   = []
                  CommitOnPull              = false
                  StateFile                 = "eru.lock"
                  Collections               = []
                  McpRefreshIntervalMinutes = 60
                  BlockPatterns             = Config.defaultBlockPatterns
                  AllowPatterns             = Config.defaultAllowPatterns
                  AllowBinaries             = Config.defaultAllowBinaries })
            |> Config.withManifests deps.ReadCachedManifest

        for src in eff.Sources do
            match src.Url with
            | None -> ()
            | Some url ->
                match GitAdapter.checkRemoteAccess url with
                | Ok ()   -> ()
                | Error e -> eprintfn "[eru] WARNING: source '%s' (%s) is not accessible: %s" src.Name url e

        let logFile = Paths.mcpLogPath ()
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName logFile) |> ignore
        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.Warning()
                .WriteTo.File(
                    logFile,
                    rollingInterval        = RollingInterval.Day,
                    retainedFileCountLimit = System.Nullable 7,
                    outputTemplate         = "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger()

        let builder = Host.CreateApplicationBuilder()
        builder.Logging.ClearProviders() |> ignore
        builder.Logging.AddSerilog(dispose = true) |> ignore
        builder.Services
            .AddSingleton<Deps>(deps)
            .AddSingleton<EffectiveConfig>(eff)
            .AddSingleton<KnowledgeSyncService>()
            .AddHostedService<IndexService>()
            .AddHostedService<CollectionCacheService>()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof<KnowledgeTools>.Assembly)
            .WithResourcesFromAssembly(typeof<SourceResources>.Assembly)
        |> ignore

        do! builder.Build().RunAsync()
    }
