module Eru.Mcp.Server

open Eru
open Eru.Adapters
open Eru.Search
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
            DepsConfig.merge deps globalCfgOpt localCfgOpt
            |> Result.defaultWith (fun _ ->
                EffectiveConfig.empty)
            |> Config.withManifests deps.ReadCachedManifest

        for src in eff.Sources do
            match src.Url with
            | None -> ()
            | Some url when FolderAdapter.isPlainFolder url -> ()
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
            .AddSingleton<SyncGate>(SyncGate.files)
            .AddSingleton<KnowledgeSyncService>()
            .AddSingleton<RankedSearch.Searcher>(fun _ -> new RankedSearch.Searcher(Paths.ferretCacheDir()))
            .AddHostedService<IndexService>()
            .AddHostedService<CollectionCacheService>()
            .AddHostedService<SupersedeService>()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof<KnowledgeTools>.Assembly)
            .WithResourcesFromAssembly(typeof<SourceResources>.Assembly)
        |> ignore

        do! builder.Build().RunAsync()
    }
