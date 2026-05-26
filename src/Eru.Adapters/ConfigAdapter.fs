namespace Eru.Adapters

open Eru
open System.IO

module ConfigAdapter =

    let readGlobalConfig () : Result<GlobalConfig option, string> =
        let path = Paths.globalConfigPath ()
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<GlobalConfig>
                |> Result.map Some
            with ex -> Error ex.Message

    let readLocalConfig (cwd: string) : Result<LocalConfig option, string> =
        let path = Paths.localConfigPath cwd
        if not (File.Exists path) then Ok None
        else
            try
                File.ReadAllText path
                |> Serialization.deserialize<LocalConfig>
                |> Result.map Some
            with ex -> Error ex.Message
