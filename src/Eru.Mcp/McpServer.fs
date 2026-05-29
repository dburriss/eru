module Eru.Mcp.Server

open Eru
open Eru.Adapters
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

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

        let builder = Host.CreateApplicationBuilder()
        builder.Logging.ClearProviders() |> ignore
        builder.Services
            .AddSingleton<Deps>(deps)
            .AddSingleton<EffectiveConfig>(eff)
            .AddSingleton<KnowledgeSyncService>()
            .AddHostedService<IndexService>()
            .AddHostedService<CollectionCacheService>()
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof<KnowledgeTools>.Assembly)
        |> ignore

        do! builder.Build().RunAsync()
    }
