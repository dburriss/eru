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

    let writeLocalConfig (cwd: string) (cfg: LocalConfig) : Result<unit, string> =
        let path = Paths.localConfigPath cwd
        try
            File.WriteAllText(path, Serialization.serialize cfg)
            Ok ()
        with ex -> Error ex.Message

    let writeGlobalConfig (cfg: GlobalConfig) : Result<unit, string> =
        let path = Paths.globalConfigPath ()
        try
            let dir = System.IO.Path.GetDirectoryName path
            if dir <> null && dir <> "" then System.IO.Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, Serialization.serialize cfg)
            Ok ()
        with ex -> Error ex.Message
