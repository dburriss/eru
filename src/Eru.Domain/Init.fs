namespace Eru

module Init =

    type Command = { Force: bool; Path: string option; IsGlobal: bool }

    let private scaffold = """{
  "version": 1,
  "sources": [],
  "collections": [],
  "settings": {
    "commitOnPull": null,
    "stateFile": null,
    "blockPatterns": null,
    "allowPatterns": null,
    "allowBinaries": null
  }
}
"""

    let private emptyGlobal : GlobalConfig =
        { Version = 1
          DefaultSources = []
          Collections = []
          Defaults = Some {
              Branch = None
              CommitOnPull = None
              McpRefreshIntervalMinutes = None
              BlockPatterns = Some Config.defaultBlockPatterns
              AllowPatterns = Some Config.defaultAllowPatterns
              AllowBinaries = Some Config.defaultAllowBinaries
          }
        }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        if cmd.IsGlobal && cmd.Path.IsSome then
            Error "--global and a path are mutually exclusive."
        elif cmd.IsGlobal then
            if not cmd.Force then
                match deps.ReadGlobalConfig() with
                | Error e     -> Error e
                | Ok (Some _) -> Error "Global config already exists. Use --force to overwrite."
                | Ok None     ->
                    match deps.WriteGlobalConfig emptyGlobal with
                    | Ok ()   -> Ok "Initialized global eru config."
                    | Error e -> Error e
            else
                match deps.WriteGlobalConfig emptyGlobal with
                | Ok ()   -> Ok "Initialized global eru config."
                | Error e -> Error e
        else
            let dir        = cmd.Path |> Option.defaultValue (deps.GetCwd())
            let configPath = System.IO.Path.Combine(dir, ".eru", "config.json")

            if System.IO.File.Exists configPath && not cmd.Force then
                Error ".eru/config.json already exists. Use --force to overwrite."
            else
                match deps.WriteLocalFile configPath scaffold with
                | Ok ()   -> Ok $"Initialized .eru/config.json in {dir}"
                | Error e -> Error e
