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

    /// Themes installed for one project: `<cwd>/.eru/themes/<name>/`.
    let localThemesDir (cwd: string) =
        Eru.PathJoin.Combine(cwd, ".eru", "themes")

    /// Themes installed for the user: `<config dir>/eru/themes/<name>/`, next to the global config.
    let userThemesDir () =
        IO.Path.Combine(IO.Path.GetDirectoryName(globalConfigPath ()), "themes")

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

    // Ferret's indexes (lexical and semantic) and the downloaded embedding model.
    let ferretCacheDir () =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
            IO.Path.Combine(localAppData, "eru", "ferret")
        else
            let xdgCache = Environment.GetEnvironmentVariable "XDG_CACHE_HOME"
            let cacheHome =
                if xdgCache <> null && xdgCache <> "" then xdgCache
                else IO.Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".cache")
            IO.Path.Combine(cacheHome, "eru", "ferret")

    // The per-file word index of the removed built-in search. Only `eru cache clear` still
    // knows about it, to remove what an older version left behind.
    let legacySearchIndexDir () =
        IO.Path.Combine(IO.Path.GetDirectoryName(ferretCacheDir ()), "index")

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

    // Root of eru's per-user cache: %LOCALAPPDATA%\eru, or $XDG_CACHE_HOME/eru (default ~/.cache/eru).
    let eruCacheDir () =
        IO.Path.GetDirectoryName(mcpLogPath ())

    // Held (exclusively, by the OS) while one process runs a background sync, so only one runs at a time.
    let syncLockPath () = IO.Path.Combine(eruCacheDir (), "sync.lock")

    // Touched after each successful background sync; its mtime throttles the other servers' timers.
    let lastSyncPath () = IO.Path.Combine(eruCacheDir (), "last-sync")

    // One <pid>.json per running `eru mcp`, so an older server can see that a newer eru is running.
    let mcpRegistryDir () = IO.Path.Combine(eruCacheDir (), "mcp")
