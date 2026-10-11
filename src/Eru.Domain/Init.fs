namespace Eru

module Init =

    type Command = { Force: bool; Path: string option; IsGlobal: bool }

    let private scaffold = """{
  "version": 3,
  "site": {
    "ignorePatterns": ["index.md", "log.md", "README.md"],
    "hideEmptyBundles": null
  },
  "okf": {
    "ignorePatterns": ["apm_modules/**", "inbox/**", "node_modules/**"]
  },
  "sync": {
    "commitOnPull": null,
    "stateFile": null
  },
  "files": {
    "blockPatterns": null,
    "allowPatterns": null,
    "allowBinaries": null
  },
  "inbox": {
    "default": null,
    "watchIntervalSeconds": null,
    "items": {}
  },
  "source": { "items": [] },
  "collection": { "items": [] }
}
"""

    let private emptyGlobal : GlobalConfig =
        { GlobalConfig.empty with
            Site = Some { SiteSection.empty with IgnorePatterns = Some Config.defaultSiteIgnorePatterns }
            Okf  = Some { IgnorePatterns = Some Config.defaultOkfIgnorePatterns }
            Files =
                Some { BlockPatterns = Some Config.defaultBlockPatterns
                       AllowPatterns = Some Config.defaultAllowPatterns
                       AllowBinaries = Some Config.defaultAllowBinaries } }

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
