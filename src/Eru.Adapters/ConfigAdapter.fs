namespace Eru.Adapters

open Eru
open System.IO

module ConfigAdapter =

    let readGlobalConfig () : Result<GlobalConfig option, string> =
        let path = Paths.globalConfigPath ()
        if not (File.Exists path) then Ok None
        else
            try
                let json = File.ReadAllText path
                Config.readAndMigrateGlobalJson
                    Serialization.deserialize<GlobalConfig>
                    Serialization.deserialize<GlobalConfigV2>
                    Serialization.deserialize<GlobalConfigV1>
                    json
                |> Result.map Some
            with ex -> Error ex.Message

    let readLocalConfig (cwd: string) : Result<LocalConfig option, string> =
        let path = Paths.localConfigPath cwd
        if not (File.Exists path) then Ok None
        else
            try
                let json = File.ReadAllText path
                Config.readAndMigrateLocalJson
                    Serialization.deserialize<LocalConfig>
                    Serialization.deserialize<LocalConfigV2>
                    Serialization.deserialize<LocalConfigV1>
                    json
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

    let private readRaw (path: string) : Result<string option, string> =
        try
            if File.Exists path then Ok (Some (File.ReadAllText path)) else Ok None
        with ex -> Error ex.Message

    let private writeRaw (path: string) (content: string) : Result<unit, string> =
        try
            let dir = System.IO.Path.GetDirectoryName path
            if dir <> null && dir <> "" then System.IO.Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, content)
            Ok ()
        with ex -> Error ex.Message

    // Raw-file access for `eru config`, which needs the on-disk text and version.
    let configIO (cwd: string) : ConfigIO =
        {
            Path = (function
                | LocalScope  -> Paths.localConfigPath cwd
                | GlobalScope -> Paths.globalConfigPath ())
            ReadRaw  = readRaw
            WriteRaw = writeRaw
            ParseGlobal =
                Config.readAndMigrateGlobalJson
                    Serialization.deserialize<GlobalConfig>
                    Serialization.deserialize<GlobalConfigV2>
                    Serialization.deserialize<GlobalConfigV1>
            ParseLocal =
                Config.readAndMigrateLocalJson
                    Serialization.deserialize<LocalConfig>
                    Serialization.deserialize<LocalConfigV2>
                    Serialization.deserialize<LocalConfigV1>
            SerializeGlobal = Serialization.serialize<GlobalConfig>
            SerializeLocal  = Serialization.serialize<LocalConfig>
        }
