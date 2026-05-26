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
        IO.Path.Combine(cwd, "eru.json")

    let lockFilePath (cwd: string) (stateFile: string option) =
        IO.Path.Combine(cwd, stateFile |> Option.defaultValue "eru.lock")
