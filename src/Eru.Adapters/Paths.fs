namespace Eru.Adapters

open System
open System.Runtime.InteropServices

module Paths =

    let globalConfigPath () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            let appData = Environment.GetFolderPath Environment.SpecialFolder.ApplicationData
            IO.Path.Combine(appData, "eru", "config.json")
        else
            let xdgConfig = Environment.GetEnvironmentVariable "XDG_CONFIG_HOME"
            let configHome =
                if xdgConfig <> null && xdgConfig <> "" then xdgConfig
                else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".config")
            IO.Path.Combine(configHome, "eru", "config.json")

    let localConfigPath (cwd: string) =
        IO.Path.Combine(cwd, ".eru", "config.json")

    let localManifestPath (cwd: string) =
        IO.Path.Combine(cwd, ".eru", "manifest.json")

    let lockFilePath (cwd: string) (stateFile: string option) =
        IO.Path.Combine(cwd, ".eru", stateFile |> Option.defaultValue "eru.lock")

    let collectionCachePath () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
            IO.Path.Combine(localAppData, "eru", "collections")
        else
            let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
            let cacheHome =
                if xdgCache <> null && xdgCache <> "" then xdgCache
                else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
            IO.Path.Combine(cacheHome, "eru", "collections")

    let sourceCacheManifestPath (sourceName: string) =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
            IO.Path.Combine(localAppData, "eru", "sources", sourceName, "manifest.json")
        else
            let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
            let cacheHome =
                if xdgCache <> null && xdgCache <> "" then xdgCache
                else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
            IO.Path.Combine(cacheHome, "eru", "sources", sourceName, "manifest.json")

    let sourceCacheDir (sourceName: string) =
        IO.Path.GetDirectoryName(sourceCacheManifestPath sourceName)

    let sourceIndexPath (sourceName: string) =
        IO.Path.Combine(sourceCacheDir sourceName, "index.json")

    let sourceFilesDir (sourceName: string) =
        IO.Path.Combine(sourceCacheDir sourceName, "files")

    let searchIndexDir () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
            IO.Path.Combine(localAppData, "eru", "index")
        else
            let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
            let cacheHome =
                if xdgCache <> null && xdgCache <> "" then xdgCache
                else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
            IO.Path.Combine(cacheHome, "eru", "index")

    let mcpLogPath () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
            IO.Path.Combine(localAppData, "eru", "mcp.log")
        else
            let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
            let cacheHome =
                if xdgCache <> null && xdgCache <> "" then xdgCache
                else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
            IO.Path.Combine(cacheHome, "eru", "mcp.log")
